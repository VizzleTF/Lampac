﻿using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Web;
using Newtonsoft.Json.Linq;
using System.Linq;
using Lampac.Engine;
using Lampac.Engine.CORE;
using System.Security.Cryptography;
using System.Text;
using Lampac.Models.LITE.Kodik;

namespace Lampac.Controllers.LITE
{
    public class Kodik : BaseController
    {
        [HttpGet]
        [Route("lite/kodik")]
        async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, string kid, string t, int s = -1)
        {
            JToken results = await search(memoryCache, imdb_id, kinopoisk_id);
            if (results == null)
                return Content(string.Empty);

            bool firstjson = true;
            string html = "<div class=\"videos__line\">";

            if (results[0].Value<string>("type") is "foreign-movie" or "soviet-cartoon" or "foreign-cartoon" or "russian-cartoon" or "anime" or "russian-movie")
            {
                #region Фильм
                foreach (var data in results)
                {
                    string link = data.Value<string>("link");
                    string voice = data.Value<JObject>("translation").Value<string>("title");

                    string url = $"{AppInit.Host(HttpContext)}/lite/kodik/video?title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&link={HttpUtility.UrlEncode(link)}";

                    html += "<div class=\"videos__item videos__movie selector " + (firstjson ? "focused" : "") + "\" media=\"\" data-json='{\"method\":\"call\",\"url\":\"" + url + "\",\"title\":\"" + $"{title ?? original_title} ({voice})" + "\"}'><div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">" + voice + "</div></div>";
                    firstjson = false;
                }
                #endregion
            }
            else
            {
                #region Перевод hash
                HashSet<string> translations = new HashSet<string>();

                foreach (var item in results)
                {
                    string translation = item.Value<JObject>("translation").Value<string>("title");
                    if (!string.IsNullOrWhiteSpace(translation))
                        translations.Add(translation);
                }
                #endregion

                #region Перевод html
                string activTranslate = t;

                foreach (var translation in translations)
                {
                    if (string.IsNullOrWhiteSpace(activTranslate))
                        activTranslate = translation;

                    string link = $"{AppInit.Host(HttpContext)}/lite/kodik?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&t={HttpUtility.UrlEncode(translation)}";

                    string active = string.IsNullOrWhiteSpace(t) ? (firstjson ? "active" : "") : (t == translation ? "active" : "");

                    html += "<div class=\"videos__button selector " + active + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'>" + translation + "</div>";
                    firstjson = false;
                }

                html += "</div>";
                #endregion

                #region Сериал
                firstjson = true;
                html += "<div class=\"videos__line\">";

                if (s == -1)
                {
                    foreach (var item in results.Reverse())
                    {
                        if (item.Value<JObject>("translation").Value<string>("title") != activTranslate)
                            continue;

                        int season = item.Value<int>("last_season");
                        string link = $"{AppInit.Host(HttpContext)}/lite/kodik?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&t={HttpUtility.UrlEncode(activTranslate)}&s={season}&kid={item.Value<string>("id")}";

                        html += "<div class=\"videos__item videos__season selector " + (firstjson ? "focused" : "") + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'><div class=\"videos__season-layers\"></div><div class=\"videos__item-imgbox videos__season-imgbox\"><div class=\"videos__item-title videos__season-title\">" + $"{season} сезон" + "</div></div></div>";
                        firstjson = false;
                    }
                }
                else
                {
                    foreach (var item in results)
                    {
                        if (item.Value<string>("id") != kid)
                            continue;

                        foreach (var episode in item.Value<JObject>("seasons").ToObject<Dictionary<string, Season>>().First().Value.episodes)
                        {
                            string url = $"{AppInit.Host(HttpContext)}/lite/kodik/video?title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&link={HttpUtility.UrlEncode(episode.Value)}";

                            html += "<div class=\"videos__item videos__movie selector " + (firstjson ? "focused" : "") + "\" media=\"\" s=\"" + s + "\" e=\"" + episode.Key + "\" data-json='{\"method\":\"call\",\"url\":\"" + url + "\",\"title\":\"" + $"{title ?? original_title} ({episode.Key} серия)" + "\"}'><div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">" + $"{episode.Key} серия" + "</div></div>";
                            firstjson = false;
                        }

                        break;
                    }
                }
                #endregion
            }

            return Content(html + "</div>", "text/html; charset=utf-8");
        }

        #region Video
        [HttpGet]
        [Route("lite/kodik/video")]
        async public Task<ActionResult> Video(string title, string original_title, string link)
        {
            string memKey = $"kodik:view:stream:{link}";
            if (!memoryCache.TryGetValue(memKey, out List<(string q, string url)> streams))
            {
                string userIp = HttpContext.Connection.RemoteIpAddress.ToString();
                if (AppInit.conf.Kodik.localip)
                {
                    userIp = await mylocalip();
                    if (userIp == null)
                        return Content(string.Empty);
                }

                string deadline = DateTime.Now.AddHours(1).ToString("yyyy MM dd HH").Replace(" ", "");
                string hmac = HMAC(AppInit.conf.Kodik.secret_token, $"{link}:{userIp}:{deadline}");

                string json = await HttpClient.Get($"{AppInit.conf.Kodik.linkhost}/api/video-links" + $"?link={link}&p={AppInit.conf.Kodik.token}&ip={userIp}&d={deadline}&s={hmac}", timeoutSeconds: 8);

                streams = new List<(string q, string url)>();
                var match = new Regex("\"([0-9]+)p?\":{\"Src\":\"(https?:)?//([^\"]+)\"", RegexOptions.IgnoreCase).Match(json);
                while (match.Success)
                {
                    if (!string.IsNullOrWhiteSpace(match.Groups[3].Value))
                        streams.Insert(0, ($"{match.Groups[1].Value}p", $"http://{match.Groups[3].Value}"));

                    match = match.NextMatch();
                }

                memoryCache.Set(memKey, streams, TimeSpan.FromHours(1));
            }

            string streansquality = string.Empty;
            foreach (var l in streams)
                streansquality += $"\"{l.q}\":\"" + l.url + "\",";

            return Content("{\"method\":\"play\",\"url\":\"" + streams[0].url + "\",\"title\":\"" + (title ?? original_title) + "\", \"quality\": {" + Regex.Replace(streansquality, ",$", "") + "}}", "application/json; charset=utf-8");
        }
        #endregion


        #region search
        async ValueTask<JToken> search(IMemoryCache memoryCache, string imdb_id, long kinopoisk_id)
        {
            string memKey = $"kodik:view:{kinopoisk_id}:{imdb_id}";

            if (!memoryCache.TryGetValue(memKey, out JToken results))
            {
                string url = $"{AppInit.conf.Kodik.apihost}/search?token={AppInit.conf.Kodik.token}&limit=100&with_episodes=true";
                if (kinopoisk_id > 0)
                    url += $"&kinopoisk_id={kinopoisk_id}";

                if (!string.IsNullOrWhiteSpace(imdb_id))
                    url += $"&imdb_id={imdb_id}";

                var root = await HttpClient.Get<JObject>(url, timeoutSeconds: 8);
                if (root == null || !root.ContainsKey("results"))
                    return null;

                results = root.GetValue("results");
                if (results.Count() == 0)
                    return null;

                memoryCache.Set(memKey, results, TimeSpan.FromMinutes(10));
            }

            return results;
        }
        #endregion

        #region HMAC
        static string HMAC(string key, string message)
        {
            using (var hash = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(message))).Replace("-", "").ToLower();
            }
        }
        #endregion
    }
}