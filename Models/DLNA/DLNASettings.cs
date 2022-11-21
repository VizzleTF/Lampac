﻿namespace Lampac.Models.DLNA
{
    public class DLNASettings
    {
        public bool enable { get; set; }

        public string mode { get; set; }

        public int downloadSpeed { get; set; }

        public int maximumDiskReadRate { get; set; }

        public int maximumDiskWriteRate { get; set; }
    }
}