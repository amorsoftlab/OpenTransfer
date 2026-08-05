using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace openTransferWPF.Services
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = "1.2.4";
        public string ReleaseNotes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
    }

    public class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        public const string CurrentAppVersion = "1.2.4";
        private const string GitHubApiUrl = "https://api.github.com/repos/amorsoftlab/OpenTransfer/releases/latest";

        private readonly HttpClient _httpClient;

        private UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenTransfer-App", CurrentAppVersion));
        }

        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            var info = new UpdateInfo
            {
                CurrentVersion = CurrentAppVersion
            };

            try
            {
                var response = await _httpClient.GetAsync(GitHubApiUrl);
                if (!response.IsSuccessStatusCode)
                    return info;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
                string cleanVersion = tagName.TrimStart('v', 'V');
                info.LatestVersion = cleanVersion;
                info.ReleaseNotes = root.GetProperty("body").GetString() ?? "New stability and performance improvements.";
                info.HtmlUrl = root.GetProperty("html_url").GetString() ?? "https://github.com/amorsoftlab/OpenTransfer";

                // Find download asset URL
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            info.DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (IsNewerVersion(cleanVersion, CurrentAppVersion))
                {
                    info.IsUpdateAvailable = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }

            return info;
        }

        private static bool IsNewerVersion(string latestStr, string currentStr)
        {
            if (Version.TryParse(latestStr, out var latest) && Version.TryParse(currentStr, out var current))
            {
                return latest > current;
            }
            return string.Compare(latestStr, currentStr, StringComparison.OrdinalIgnoreCase) > 0;
        }

        public async Task<string> DownloadUpdateAsync(string downloadUrl, Action<long, long> progressCallback)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "OpenTransfer_Setup.exe");

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0L;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;
                progressCallback?.Invoke(totalRead, totalBytes);
            }

            return tempFile;
        }

        public void LaunchInstaller(string installerPath)
        {
            if (File.Exists(installerPath))
            {
                Process.Start(new ProcessStartInfo(installerPath)
                {
                    UseShellExecute = true
                });
                Environment.Exit(0);
            }
        }
    }
}
