using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace openTransferWPF.Services
{
    public class CopyEngineProgress
    {
        public string CurrentFileName { get; set; } = string.Empty;
        public int CurrentFileIndex { get; set; }
        public int TotalFileCount { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedMbPerSec { get; set; }
        public int EtaSeconds { get; set; }
        public int SkippedCount { get; set; }
        public int CopiedCount { get; set; }
        public bool IsPaused { get; set; }
        public string DirectionLabel { get; set; } = "Transferring";

        // Per-file progress fields
        public long CurrentFileBytesTransferred { get; set; }
        public long CurrentFileTotalBytes { get; set; }

        public double CurrentFilePercent => CurrentFileTotalBytes > 0
            ? Math.Min(100.0, Math.Max(0.0, (CurrentFileBytesTransferred * 100.0 / CurrentFileTotalBytes)))
            : 0;

        public double TotalPercent => TotalFileCount > 0
            ? Math.Min(100.0, Math.Max(0.0, (CurrentFileIndex * 100.0 / TotalFileCount)))
            : 0;
    }

    public class CopyEngineItem
    {
        public string LocalPath { get; set; } = string.Empty;
        public string RelativeRemotePath { get; set; } = string.Empty;
    }

    public class CopyEngine
    {
        private readonly AdbService _adbService;
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        private bool _isPaused;

        public bool IsPaused => _isPaused;
        public List<string> LastFailedItems { get; } = new();

        public CopyEngine(AdbService adbService)
        {
            _adbService = adbService;
        }

        public void Pause()
        {
            _isPaused = true;
            _pauseEvent.Reset();
        }

        public void Resume()
        {
            _isPaused = false;
            _pauseEvent.Set();
        }

        // --- DOWNLOAD: PHONE -> PC (OPTIMIZED DICTIONARY + PER-FILE PROGRESS) ---
        public async Task RunDownloadJobAsync(
            string serial,
            string remoteSourceDir,
            string localDestDir,
            string strategyMode, // "SkipExisting", "OverwriteAll"
            IProgress<CopyEngineProgress> progress,
            IProgress<string> logger,
            CancellationToken ct)
        {
            LastFailedItems.Clear();
            logger.Report($"🔍 Scanning remote folder tree on Android [{remoteSourceDir}]...");
            var remoteFiles = await _adbService.ScanRemoteTreeAsync(serial, remoteSourceDir);
            logger.Report($"✓ Found {remoteFiles.Count} total files across all subfolders.");

            if (remoteFiles.Count == 0)
            {
                logger.Report("⚠️ No files to copy.");
                return;
            }

            // Build local index upfront using streaming Directory.EnumerateFiles
            var localIndex = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(localDestDir))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(localDestDir);
                    foreach (var fileInfo in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(localDestDir, fileInfo.FullName).Replace('\\', '/');
                        localIndex[rel] = fileInfo.Length;
                    }
                    logger.Report($"✓ Indexed {localIndex.Count} existing local files in destination.");
                }
                catch (Exception ex)
                {
                    logger.Report($"⚠️ Local indexing note: {ex.Message}");
                }
            }

            int totalCount = remoteFiles.Count;
            long totalSizeJobBytes = 0;
            foreach (var rf in remoteFiles) { totalSizeJobBytes += Math.Max(0, rf.SizeBytes); }

            int copiedCount = 0;
            int skippedCount = 0;
            long totalBytesTransferred = 0;
            var stopwatch = Stopwatch.StartNew();

            string cleanRemoteBase = remoteSourceDir.TrimEnd('/');

            long lastReportTicks = 0;
            long reportIntervalTicks = (long)(0.15 * Stopwatch.Frequency); // 150ms throttle for overall stats

            // Pre-calculate directory file counts for Auto-Split
            var folderTotalCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (SettingsService.Instance.Settings.AutoSplitOnTransfer && SettingsService.Instance.Settings.AutoSplitBatchSize > 0)
            {
                foreach (var rf in remoteFiles)
                {
                    string rPath = rf.Path;
                    if (rPath.StartsWith(cleanRemoteBase, StringComparison.OrdinalIgnoreCase))
                    {
                        rPath = rPath.Substring(cleanRemoteBase.Length).TrimStart('/', '\\');
                    }
                    string rDir = Path.GetDirectoryName(rPath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
                    if (!folderTotalCounts.TryGetValue(rDir, out int c)) c = 0;
                    folderTotalCounts[rDir] = c + 1;
                }
            }

            var dirFileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < remoteFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                while (!_pauseEvent.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(200, ct);
                }

                var rfile = remoteFiles[i];
                var settings = SettingsService.Instance.Settings;

                string relPath = rfile.Path;
                if (relPath.StartsWith(cleanRemoteBase, StringComparison.OrdinalIgnoreCase))
                {
                    relPath = relPath.Substring(cleanRemoteBase.Length).TrimStart('/', '\\');
                }

                // Check Auto-Split Settings
                string localTargetPath;
                string relDirForSplit = Path.GetDirectoryName(relPath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
                bool shouldSplit = settings.AutoSplitOnTransfer && 
                                   settings.AutoSplitBatchSize > 0 && 
                                   folderTotalCounts.TryGetValue(relDirForSplit, out int totCount) && 
                                   totCount > settings.AutoSplitBatchSize;

                if (shouldSplit)
                {
                    string relDir = relDirForSplit;
                    string fileName = Path.GetFileName(relPath.Replace('/', Path.DirectorySeparatorChar));
                    
                    if (!dirFileCounts.TryGetValue(relDir, out int count)) count = 0;
                    dirFileCounts[relDir] = count + 1;
                    
                    int batchIndex = ((count - 1) / settings.AutoSplitBatchSize) + 1;
                    string subFolder;
                    
                    if (settings.AutoSplitNamingFormat == "Day")
                    {
                        subFolder = $"day 1-{batchIndex}";
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(relDir))
                        {
                            string leafFolder = Path.GetFileName(relDir);
                            string parentFolder = Path.GetFileName(Path.GetDirectoryName(relDir) ?? "");
                            if (string.IsNullOrEmpty(parentFolder)) 
                            {
                                subFolder = $"{leafFolder}_{batchIndex}";
                            }
                            else
                            {
                                subFolder = $"{parentFolder} {leafFolder}_{batchIndex}";
                            }
                        }
                        else
                        {
                            string rootName = Path.GetFileName(cleanRemoteBase);
                            if (string.IsNullOrEmpty(rootName) || rootName.Equals("sdcard", StringComparison.OrdinalIgnoreCase)) rootName = "photo";
                            subFolder = $"{rootName}_{batchIndex}";
                        }
                    }
                    
                    localTargetPath = Path.Combine(localDestDir, relDir, subFolder, fileName);
                }
                else
                {
                    localTargetPath = Path.Combine(localDestDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                }

                if (!settings.PreserveFolderStructure)
                    localTargetPath = Path.Combine(localDestDir, Path.GetFileName(relPath));

                // O(1) Dictionary Skip Check
                bool localExists = File.Exists(localTargetPath);
                if (strategyMode == "SkipExisting" && localExists)
                {
                    var localInfo = new FileInfo(localTargetPath);
                    bool matches = settings.CompareMethod == "FilenameOnly" || localInfo.Length == rfile.SizeBytes;
                    if (matches && settings.CompareMethod == "FilenameSizeDate" && rfile.ModifiedUnixSeconds > 0)
                    {
                        long localUnix = new DateTimeOffset(localInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
                        matches = Math.Abs(localUnix - rfile.ModifiedUnixSeconds) <= 2;
                    }
                    if (matches)
                    {
                        skippedCount++;
                        logger.Report($"⏩ Skipped (Already Exists): {rfile.Name}");
                        ReportProgress(progress, "Downloading", rfile.Name, i + 1, totalCount, totalBytesTransferred, totalSizeJobBytes, stopwatch.Elapsed.TotalSeconds, skippedCount, copiedCount, 0, rfile.SizeBytes);
                        continue;
                    }
                }
                else if (strategyMode == "Rename" && localExists)
                {
                    localTargetPath = GetAvailableLocalPath(localTargetPath);
                }

                logger.Report($"📥 Downloading: {relPath} -> {localTargetPath}");
                
                long currentFileTransferred = 0;
                var fileBytesProgress = new Progress<long>(curBytes =>
                {
                    currentFileTransferred = curBytes;
                    // Always report per-file progress immediately
                    ReportProgress(progress, "Downloading", rfile.Name, i + 1, totalCount, totalBytesTransferred + curBytes, totalSizeJobBytes, stopwatch.Elapsed.TotalSeconds, skippedCount, copiedCount, curBytes, rfile.SizeBytes);
                });

                bool success = false;
                int maxAttempts = settings.RetryFailedTransfers ? Math.Max(1, settings.RetryCount + 1) : 1;
                for (int attempt = 1; attempt <= maxAttempts && !success; attempt++)
                {
                    success = await _adbService.PullFileWithProgressAsync(serial, rfile.Path, localTargetPath, rfile.SizeBytes, fileBytesProgress, ct);
                    if (!success && attempt < maxAttempts)
                    {
                        logger.Report($"⚠️ Retry {attempt}/{maxAttempts - 1} for: {rfile.Name}");
                        await Task.Delay(200 * attempt, ct);
                    }
                }

                if (success && settings.VerifyCopiedFiles && rfile.SizeBytes >= 0)
                {
                    success = File.Exists(localTargetPath) && new FileInfo(localTargetPath).Length == rfile.SizeBytes;
                    if (!success) logger.Report($"❌ Verification failed (size mismatch): {rfile.Name}");
                }

                if (success)
                {
                    if (settings.PreserveModifiedDate && rfile.ModifiedUnixSeconds > 0)
                    {
                        try { File.SetLastWriteTimeUtc(localTargetPath, DateTimeOffset.FromUnixTimeSeconds(rfile.ModifiedUnixSeconds).UtcDateTime); }
                        catch (Exception ex) { logger.Report($"⚠️ Could not preserve date for {rfile.Name}: {ex.Message}"); }
                    }
                    copiedCount++;
                    long fileBytes = rfile.SizeBytes > 0 ? rfile.SizeBytes : (File.Exists(localTargetPath) ? new FileInfo(localTargetPath).Length : 0);
                    totalBytesTransferred += fileBytes;
                    logger.Report($"✓ Downloaded: {rfile.Name}");
                }
                else
                {
                    LastFailedItems.Add(rfile.Path);
                    logger.Report($"❌ Failed to download: {rfile.Name}");
                }

                long nowTicks = Stopwatch.GetTimestamp();
                if (nowTicks - lastReportTicks >= reportIntervalTicks || i == totalCount - 1)
                {
                    ReportProgress(progress, "Downloading", rfile.Name, i + 1, totalCount, totalBytesTransferred, totalSizeJobBytes, stopwatch.Elapsed.TotalSeconds, skippedCount, copiedCount, rfile.SizeBytes, rfile.SizeBytes);
                    lastReportTicks = nowTicks;
                }
            }

            stopwatch.Stop();
            logger.Report($"🎉 Download Complete: {copiedCount} Copied, {skippedCount} Skipped in {stopwatch.Elapsed.TotalSeconds:0.#}s.");
        }

        // --- UPLOAD: PC -> PHONE (OPTIMIZED DICTIONARY + BATCH MKDIR + PER-FILE PROGRESS) ---
        public async Task RunUploadJobAsync(
            string serial,
            List<string> localSourcePaths,
            string remoteDestDir,
            string strategyMode, // "SkipExisting", "OverwriteAll"
            IProgress<CopyEngineProgress> progress,
            IProgress<string> logger,
            CancellationToken ct)
        {
            LastFailedItems.Clear();
            logger.Report($"🔍 Scanning local folder tree...");
            var itemsToPush = new List<CopyEngineItem>();

            foreach (var srcPath in localSourcePaths)
            {
                if (File.Exists(srcPath))
                {
                    itemsToPush.Add(new CopyEngineItem
                    {
                        LocalPath = srcPath,
                        RelativeRemotePath = Path.GetFileName(srcPath)
                    });
                }
                else if (Directory.Exists(srcPath))
                {
                    string rootDirName = Path.GetFileName(srcPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var dirInfo = new DirectoryInfo(srcPath);

                    foreach (var lfInfo in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        string relSubPath = Path.GetRelativePath(srcPath, lfInfo.FullName).Replace('\\', '/');
                        string remoteRel = $"{rootDirName}/{relSubPath}";

                        itemsToPush.Add(new CopyEngineItem
                        {
                            LocalPath = lfInfo.FullName,
                            RelativeRemotePath = remoteRel
                        });
                    }
                }
            }

            if (itemsToPush.Count == 0)
            {
                logger.Report("⚠️ No valid local files found to upload.");
                return;
            }

            logger.Report($"✓ Found {itemsToPush.Count} file(s) across all subfolders to check.");

            // Ultra-fast remote dictionary lookup (single ADB command for entire remote tree)
            logger.Report($"🔍 Fetching remote directory index from Android...");
            var remoteIndex = await _adbService.ScanRemoteTreeAsDictionaryAsync(serial, remoteDestDir);
            logger.Report($"✓ Indexed {remoteIndex.Count} remote files in target location.");

            int totalCount = itemsToPush.Count;
            long totalSizeJobBytes = 0;
            foreach (var item in itemsToPush)
            {
                try { totalSizeJobBytes += new FileInfo(item.LocalPath).Length; } catch { }
            }

            int uploadedCount = 0;
            int skippedCount = 0;
            long totalBytesTransferred = 0;
            var stopwatch = Stopwatch.StartNew();

            string cleanRemoteBase = remoteDestDir.TrimEnd('/');
            var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            long lastReportTicks = 0;
            long reportIntervalTicks = (long)(0.15 * Stopwatch.Frequency); // 150ms throttle for overall stats

            // Pre-calculate directory file counts for Auto-Split
            var folderTotalCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (SettingsService.Instance.Settings.AutoSplitOnTransfer && SettingsService.Instance.Settings.AutoSplitBatchSize > 0)
            {
                foreach (var item in itemsToPush)
                {
                    string rDir = Path.GetDirectoryName(item.RelativeRemotePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
                    if (!folderTotalCounts.TryGetValue(rDir, out int c)) c = 0;
                    folderTotalCounts[rDir] = c + 1;
                }
            }

            var dirFileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < itemsToPush.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                while (!_pauseEvent.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(200, ct);
                }

                var item = itemsToPush[i];
                var settings = SettingsService.Instance.Settings;

                // Check Auto-Split Settings
                string remoteTargetFile;
                string relDirForSplit = Path.GetDirectoryName(item.RelativeRemotePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
                bool shouldSplit = settings.AutoSplitOnTransfer && 
                                   settings.AutoSplitBatchSize > 0 && 
                                   folderTotalCounts.TryGetValue(relDirForSplit, out int totCount) && 
                                   totCount > settings.AutoSplitBatchSize;

                if (shouldSplit)
                {
                    string relDir = relDirForSplit;
                    string splitFileName = Path.GetFileName(item.RelativeRemotePath.Replace('\\', '/'));
                    
                    if (!dirFileCounts.TryGetValue(relDir, out int count)) count = 0;
                    dirFileCounts[relDir] = count + 1;
                    
                    int batchIndex = ((count - 1) / settings.AutoSplitBatchSize) + 1;
                    string subFolder;
                    
                    if (settings.AutoSplitNamingFormat == "Day")
                    {
                        subFolder = $"day 1-{batchIndex}";
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(relDir))
                        {
                            string leafFolder = Path.GetFileName(relDir);
                            string parentFolder = Path.GetFileName(Path.GetDirectoryName(relDir) ?? "");
                            if (string.IsNullOrEmpty(parentFolder)) 
                            {
                                subFolder = $"{leafFolder}_{batchIndex}";
                            }
                            else
                            {
                                subFolder = $"{parentFolder} {leafFolder}_{batchIndex}";
                            }
                        }
                        else
                        {
                            string rootName = Path.GetFileName(cleanRemoteBase);
                            if (string.IsNullOrEmpty(rootName) || rootName.Equals("sdcard", StringComparison.OrdinalIgnoreCase)) rootName = "photo";
                            subFolder = $"{rootName}_{batchIndex}";
                        }
                    }
                    
                    string combinedRelPath = string.IsNullOrEmpty(relDir) 
                        ? $"{subFolder}/{splitFileName}" 
                        : $"{relDir}/{subFolder}/{splitFileName}";
                    remoteTargetFile = $"{cleanRemoteBase}/{combinedRelPath}";
                }
                else
                {
                    remoteTargetFile = $"{cleanRemoteBase}/{item.RelativeRemotePath}";
                }

                if (!settings.PreserveFolderStructure)
                    remoteTargetFile = $"{cleanRemoteBase}/{Path.GetFileName(item.LocalPath)}";

                long fileSize = 0;
                try { fileSize = new FileInfo(item.LocalPath).Length; } catch { }

                string remoteRelativeTarget = remoteTargetFile.StartsWith(cleanRemoteBase + "/", StringComparison.OrdinalIgnoreCase)
                    ? remoteTargetFile.Substring(cleanRemoteBase.Length + 1)
                    : item.RelativeRemotePath;

                // O(1) Remote Dictionary Skip Check
                if (strategyMode == "SkipExisting" && remoteIndex.TryGetValue(remoteRelativeTarget, out long existingSize))
                {
                    bool matches = settings.CompareMethod == "FilenameOnly" || existingSize == fileSize;
                    if (matches && settings.CompareMethod == "FilenameSizeDate")
                    {
                        long remoteUnix = await _adbService.GetRemoteModifiedUnixAsync(serial, remoteTargetFile);
                        long localUnix = new DateTimeOffset(File.GetLastWriteTimeUtc(item.LocalPath)).ToUnixTimeSeconds();
                        matches = remoteUnix > 0 && Math.Abs(remoteUnix - localUnix) <= 2;
                    }
                    if (matches)
                    {
                        skippedCount++;
                        logger.Report($"⏩ Skipped (Already Exists): {Path.GetFileName(item.LocalPath)}");
                        ReportProgress(progress, "Uploading", Path.GetFileName(item.LocalPath), i + 1, totalCount, totalBytesTransferred, totalSizeJobBytes, stopwatch.Elapsed.TotalSeconds, skippedCount, uploadedCount, 0, fileSize);
                        continue;
                    }
                }
                else if (strategyMode == "Rename" && remoteIndex.ContainsKey(remoteRelativeTarget))
                {
                    remoteTargetFile = GetAvailableRemotePath(remoteTargetFile, cleanRemoteBase, remoteIndex);
                    remoteRelativeTarget = remoteTargetFile.Substring(cleanRemoteBase.Length + 1);
                }

                // Batch directory creation
                string remoteDir = Path.GetDirectoryName(remoteTargetFile)?.Replace('\\', '/') ?? cleanRemoteBase;
                if (!createdDirs.Contains(remoteDir))
                {
                    if (!settings.CreateMissingFolders && remoteDir != cleanRemoteBase)
                    {
                        skippedCount++;
                        logger.Report($"⏩ Skipped (missing folder creation disabled): {Path.GetFileName(item.LocalPath)}");
                        continue;
                    }
                    await _adbService.CreateDirectoryAsync(serial, remoteDir);
                    createdDirs.Add(remoteDir);
                }

                string fileName = Path.GetFileName(item.LocalPath);
                logger.Report($"📤 Uploading: {fileName} -> {remoteTargetFile}");

                long currentFileTransferred = 0;
                var fileBytesProgress = new Progress<long>(curBytes =>
                {
                    currentFileTransferred = curBytes;
                    // Always report per-file progress immediately
                    ReportProgress(progress, "Uploading", fileName, i + 1, totalCount, totalBytesTransferred + curBytes, totalSizeJobBytes, stopwatch.Elapsed.TotalSeconds, skippedCount, uploadedCount, curBytes, fileSize);
                });

                bool success = false;
                int maxAttempts = settings.RetryFailedTransfers ? Math.Max(1, settings.RetryCount + 1) : 1;
                for (int attempt = 1; attempt <= maxAttempts && !success; attempt++)
                {
                    success = await _adbService.PushFileWithProgressAsync(serial, item.LocalPath, remoteTargetFile, fileSize, fileBytesProgress, ct);
                    if (!success && attempt < maxAttempts)
                    {
                        logger.Report($"⚠️ Retry {attempt}/{maxAttempts - 1} for: {fileName}");
                        await Task.Delay(200 * attempt, ct);
                    }
                }

                if (success && settings.VerifyCopiedFiles)
                {
                    long remoteSize = await _adbService.GetRemoteFileSizeAsync(serial, remoteTargetFile);
                    success = remoteSize == fileSize;
                    if (!success) logger.Report($"❌ Verification failed (size mismatch): {fileName}");
                }

                if (success)
                {
                    if (settings.PreserveModifiedDate)
                        await _adbService.SetRemoteModifiedTimeAsync(serial, remoteTargetFile, File.GetLastWriteTime(item.LocalPath));
                    uploadedCount++;
                    totalBytesTransferred += fileSize;
                    logger.Report($"✓ Uploaded: {fileName}");
                }
                else
                {
                    LastFailedItems.Add(item.LocalPath);
                    logger.Report($"❌ Failed to upload: {fileName}");
                }

                long nowTicks = Stopwatch.GetTimestamp();
                if (nowTicks - lastReportTicks >= reportIntervalTicks || i == totalCount - 1)
                {
                    ReportProgress(progress, "Uploading", fileName, i + 1, totalCount, totalBytesTransferred, totalSizeJobBytes, stopwatch.Elapsed.TotalSeconds, skippedCount, uploadedCount, fileSize, fileSize);
                    lastReportTicks = nowTicks;
                }
            }

            stopwatch.Stop();
            logger.Report($"🎉 Upload Complete: {uploadedCount} Uploaded, {skippedCount} Skipped in {stopwatch.Elapsed.TotalSeconds:0.#}s.");
        }

        private static string GetAvailableLocalPath(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int n = 1; ; n++)
            {
                string candidate = Path.Combine(directory ?? string.Empty, $"{stem} ({n}){extension}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        private static string GetAvailableRemotePath(string path, string remoteBase, Dictionary<string, long> index)
        {
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? remoteBase;
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int n = 1; ; n++)
            {
                string candidate = $"{directory}/{stem} ({n}){extension}";
                string relative = candidate.Substring(remoteBase.Length).TrimStart('/');
                if (!index.ContainsKey(relative)) return candidate;
            }
        }

        private void ReportProgress(
            IProgress<CopyEngineProgress> progress,
            string label,
            string currentName,
            int currentIndex,
            int totalCount,
            long bytesTransferred,
            long totalBytes,
            double elapsedSeconds,
            int skippedCount,
            int copiedCount,
            long currentFileBytes,
            long currentFileTotal)
        {
            double speedMb = elapsedSeconds > 0 ? (bytesTransferred / (1024.0 * 1024.0)) / elapsedSeconds : 0;
            int eta = 0;
            if (speedMb > 0 && totalBytes > bytesTransferred)
            {
                double remainingMb = (totalBytes - bytesTransferred) / (1024.0 * 1024.0);
                eta = (int)(remainingMb / speedMb);
            }
            else if (currentIndex < totalCount && speedMb > 0)
            {
                int remainingFiles = totalCount - currentIndex;
                eta = (int)(remainingFiles * 0.5);
            }

            progress.Report(new CopyEngineProgress
            {
                DirectionLabel = label,
                CurrentFileName = currentName,
                CurrentFileIndex = currentIndex,
                TotalFileCount = totalCount,
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytes,
                SpeedMbPerSec = speedMb,
                EtaSeconds = eta,
                SkippedCount = skippedCount,
                CopiedCount = copiedCount,
                IsPaused = _isPaused,
                CurrentFileBytesTransferred = currentFileBytes,
                CurrentFileTotalBytes = currentFileTotal
            });
        }
    }
}
