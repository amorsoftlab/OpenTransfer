using System;

namespace openTransferWPF.Models
{
    public class DiskUsageItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public long TotalParentSizeBytes { get; set; }
        public bool IsDir { get; set; }
        public int Rank { get; set; }

        public string Icon => IsDir ? "📁" : GetFileIcon(Name);

        public double UsagePercentage => TotalParentSizeBytes > 0
            ? Math.Min(100.0, Math.Max(0.0, (SizeBytes * 100.0 / TotalParentSizeBytes)))
            : 0;

        public string FormattedPercentage => $"{UsagePercentage:0.#}%";

        public string FormattedSize
        {
            get
            {
                if (SizeBytes < 0) return "--";
                if (SizeBytes < 1024) return $"{SizeBytes} B";
                if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:0.#} KB";
                if (SizeBytes < 1024 * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024.0):0.#} MB";
                return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";
            }
        }

        public string RankBadge => Rank switch
        {
            1 => "🥇 #1",
            2 => "🥈 #2",
            3 => "🥉 #3",
            _ => $"#{Rank}"
        };

        public string RankColor => Rank switch
        {
            1 => "#D97706",
            2 => "#4B5563",
            3 => "#B45309",
            _ => "#9CA3AF"
        };

        private static string GetFileIcon(string filename)
        {
            string ext = System.IO.Path.GetExtension(filename).ToLower();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp") return "🖼️";
            if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm") return "🎬";
            if (ext is ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a") return "🎵";
            if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz") return "📦";
            if (ext is ".pdf" or ".doc" or ".docx" or ".txt") return "📄";
            if (ext is ".apk") return "🤖";
            return "📄";
        }
    }
}
