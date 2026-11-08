using System;
using System.Collections.Generic;

namespace openTransferWPF.Models
{
    public class TransferHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Direction { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public int CompletedFiles { get; set; }
        public string Result { get; set; } = string.Empty;
        public List<string> FailedItems { get; set; } = new();
        public int FailedCount => FailedItems?.Count ?? 0;
        public bool CanRetry => FailedCount > 0;
        public string DisplayTime => Timestamp.ToString("yyyy-MM-dd HH:mm");
        public string Summary => FailedCount > 0
            ? $"{Direction} • {CompletedFiles}/{TotalFiles} files • {FailedCount} failed"
            : $"{Direction} • {CompletedFiles}/{TotalFiles} files • {Result}";
    }
}
