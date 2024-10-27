﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Online;
using Shared.Model.Online.VDBmovies;
using Lampac.Engine.CORE;
using Shared.Engine.Online;
using Shared.Engine.CORE;
using System.Collections.Generic;
using Shared.Model.Online;

namespace Lampac.Controllers.LITE
{
    public class FanCDN : BaseOnlineController
    {
        [HttpGet]
        [Route("lite/fancdn")]
        async public Task<ActionResult> Index(string title, string original_title, int year, bool rjson = false)
        {
            var init = AppInit.conf.FanCDN.Clone();

            if (!init.enable || string.IsNullOrEmpty(init.cookie) || init.rhub || string.IsNullOrEmpty(original_title) || year == 0)
                return OnError();

            var baseheader = HeadersModel.Init(
                ("cache-control", "no-cache"),
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("pragma", "no-cache"),
                ("priority", "u=0, i"),
                ("sec-ch-ua", "\"Google Chrome\";v=\"129\", \"Not = A ? Brand\";v=\"8\", \"Chromium\";v=\"129\""),
                ("sec-ch-ua-mobile", "?0"),
                ("sec-ch-ua-platform", "\"Windows\""),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "none"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1"),
                ("cookie", init.cookie)
            );

            var proxyManager = new ProxyManager("fancdn", init);
            var proxy = proxyManager.Get();

            var oninvk = new FanCDNInvoke
            (
               host,
               init.corsHost(),
               ongettourl => 
               {
                   var headers = httpHeaders(init, baseheader);
                   if (ongettourl.Contains("fancdn."))
                       headers.Add(new HeadersModel("referer", $"{init.host}/"));

                   return HttpClient.Get(init.cors(ongettourl), timeoutSeconds: 8, proxy: proxy, headers: headers, httpversion: 2);
               },
               streamfile => HostStreamProxy(init, streamfile, proxy: proxy, plugin: "fancdn")
            );

            var cache = await InvokeCache<List<Episode>>($"fancdn:{original_title}:{year}", cacheTime(20, init: init), proxyManager, async res =>
            {
                return await oninvk.Embed(title, original_title, year);
            });

            return OnResult(cache, () => oninvk.Html(cache.Value, title, original_title), rjson: rjson);
        }
    }
}