namespace openTransferWPF.Models
{
    public enum TransferStatus
    {
        Waiting,
        Copying,
        Completed,
        Skipped,
        Failed
    }

    public class TransferQueueItem
    {
        public string RemotePath { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public TransferStatus Status { get; set; } = TransferStatus.Waiting;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
