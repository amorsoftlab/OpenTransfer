using System;

namespace openTransferWPF.Models
{
    public class AppSettings
    {
        // --- GENERAL ---
        public bool LaunchAtStartup { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool AutoDetectDevices { get; set; } = true;
        public bool AutoReconnect { get; set; } = true;

        // --- APPEARANCE ---
        public string Theme { get; set; } = "System"; // "System", "Light", "Dark"
        public string AccentColor { get; set; } = "#005fb8";
        public string FolderColor { get; set; } = "Yellow"; // "Yellow", "Blue", "Green", "Purple", "Red", "Orange"
        public string Language { get; set; } = "English"; // "English", "Malayalam"

        // --- TRANSFERS ---
        public string ConflictResolution { get; set; } = "Skip"; // "Ask", "Skip", "Replace", "Rename"
        public string CompareMethod { get; set; } = "FilenameSize"; // "FilenameOnly", "FilenameSize", "FilenameSizeDate"
        public string TransferMode { get; set; } = "Balanced"; // "MaxSpeed", "Balanced", "MaxCompatibility"
        public string BufferSize { get; set; } = "Auto"; // "Auto", "512 KB", "1 MB", "2 MB", "4 MB", "8 MB"
        public bool VerifyCopiedFiles { get; set; } = true;
        public bool RetryFailedTransfers { get; set; } = true;
        public int RetryCount { get; set; } = 3;

        // --- DEVICE ---
        public string DefaultAndroidStorage { get; set; } = "/sdcard";
        public int RefreshIntervalSeconds { get; set; } = 3; // 1, 3, 5

        // --- EXPLORER ---
        public string DefaultView { get; set; } = "Details"; // "Details", "LargeIcons", "MediumIcons"
        public bool ShowHiddenFiles { get; set; } = false;
        public bool ShowFileExtensions { get; set; } = true;
        public bool ShowFolderSizes { get; set; } = false;
        public bool PreserveModifiedDate { get; set; } = true;
        public bool PreserveFolderStructure { get; set; } = true;
        public bool CreateMissingFolders { get; set; } = true;

        // --- AUTO-SPLIT ON TRANSFER ---
        public bool AutoSplitOnTransfer { get; set; } = false;
        public int AutoSplitBatchSize { get; set; } = 500; // 100, 250, 500, 1000
        public string AutoSplitNamingFormat { get; set; } = "Photo"; // "Photo" (photo-1, photo-2), "Day" (day 1-1, day 1-2)

        // --- ADVANCED ---
        public string CustomAdbPath { get; set; } = string.Empty;
        public int AdbTimeoutSeconds { get; set; } = 30; // 30, 60, 120
        public bool EnableDebugLogging { get; set; } = false;
    }
}
