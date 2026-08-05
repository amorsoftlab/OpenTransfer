using System;

namespace openTransferWPF.Models
{
    public class SplitFolderProgress
    {
        public int MovedCount { get; set; }
        public int TotalCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Percent => TotalCount > 0 ? (int)Math.Min(100, Math.Max(0, (double)MovedCount / TotalCount * 100)) : 0;
    }
}
