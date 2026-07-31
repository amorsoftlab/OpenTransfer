namespace openTransferWPF.Models
{
    public class DeviceItem
    {
        public string Serial { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public string DisplayName => $"{Model} ({Serial})";
    }
}
