using System;

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
        public string DisplayTime => Timestamp.ToString("yyyy-MM-dd HH:mm");
        public string Summary => $"{Direction} • {CompletedFiles}/{TotalFiles} files • {Result}";
    }
}
