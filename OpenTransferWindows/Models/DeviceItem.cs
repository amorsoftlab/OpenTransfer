namespace openTransferWPF.Models
{
    public class DeviceItem
    {
        public string Serial { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsWireless => Serial.Contains("_adb-tls-connect._tcp", System.StringComparison.OrdinalIgnoreCase) || Serial.Contains(':');

        public string DisplayName => string.IsNullOrEmpty(Serial) ? Model : $"{(IsWireless ? "Wi-Fi" : "USB")} • {Model} ({Serial})";
    }
}
