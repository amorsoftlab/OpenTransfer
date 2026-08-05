using System;

namespace openTransferWPF.Models
{
    public class AndroidFileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDir { get; set; }
        public long SizeBytes { get; set; }
        public string ModifiedDateStr { get; set; } = string.Empty;

        public string Icon
        {
            get; set;
        } = "📁";

        public void ResolveCategoryIcon()
        {
            if (IsDir)
            {
                Icon = "📁";
            }
            else
            {
                string ext = System.IO.Path.GetExtension(Name).ToLower();
                if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".raw" or ".cr2" or ".nef")
                    Icon = "🖼️";
                else if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm")
                    Icon = "🎬";
                else if (ext is ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a" or ".ogg")
                    Icon = "🎵";
                else if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz")
                    Icon = "📦";
                else if (ext is ".pdf" or ".doc" or ".docx" or ".txt")
                    Icon = "📄";
                else
                    Icon = "📄";
            }
        }

        public string FormattedSize
        {
            get
            {
                if (IsDir) return "--";
                if (SizeBytes < 0) return "--";
                if (SizeBytes < 1024) return $"{SizeBytes} B";
                if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:0.#} KB";
                if (SizeBytes < 1024 * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024.0):0.#} MB";
                return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";
            }
        }
    }
}
