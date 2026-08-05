using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using openTransferWPF.Models;

namespace openTransferWPF.Services
{
    public class AdbService
    {
        private const string DefaultSdkAdb = @"C:\Users\Jaseem\AppData\Local\Android\Sdk\platform-tools\adb.exe";
        public string AdbPath { get; private set; }

        public AdbService(string? customPath = null)
        {
            AdbPath = ResolveAdbPath(customPath);
        }

        public void UpdateSettings(AppSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.CustomAdbPath) && File.Exists(settings.CustomAdbPath))
            {
                AdbPath = settings.CustomAdbPath;
            }
            else
            {
                AdbPath = ResolveAdbPath(null);
            }
        }

        private string ResolveAdbPath(string? customPath)
        {
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                return customPath;

            if (File.Exists(DefaultSdkAdb))
                return DefaultSdkAdb;

            return "adb";
        }

        public async Task<bool> IsAdbAvailableAsync()
        {
            try
            {
                var result = await RunProcessAsync(AdbPath, "version");
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<DeviceItem>> GetDevicesAsync()
        {
            var devices = new List<DeviceItem>();
            var result = await RunProcessAsync(AdbPath, "devices -l");
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
                return devices;

            var lines = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("List of devices") || string.IsNullOrEmpty(trimmed))
                    continue;

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string serial = parts[0];
                    string status = parts[1];
                    string model = serial;

                    foreach (var p in parts)
                    {
                        if (p.StartsWith("model:"))
                            model = p.Substring(6).Replace("_", " ");
                    }

                    devices.Add(new DeviceItem
                    {
                        Serial = serial,
                        Status = status,
                        Model = model
                    });
                }
            }
            return devices;
        }

        public async Task<List<AndroidFileItem>> ListDirectoryAsync(string serial, string remotePath)
        {
            var items = new List<AndroidFileItem>();
            remotePath = remotePath.TrimEnd('/');
            if (string.IsNullOrEmpty(remotePath)) remotePath = "/sdcard";

            string targetCmdPath = remotePath + "/";
            string shellCmd = $"ls -la '{targetCmdPath}'";
            
            var result = await RunBase64ShellCmdAsync(serial, shellCmd);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
                return items;

            var lines = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("total ")) continue;

                var match = Regex.Match(line, @"^([d-l][rwx-]{9})\s+\d+\s+\S+\s+\S+\s+(\d+)?\s*(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})\s+(.+)$");
                if (!match.Success)
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        bool isDir = parts[0].StartsWith("d") || parts[0].StartsWith("l");
                        string name = parts[parts.Length - 1];
                        if (name == "." || name == "..") continue;
                        items.Add(new AndroidFileItem
                        {
                            Name = name,
                            Path = $"{remotePath}/{name}",
                            IsDir = isDir,
                            SizeBytes = 0,
                            ModifiedDateStr = "Unknown"
                        });
                    }
                    continue;
                }

                string perms = match.Groups[1].Value;
                string sizeStr = match.Groups[2].Value;
                string dateStr = match.Groups[3].Value;
                string nameStr = match.Groups[4].Value;

                if (nameStr == "." || nameStr == "..") continue;
                if (nameStr.Contains("->")) nameStr = nameStr.Split("->")[0].TrimEnd();

                bool isDirectory = perms.StartsWith("d") || perms.StartsWith("l");
                long size = 0;
                long.TryParse(sizeStr, out size);

                items.Add(new AndroidFileItem
                {
                    Name = nameStr,
                    Path = $"{remotePath}/{nameStr}",
                    IsDir = isDirectory,
                    SizeBytes = isDirectory ? 0 : size,
                    ModifiedDateStr = dateStr
                });
            }

            items.Sort((a, b) =>
            {
                if (a.IsDir && !b.IsDir) return -1;
                if (!a.IsDir && b.IsDir) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return items;
        }

        /// <summary>
        /// Ultra-fast remote tree scanner returning a Dictionary of (RelativePath -> SizeInBytes).
        /// Executes a single ADB find command returning path|size format.
        /// </summary>
        public async Task<Dictionary<string, long>> ScanRemoteTreeAsDictionaryAsync(string serial, string remoteRoot)
        {
            var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            remoteRoot = remoteRoot.TrimEnd('/');
            
            // Single command to retrieve path|size for all files in tree
            string statCmd = $"find '{remoteRoot}' -type f -exec stat -c '%n|%s' {{}} +";
            var result = await RunBase64ShellCmdAsync(serial, statCmd, timeoutMs: 45000);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
                return dict;

            var lines = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split('|');
                if (parts.Length >= 2 && long.TryParse(parts[1], out long size))
                {
                    string fullPath = parts[0];
                    string relPath = fullPath;
                    if (relPath.StartsWith(remoteRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        relPath = relPath.Substring(remoteRoot.Length).TrimStart('/', '\\');
                    }
                    dict[relPath] = size;
                }
            }

            return dict;
        }

        /// <summary>
        /// Single-command optimized remote tree scanner returning a list of AndroidFileItem objects.
        /// </summary>
        public async Task<List<AndroidFileItem>> ScanRemoteTreeAsync(string serial, string remoteRoot)
        {
            var files = new List<AndroidFileItem>();
            remoteRoot = remoteRoot.TrimEnd('/');
            
            string statCmd = $"find '{remoteRoot}' -type f -exec stat -c '%n|%s' {{}} +";
            var result = await RunBase64ShellCmdAsync(serial, statCmd, timeoutMs: 45000);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
                return files;

            var lines = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split('|');
                if (parts.Length >= 2)
                {
                    string fullPath = parts[0];
                    long size = 0;
                    long.TryParse(parts[1], out size);

                    files.Add(new AndroidFileItem
                    {
                        Name = Path.GetFileName(fullPath),
                        Path = fullPath,
                        IsDir = false,
                        SizeBytes = size
                    });
                }
            }

            return files;
        }

        public async Task<string> GetFreeStorageAsync(string serial)
        {
            var res = await RunBase64ShellCmdAsync(serial, "df -h /sdcard");
            if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.Stdout))
            {
                var lines = res.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 2)
                {
                    var parts = lines[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        string avail = parts[3].Trim();
                        if (avail.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
                            return $"Free {avail.Substring(0, avail.Length - 2).Trim()} GB";
                        if (avail.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
                            return $"Free {avail.Substring(0, avail.Length - 2).Trim()} MB";
                        if (avail.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
                            return $"Free {avail.Substring(0, avail.Length - 2).Trim()} KB";
                        if (avail.EndsWith("G", StringComparison.OrdinalIgnoreCase))
                            return $"Free {avail.Substring(0, avail.Length - 1).Trim()} GB";
                        if (avail.EndsWith("M", StringComparison.OrdinalIgnoreCase))
                            return $"Free {avail.Substring(0, avail.Length - 1).Trim()} MB";
                        if (avail.EndsWith("K", StringComparison.OrdinalIgnoreCase))
                            return $"Free {avail.Substring(0, avail.Length - 1).Trim()} KB";

                        return $"Free {avail}";
                    }
                }
            }
            return "Free --";
        }

        public async Task<long> GetRemoteFileSizeAsync(string serial, string remoteFilePath)
        {
            string cmd = $"stat -c '%s' '{remoteFilePath}'";
            var res = await RunBase64ShellCmdAsync(serial, cmd);
            if (res.ExitCode == 0 && long.TryParse(res.Stdout.Trim(), out long size))
            {
                return size;
            }
            return -1;
        }

        public async Task<bool> CreateDirectoryAsync(string serial, string remoteFolderPath)
        {
            string cmd = $"mkdir -p '{remoteFolderPath}'";
            var res = await RunBase64ShellCmdAsync(serial, cmd);
            return res.ExitCode == 0;
        }

        public async Task<bool> DeleteItemAsync(string serial, string remotePath)
        {
            string cmd = $"rm -rf '{remotePath}'";
            var res = await RunBase64ShellCmdAsync(serial, cmd);
            return res.ExitCode == 0;
        }

        public async Task<int> CleanEmptyFoldersAsync(string serial, string remotePath)
        {
            remotePath = remotePath.TrimEnd('/');
            if (string.IsNullOrEmpty(remotePath)) remotePath = "/sdcard";

            string script = $@"
find '{remotePath}/' -mindepth 1 -depth -type d > /data/local/tmp/dirs.txt
deleted=0
while read -r dir; do
    if rmdir ""$dir"" 2>/dev/null; then
        deleted=$((deleted+1))
    fi
done < /data/local/tmp/dirs.txt
rm -f /data/local/tmp/dirs.txt
echo $deleted".Replace("\r", "");
            
            var res = await RunBase64ShellCmdAsync(serial, script, timeoutMs: 120000);
            if (res.ExitCode == 0 && int.TryParse(res.Stdout?.Trim(), out int deletedCount))
            {
                return deletedCount;
            }
            return 0;
        }

        public async Task<bool> RenameItemAsync(string serial, string oldPath, string newPath)
        {
            string cmd = $"mv '{oldPath}' '{newPath}'";
            var res = await RunBase64ShellCmdAsync(serial, cmd);
            return res.ExitCode == 0;
        }

        /// <summary>
        /// Splits files in a target directory into batch subfolders (e.g. photo_1, photo_2 or day 1-1, day 1-2).
        /// Reports real-time progress for every mini-chunk of 25 files.
        /// </summary>
        public async Task<int> SplitFolderAsync(
            string serial,
            string targetFolderPath,
            int filesPerFolder,
            string separator,
            bool processInnerFolders,
            IProgress<SplitFolderProgress>? progress = null)
        {
            int totalMovedCount = 0;
            targetFolderPath = targetFolderPath.TrimEnd('/');

            var items = await ListDirectoryAsync(serial, targetFolderPath);
            
            var subDirs = items.Where(i => i.IsDir).ToList();
            var directFiles = items.Where(i => !i.IsDir).ToList();

            int overallTotalCount = 0;
            var workQueue = new List<(string ParentPath, string PrefixName, List<AndroidFileItem> Files)>();

            if (processInnerFolders && subDirs.Count > 0)
            {
                foreach (var subDir in subDirs)
                {
                    progress?.Report(new SplitFolderProgress { MovedCount = 0, TotalCount = 0, Message = $"🔍 Scanning subfolder '{subDir.Name}'..." });
                    var childItems = await ListDirectoryAsync(serial, subDir.Path);
                    var childFiles = childItems.Where(i => !i.IsDir).ToList();

                    if (childFiles.Count > 0)
                    {
                        workQueue.Add((subDir.Path, subDir.Name, childFiles));
                        overallTotalCount += childFiles.Count;
                    }
                }

                if (directFiles.Count > 0)
                {
                    string parentName = Path.GetFileName(targetFolderPath);
                    if (string.IsNullOrEmpty(parentName)) parentName = "folder";
                    workQueue.Add((targetFolderPath, parentName, directFiles));
                    overallTotalCount += directFiles.Count;
                }
            }
            else
            {
                string parentName = Path.GetFileName(targetFolderPath);
                if (string.IsNullOrEmpty(parentName)) parentName = "folder";
                
                var allFiles = new List<AndroidFileItem>(directFiles);
                if (allFiles.Count == 0 && subDirs.Count > 0)
                {
                    foreach (var subDir in subDirs)
                    {
                        var childItems = await ListDirectoryAsync(serial, subDir.Path);
                        allFiles.AddRange(childItems.Where(i => !i.IsDir));
                    }
                }

                if (allFiles.Count > 0)
                {
                    workQueue.Add((targetFolderPath, parentName, allFiles));
                    overallTotalCount += allFiles.Count;
                }
            }

            if (workQueue.Count == 0) return 0;

            int cumulativeMoved = 0;
            progress?.Report(new SplitFolderProgress
            {
                MovedCount = 0,
                TotalCount = overallTotalCount,
                Message = $"Starting split operation for {overallTotalCount} files..."
            });

            foreach (var item in workQueue)
            {
                int itemBatches = (int)Math.Ceiling((double)item.Files.Count / filesPerFolder);

                for (int batchIdx = 0; batchIdx < itemBatches; batchIdx++)
                {
                    string targetSubfolderName = $"{item.PrefixName}{separator}{batchIdx + 1}";
                    string targetSubfolderPath = $"{item.ParentPath}/{targetSubfolderName}";

                    await CreateDirectoryAsync(serial, targetSubfolderPath);
                    progress?.Report(new SplitFolderProgress
                    {
                        MovedCount = cumulativeMoved,
                        TotalCount = overallTotalCount,
                        Message = $"📁 Created subfolder '{targetSubfolderName}'"
                    });

                    var batchFiles = item.Files.Skip(batchIdx * filesPerFolder).Take(filesPerFolder).ToList();

                    int miniChunkSize = 25;
                    for (int i = 0; i < batchFiles.Count; i += miniChunkSize)
                    {
                        var miniBatch = batchFiles.Skip(i).Take(miniChunkSize).ToList();
                        var sb = new StringBuilder();
                        sb.Append("mv");
                        foreach (var file in miniBatch)
                        {
                            string escapedFile = file.Path.Replace("'", "'\\''");
                            sb.Append($" '{escapedFile}'");
                        }
                        string escapedDest = targetSubfolderPath.Replace("'", "'\\''");
                        sb.Append($" '{escapedDest}/'");

                        var result = await RunBase64ShellCmdAsync(serial, sb.ToString(), timeoutMs: 30000);
                        if (result.ExitCode == 0)
                        {
                            cumulativeMoved += miniBatch.Count;
                            totalMovedCount += miniBatch.Count;
                        }
                        else
                        {
                            foreach (var file in miniBatch)
                            {
                                string destPath = $"{targetSubfolderPath}/{file.Name}";
                                bool ok = await RenameItemAsync(serial, file.Path, destPath);
                                if (ok)
                                {
                                    cumulativeMoved++;
                                    totalMovedCount++;
                                }
                            }
                        }

                        progress?.Report(new SplitFolderProgress
                        {
                            MovedCount = cumulativeMoved,
                            TotalCount = overallTotalCount,
                            Message = $"➡️ Moved into '{targetSubfolderName}' ({cumulativeMoved}/{overallTotalCount})"
                        });
                    }
                }
            }

            return totalMovedCount;
        }

        public async Task<bool> PullFileAsync(string serial, string remoteFile, string localDestFile)
        {
            string dir = Path.GetDirectoryName(localDestFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string args = $"-s {serial} pull \"{remoteFile}\" \"{localDestFile}\"";
            var result = await RunProcessAsync(AdbPath, args, timeoutMs: 60000);
            return result.ExitCode == 0;
        }

        /// <summary>
        /// Pulls a file from Android while reporting byte-level progress by polling local file size.
        /// </summary>
        public async Task<bool> PullFileWithProgressAsync(
            string serial,
            string remoteFile,
            string localDestFile,
            long expectedSize,
            IProgress<long> bytesProgress,
            CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(localDestFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string args = $"-s {serial} pull \"{remoteFile}\" \"{localDestFile}\"";
            
            var psi = new ProcessStartInfo
            {
                FileName = AdbPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            // Poll destination file size on disk while adb process runs
            while (!proc.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(localDestFile))
                {
                    try
                    {
                        long curSize = new FileInfo(localDestFile).Length;
                        bytesProgress.Report(curSize);
                    }
                    catch { }
                }
                await Task.Delay(100, ct);
            }

            await proc.WaitForExitAsync(ct);

            if (File.Exists(localDestFile))
            {
                try
                {
                    bytesProgress.Report(new FileInfo(localDestFile).Length);
                }
                catch { }
            }

            return proc.ExitCode == 0;
        }

        public async Task<bool> PushFileAsync(string serial, string localFile, string remoteDestFile)
        {
            string args = $"-s {serial} push \"{localFile}\" \"{remoteDestFile}\"";
            var result = await RunProcessAsync(AdbPath, args, timeoutMs: 60000);
            return result.ExitCode == 0;
        }

        /// <summary>
        /// Pushes a file to Android while reading stderr progress lines for byte-level progress reporting.
        /// </summary>
        public async Task<bool> PushFileWithProgressAsync(
            string serial,
            string localFile,
            string remoteDestFile,
            long fileSize,
            IProgress<long> bytesProgress,
            CancellationToken ct)
        {
            string args = $"-s {serial} push \"{localFile}\" \"{remoteDestFile}\"";
            
            var psi = new ProcessStartInfo
            {
                FileName = AdbPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var readErrorTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await proc.StandardError.ReadLineAsync()) != null)
                {
                    if (TryParseAdbProgress(line, out int pct))
                    {
                        long currentBytes = (long)(fileSize * (pct / 100.0));
                        bytesProgress.Report(currentBytes);
                    }
                }
            });

            await proc.WaitForExitAsync(ct);
            await readErrorTask;

            if (proc.ExitCode == 0 && fileSize > 0)
            {
                bytesProgress.Report(fileSize);
            }

            return proc.ExitCode == 0;
        }

        /// <summary>
        /// Analyzes disk space for all files and subfolders inside remotePath using fast du -sk.
        /// </summary>
        public async Task<List<DiskUsageItem>> AnalyzeDiskSpaceAsync(string serial, string remotePath)
        {
            var items = new List<DiskUsageItem>();
            remotePath = remotePath.TrimEnd('/');
            if (string.IsNullOrEmpty(remotePath)) remotePath = "/sdcard";

            string duCmd = $"du -sk '{remotePath}'/*";
            var result = await RunBase64ShellCmdAsync(serial, duCmd, timeoutMs: 30000);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            {
                // Fallback to ls if du is restricted
                var listItems = await ListDirectoryAsync(serial, remotePath);
                long sumFallback = listItems.Sum(i => i.SizeBytes);
                int rFallback = 1;
                foreach (var li in listItems.OrderByDescending(i => i.SizeBytes))
                {
                    items.Add(new DiskUsageItem
                    {
                        Name = li.Name,
                        Path = li.Path,
                        SizeBytes = li.SizeBytes,
                        TotalParentSizeBytes = sumFallback,
                        IsDir = li.IsDir,
                        Rank = rFallback++
                    });
                }
                return items;
            }

            // Quick dir check map
            string checkDirCmd = $"find '{remotePath}' -maxdepth 1 -type d";
            var dirRes = await RunBase64ShellCmdAsync(serial, checkDirCmd, timeoutMs: 15000);
            var dirSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dirRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(dirRes.Stdout))
            {
                foreach (var d in dirRes.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    dirSet.Add(d.Trim().TrimEnd('/'));
            }

            var lines = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            long totalScanBytes = 0;

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[0], out long sizeKb))
                {
                    long sizeBytes = sizeKb * 1024;
                    string fullPath = parts[1].Trim().TrimEnd('/');
                    string name = Path.GetFileName(fullPath);
                    if (string.IsNullOrEmpty(name)) continue;

                    bool isDirectory = dirSet.Contains(fullPath);
                    totalScanBytes += sizeBytes;

                    items.Add(new DiskUsageItem
                    {
                        Name = name,
                        Path = fullPath,
                        SizeBytes = sizeBytes,
                        IsDir = isDirectory
                    });
                }
            }

            items = items.OrderByDescending(i => i.SizeBytes).ToList();
            int rank = 1;
            foreach (var item in items)
            {
                item.TotalParentSizeBytes = totalScanBytes;
                item.Rank = rank++;
            }

            return items;
        }

        /// <summary>
        /// Retrieves total, used, and free capacity details for Android storage.
        /// </summary>
        public async Task<(long TotalBytes, long UsedBytes, long FreeBytes, double UsedPercent)> GetStorageCapacityDetailsAsync(string serial)
        {
            var res = await RunBase64ShellCmdAsync(serial, "df -k /sdcard");
            if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.Stdout))
            {
                var lines = res.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 2)
                {
                    var parts = lines[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        long.TryParse(parts[1], out long totalKb) &&
                        long.TryParse(parts[2], out long usedKb) &&
                        long.TryParse(parts[3], out long freeKb))
                    {
                        long totalB = totalKb * 1024;
                        long usedB = usedKb * 1024;
                        long freeB = freeKb * 1024;
                        double pct = totalB > 0 ? (usedB * 100.0 / totalB) : 0;
                        return (totalB, usedB, freeB, pct);
                    }
                }
            }
            return (0, 0, 0, 0);
        }

        private static bool TryParseAdbProgress(string line, out int percentage)
        {
            percentage = 0;
            // Matches ADB output pattern like "[ 45%]" or "45%"
            var m = Regex.Match(line, @"\[?\s*(\d{1,3})%\s*\]?");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int pct))
            {
                percentage = Math.Min(100, Math.Max(0, pct));
                return true;
            }
            return false;
        }

        private async Task<(int ExitCode, string Stdout, string Stderr)> RunBase64ShellCmdAsync(string serial, string shellCommand, int timeoutMs = 15000)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(shellCommand);
            string b64 = Convert.ToBase64String(bytes);
            string args = $"-s {serial} shell \"echo {b64} | base64 -d | sh\"";
            return await RunProcessAsync(AdbPath, args, timeoutMs);
        }

        private Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string exe, string args, int timeoutMs = 15000)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                bool exited = proc.WaitForExit(timeoutMs);

                if (!exited)
                {
                    try { proc.Kill(); } catch { }
                    return (-1, "", "Timed out");
                }

                return (proc.ExitCode, stdout, stderr);
            });
        }
    }
}
