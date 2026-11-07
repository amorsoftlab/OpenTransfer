using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using openTransferWPF.Dialogs;
using openTransferWPF.Models;
using openTransferWPF.Services;

namespace openTransferWPF
{
    public partial class MainWindow : Window
    {
        private readonly AdbService _adbService;
        private readonly CopyEngine _copyEngine;
        private readonly DispatcherTimer _deviceTimer;

        private string _activeSerial = string.Empty;
        private string _currentPath = "/sdcard";
        private List<AndroidFileItem> _allItems = new();
        private readonly List<string> _history = new() { "/sdcard" };
        private int _historyIdx = 0;

        private CancellationTokenSource? _copyCts;
        private GridView? _defaultGridView;
        private bool _isGridView = false;
        private int _directoryLoadVersion;
        private bool _isDeviceCheckRunning;
        private Forms.NotifyIcon? _trayIcon;
        private bool _allowClose;
        private bool _deviceWasDisconnected;
        private bool _transferActive;
        private bool _updatingStorageRoots;
        private readonly SemaphoreSlim _transferGate = new(1, 1);
        private int _queuedTransferCount;

        public MainWindow()
        {
            InitializeComponent();

            TxtAboutVersion.Text = $"Version: {UpdateService.CurrentAppVersion}";

            _adbService = new AdbService();
            _copyEngine = new CopyEngine(_adbService);

            Log("Connected to Redmi Note 7 Pro");

            // Device Polling Timer
            _deviceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(SettingsService.Instance.Settings.RefreshIntervalSeconds)
            };
            _deviceTimer.Tick += async (s, e) => await CheckDevicesAsync(refreshStorage: true);
            _deviceTimer.Start();

            // Initial check & Settings listener
            Loaded += async (s, e) =>
            {
                ApplySettings(SettingsService.Instance.Settings);
                await CheckDevicesAsync();
                _ = CheckForUpdatesOnStartupAsync();
            };

            SettingsService.Instance.SettingsChanged += (s, settings) => ApplySettings(settings);
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            TxtLogConsole.AppendText($"[{timestamp}] {message}\n");
            TxtLogConsole.ScrollToEnd();
        }

        private void BtnToggleLogs_Click(object sender, RoutedEventArgs e)
        {
            if (PnlActivityLogs.Visibility == Visibility.Visible)
            {
                PnlActivityLogs.Visibility = Visibility.Collapsed;
            }
            else
            {
                PnlActivityLogs.Visibility = Visibility.Visible;
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            TxtLogConsole.Clear();
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtLogConsole.Text))
            {
                Clipboard.SetText(TxtLogConsole.Text);
                MessageBox.Show("Logs copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task CheckDevicesAsync(bool refreshStorage = false)
        {
            if (_isDeviceCheckRunning) return;
            _isDeviceCheckRunning = true;
            try
            {
            bool available = await _adbService.IsAdbAvailableAsync();
            if (!available)
            {
                TxtStatusFooter.Text = "ADB Binary Not Found";
                CmbDevices.ItemsSource = null;
                PnlNoDevice.Visibility = Visibility.Visible;
                LstFiles.Visibility = Visibility.Collapsed;
                TxtFreeStorage.Text = "Free --";
                return;
            }

            var devices = await _adbService.GetDevicesAsync();
            if (devices.Count == 0)
            {
                TxtStatusFooter.Text = "No Android device connected";
                CmbDevices.ItemsSource = null;
                PnlNoDevice.Visibility = Visibility.Visible;
                LstFiles.Visibility = Visibility.Collapsed;
                TxtFreeStorage.Text = "Free --";
                if (!string.IsNullOrEmpty(_activeSerial))
                {
                    Log("🔴 Device Disconnected.");
                    if (_transferActive)
                        MessageBox.Show("The Android device disconnected during an active transfer. The current item may be incomplete; reconnect and retry failed files.", "Transfer Interrupted", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _activeSerial = string.Empty;
                    _deviceWasDisconnected = true;
                    Interlocked.Increment(ref _directoryLoadVersion);
                }
            }
            else
            {
                PnlNoDevice.Visibility = Visibility.Collapsed;
                LstFiles.Visibility = Visibility.Visible;
                CmbDevices.ItemsSource = devices;
                CmbDevices.DisplayMemberPath = "DisplayName";
                CmbDevices.SelectedValuePath = "Serial";

                // Auto Select First ADB Device
                if (string.IsNullOrEmpty(_activeSerial) || !devices.Any(d => d.Serial == _activeSerial))
                {
                    if (!_deviceWasDisconnected || SettingsService.Instance.Settings.AutoReconnect)
                    {
                        CmbDevices.SelectedIndex = 0;
                        _deviceWasDisconnected = false;
                    }
                }
                else
                {
                    CmbDevices.SelectedValue = _activeSerial;
                    if (refreshStorage) await UpdateStorageInfoAsync();
                }
            }
            }
            finally { _isDeviceCheckRunning = false; }
        }

        private async void CmbDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDevices.SelectedItem is DeviceItem dev)
            {
                if (_activeSerial != dev.Serial)
                {
                    _activeSerial = dev.Serial;
                    Title = $"Android File Transfer - {dev.Model}";
                    Log($"Connected to {dev.Model}");
                    _ = UpdateStorageInfoAsync();
                    _ = PopulateStorageRootsAsync();
                    RefreshQuickAccess();
                    _ = LoadDirectoryAsync(await GetStartupStoragePathAsync());
                }
            }
        }

        private async Task UpdateStorageInfoAsync()
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;
            string freeStr = await _adbService.GetFreeStorageAsync(_activeSerial);
            TxtFreeStorage.Text = freeStr;
        }

        private async Task LoadDirectoryAsync(string remotePath)
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;

            int loadVersion = Interlocked.Increment(ref _directoryLoadVersion);
            string serial = _activeSerial;

            TxtStatusFooter.Text = $"Loading {remotePath}...";
            Log($"{remotePath} folder indexed");
            var itemsTask = _adbService.ListDirectoryAsync(serial, remotePath);
            var storageTask = _adbService.GetFreeStorageAsync(serial);
            var items = await itemsTask;

            var settings = SettingsService.Instance.Settings;
            if (settings.ShowFolderSizes)
            {
                using var sizeGate = new SemaphoreSlim(4);
                await Task.WhenAll(items.Where(i => i.IsDir).Select(async item =>
                {
                    await sizeGate.WaitAsync();
                    try { item.SizeBytes = await _adbService.GetDirectorySizeAsync(serial, item.Path); }
                    finally { sizeGate.Release(); }
                }));
            }

            if (loadVersion != _directoryLoadVersion || serial != _activeSerial) return;

            foreach (var item in items)
            {
                item.ResolveCategoryIcon();
                item.FolderBrush = GetFolderBrush(settings.FolderColor);
                item.DisplayName = settings.ShowFileExtensions || item.IsDir
                    ? item.Name
                    : System.IO.Path.GetFileNameWithoutExtension(item.Name);
            }

            TxtFreeStorage.Text = await storageTask;

            _currentPath = remotePath;
            TxtPath.Text = remotePath;
            _allItems = items;

            // Track History
            if (_history.Count == 0 || _history[_historyIdx] != remotePath)
            {
                if (_historyIdx < _history.Count - 1)
                    _history.RemoveRange(_historyIdx + 1, _history.Count - (_historyIdx + 1));
                _history.Add(remotePath);
                _historyIdx = _history.Count - 1;
            }

            FilterAndRender();
            Log($"Ready for transfer");
        }

        private void FilterAndRender()
        {
            string q = TxtSearch.Text.Trim();
            bool showHidden = SettingsService.Instance.Settings.ShowHiddenFiles;
            string? extension = null;
            long? minSize = null, maxSize = null;
            DateTime? afterDate = null;
            var textTerms = new List<string>();
            foreach (string token in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase)) extension = token[4..].TrimStart('.');
                else if (token.StartsWith("min:", StringComparison.OrdinalIgnoreCase)) minSize = ParseSize(token[4..]);
                else if (token.StartsWith("max:", StringComparison.OrdinalIgnoreCase)) maxSize = ParseSize(token[4..]);
                else if (token.StartsWith("after:", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(token[6..], out var date)) afterDate = date;
                else textTerms.Add(token);
            }
            IEnumerable<AndroidFileItem> query = _allItems.Where(i => showHidden || !i.Name.StartsWith('.'));
            if (extension != null) query = query.Where(i => i.Name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase));
            if (minSize.HasValue) query = query.Where(i => !i.IsDir && i.SizeBytes >= minSize.Value);
            if (maxSize.HasValue) query = query.Where(i => !i.IsDir && i.SizeBytes <= maxSize.Value);
            if (afterDate.HasValue) query = query.Where(i => DateTime.TryParse(i.ModifiedDateStr, out var d) && d >= afterDate.Value);
            foreach (string term in textTerms) query = query.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            var settings = SettingsService.Instance.Settings;
            IOrderedEnumerable<AndroidFileItem> ordered = query.OrderByDescending(i => i.IsDir);
            List<AndroidFileItem> filtered = settings.SortBy switch
            {
                "Size" => (settings.SortDescending ? ordered.ThenByDescending(i => i.SizeBytes) : ordered.ThenBy(i => i.SizeBytes)).ToList(),
                "Date" => (settings.SortDescending ? ordered.ThenByDescending(i => i.ModifiedUnixSeconds) : ordered.ThenBy(i => i.ModifiedUnixSeconds)).ToList(),
                _ => (settings.SortDescending ? ordered.ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase) : ordered.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)).ToList()
            };

            LstFiles.ItemsSource = filtered;
            TxtStatusFooter.Text = $"{filtered.Count} Items";
            UpdateSelectionStats();
        }

        private static long? ParseSize(string value)
        {
            var match = System.Text.RegularExpressions.Regex.Match(value, @"^([0-9]+(?:\.[0-9]+)?)(KB|MB|GB|B)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success || !double.TryParse(match.Groups[1].Value, out double number)) return null;
            double factor = match.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1024, "MB" => 1024 * 1024, "GB" => 1024d * 1024 * 1024, _ => 1 };
            return (long)(number * factor);
        }

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            var settings = SettingsService.Instance.Settings;
            string tag = item.Tag?.ToString() ?? "Name";
            if (tag == "Toggle") settings.SortDescending = !settings.SortDescending;
            else settings.SortBy = tag;
            SettingsService.Instance.SaveSettings(settings);
            FilterAndRender();
        }

        private void LstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionStats();
            if (LstFiles.SelectedItem is AndroidFileItem item)
            {
                Log($"Selected item: {item.Name}");
            }
        }

        private void UpdateSelectionStats()
        {
            var selected = LstFiles.SelectedItems.Cast<AndroidFileItem>().ToList();
            if (selected.Count == 0)
            {
                TxtSelectedStats.Text = "0 Selected";
            }
            else
            {
                TxtSelectedStats.Text = selected.Count == 1 ? "1 Selected" : $"{selected.Count} Selected";
            }
        }

        private void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstFiles.SelectedItem is AndroidFileItem item && item.IsDir)
            {
                _ = LoadDirectoryAsync(item.Path);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIdx > 0)
            {
                _historyIdx--;
                _ = LoadDirectoryAsync(_history[_historyIdx]);
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIdx < _history.Count - 1)
            {
                _historyIdx++;
                _ = LoadDirectoryAsync(_history[_historyIdx]);
            }
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            string parent = System.IO.Path.GetDirectoryName(_currentPath.TrimEnd('/'))?.Replace('\\', '/') ?? "/sdcard";
            if (string.IsNullOrEmpty(parent) || parent == "/") parent = "/sdcard";
            _ = LoadDirectoryAsync(parent);
        }

        private void BtnRootFolder_Click(object sender, MouseButtonEventArgs e)
        {
            _ = LoadDirectoryAsync("/sdcard");
        }

        private async Task PopulateStorageRootsAsync()
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;
            _updatingStorageRoots = true;
            try
            {
                var roots = await _adbService.GetStorageRootsAsync(_activeSerial);
                CmbStorageRoots.ItemsSource = roots;
                CmbStorageRoots.SelectedItem = roots.FirstOrDefault(r => _currentPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)) ?? roots.FirstOrDefault();
            }
            finally { _updatingStorageRoots = false; }
        }

        private void CmbStorageRoots_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingStorageRoots && CmbStorageRoots.SelectedItem is string path) _ = LoadDirectoryAsync(path);
        }

        private async void RefreshQuickAccess()
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;
            var candidates = new List<string>
            {
                "/sdcard/DCIM/Camera", "/sdcard/Pictures/Screenshots", "/sdcard/Download",
                "/sdcard/Android/media/com.whatsapp/WhatsApp/Media",
                "/sdcard/Android/media/org.telegram.messenger/Telegram"
            };
            var checks = await Task.WhenAll(candidates.Select(async path => (path, exists: await _adbService.PathExistsAsync(_activeSerial, path))));
            var paths = checks.Where(x => x.exists).Select(x => x.path).ToList();
            paths.AddRange(SettingsService.Instance.Settings.FavoritePaths);
            CmbQuickAccess.ItemsSource = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            CmbQuickAccess.SelectedIndex = -1;
            CmbQuickAccess.Text = "Quick Access";
        }

        private async void CmbQuickAccess_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbQuickAccess.SelectedItem is not string path || string.IsNullOrEmpty(_activeSerial)) return;
            if (await _adbService.PathExistsAsync(_activeSerial, path)) await LoadDirectoryAsync(path);
            else MessageBox.Show($"This folder does not exist on the connected device:\n{path}", "Quick Access", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Settings;
            var existing = settings.FavoritePaths.FirstOrDefault(p => p.Equals(_currentPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { settings.FavoritePaths.Remove(existing); Log($"Unpinned {_currentPath}"); }
            else { settings.FavoritePaths.Add(_currentPath); Log($"Pinned {_currentPath}"); }
            SettingsService.Instance.SaveSettings(settings);
            RefreshQuickAccess();
        }

        private void TxtPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = LoadDirectoryAsync(TxtPath.Text.Trim());
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndRender();
        }

        private async void BtnRefreshDevice_Click(object sender, RoutedEventArgs e)
        {
            Log("Refreshing device storage...");
            await CheckDevicesAsync(refreshStorage: true);
            if (!string.IsNullOrEmpty(_activeSerial))
            {
                await LoadDirectoryAsync(_currentPath);
            }
        }

        // --- FULL FILE ACTIONS WITH SMART CONFLICT DETECTION ---

        private async void BtnNewFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;

            string? folderName = Dialogs.InputDialog.Show(this, "📁 Create New Folder", "Enter name for new folder on Android:", "New_Folder", "Create Folder");
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                string targetPath = $"{_currentPath.TrimEnd('/')}/{folderName.Trim()}";
                Log($"Creating folder: {targetPath}");
                bool ok = await _adbService.CreateDirectoryAsync(_activeSerial, targetPath);
                if (ok)
                {
                    Log($"Created folder: {folderName}");
                    _ = LoadDirectoryAsync(_currentPath);
                }
                else
                {
                    Log($"Failed to create folder: {folderName}");
                    MessageBox.Show("Failed to create folder on Android.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnDeleteEmptyFolders_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;
            
            string targetPath = _currentPath;
            
            // Only use SelectedItem if invoked from the ContextMenu (MenuItem)
            if (sender is System.Windows.Controls.MenuItem && LstFiles.SelectedItem is AndroidFileItem item && item.IsDir)
            {
                targetPath = item.Path;
            }
            
            var confirm = MessageBox.Show($"Are you sure you want to clean all empty folders recursively inside:\n\n{targetPath}\n\n(This will scan everything inside this folder and its subfolders)", "Clean Empty Folders", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (confirm == MessageBoxResult.Yes)
            {
                TxtStatusFooter.Text = "Cleaning empty folders...";
                int deletedCount = await _adbService.CleanEmptyFoldersAsync(_activeSerial, targetPath);
                if (deletedCount >= 0)
                {
                    Log($"Cleaned {deletedCount} empty folders in '{targetPath}'");
                    TxtStatusFooter.Text = "Cleanup complete.";
                    
                    await LoadDirectoryAsync(_currentPath); // Refresh UI BEFORE showing dialog
                    var successDialog = new openTransferWPF.Dialogs.SuccessDialog(
                        "Cleanup Complete", 
                        $"Successfully removed {deletedCount} empty folder(s)."
                    )
                    {
                        Owner = this
                    };
                    successDialog.ShowDialog();
                }
                else
                {
                    Log($"Failed to clean empty folders in '{targetPath}'");
                    TxtStatusFooter.Text = "Cleanup failed.";
                    MessageBox.Show("Failed to clean empty folders.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;
            var selected = LstFiles.SelectedItems.Cast<AndroidFileItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select file(s) or folder(s) to delete.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Are you sure you want to delete {selected.Count} item(s) permanently from your Android phone?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                TxtStatusFooter.Text = $"Deleting {selected.Count} item(s)...";
                foreach (var item in selected)
                {
                    Log($"Deleting: {item.Name}");
                    await _adbService.DeleteItemAsync(_activeSerial, item.Path);
                }
                Log($"Deleted {selected.Count} item(s).");
                _ = LoadDirectoryAsync(_currentPath);
            }
        }

        private async void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;
            if (LstFiles.SelectedItem is AndroidFileItem item)
            {
                string? newName = Dialogs.InputDialog.Show(this, "✏️ Rename Item", $"Enter new name for '{item.Name}':", item.Name, "Rename");
                if (!string.IsNullOrWhiteSpace(newName) && newName != item.Name)
                {
                    string newPath = $"{_currentPath.TrimEnd('/')}/{newName.Trim()}";
                    Log($"Renaming '{item.Name}' -> '{newName}'");
                    bool ok = await _adbService.RenameItemAsync(_activeSerial, item.Path, newPath);
                    if (ok)
                    {
                        Log($"Renamed successfully.");
                        _ = LoadDirectoryAsync(_currentPath);
                    }
                    else
                    {
                        Log($"Failed to rename '{item.Name}'.");
                        MessageBox.Show("Failed to rename item.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select an item to rename.", "Rename", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnPreviewFile_Click(object sender, RoutedEventArgs e)
        {
            if (LstFiles.SelectedItem is not AndroidFileItem item || item.IsDir || string.IsNullOrEmpty(_activeSerial))
            {
                MessageBox.Show("Select a file to preview.", "Preview", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                if (item.SizeBytes > 100L * 1024 * 1024 && MessageBox.Show($"Preview requires downloading {item.FormattedSize}. Continue?", "Large Preview", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                string previewDir = Path.Combine(Path.GetTempPath(), "OpenTransfer", "Preview");
                Directory.CreateDirectory(previewDir);
                string localPath = Path.Combine(previewDir, item.Name);
                TxtStatusFooter.Text = $"Preparing preview: {item.Name}";
                bool ok = await _adbService.PullFileWithProgressAsync(_activeSerial, item.Path, localPath, item.SizeBytes, new Progress<long>(), CancellationToken.None);
                if (!ok) throw new IOException("ADB could not download the preview file.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(localPath) { UseShellExecute = true });
                TxtStatusFooter.Text = "Preview opened";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Preview Failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e) => new TransferHistoryDialog { Owner = this }.ShowDialog();

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "OpenTransfer settings (*.json)|*.json", FileName = "OpenTransfer-settings.json" };
            if (dialog.ShowDialog() == true) SettingsService.Instance.Export(dialog.FileName);
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "OpenTransfer settings (*.json)|*.json" };
            if (dialog.ShowDialog() == true)
            {
                if (SettingsService.Instance.Import(dialog.FileName)) { LoadSettingsIntoUI(); MessageBox.Show("Settings imported."); }
                else MessageBox.Show("The selected settings file is invalid.", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset all settings to defaults?", "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            { SettingsService.Instance.Reset(); LoadSettingsIntoUI(); RefreshQuickAccess(); }
        }

        private void BtnClearLogFiles_Click(object sender, RoutedEventArgs e)
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openTransferWPF", "Logs");
            try { if (Directory.Exists(logDir)) foreach (string file in Directory.EnumerateFiles(logDir, "*.log")) File.Delete(file); MessageBox.Show("Log files cleared."); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Clear Logs Failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnCopyToPC_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial))
            {
                MessageBox.Show("Please connect an Android device first.", "No Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select PC Destination Folder to Copy Files Into",
                InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            if (dialog.ShowDialog() == true)
            {
                string destFolder = dialog.FolderName;

                // Smart Pre-Check: Check if any local file conflicts exist in destFolder
                bool hasConflict = false;
                if (Directory.Exists(destFolder))
                {
                    var remoteDict = await _adbService.ScanRemoteTreeAsDictionaryAsync(_activeSerial, _currentPath);
                    foreach (var relPath in remoteDict.Keys)
                    {
                        string localTargetPath = System.IO.Path.Combine(destFolder, relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        if (File.Exists(localTargetPath))
                        {
                            hasConflict = true;
                            break;
                        }
                    }
                }

                string? strategy = ResolveConflictStrategy(hasConflict);
                if (strategy == null) return;

                int queuePosition = Interlocked.Increment(ref _queuedTransferCount);
                if (queuePosition > 1) TxtStatusFooter.Text = $"Download queued (position {queuePosition})";
                await _transferGate.WaitAsync();
                Interlocked.Decrement(ref _queuedTransferCount);

                // Show Interactive Progress Dialog
                _copyCts = new CancellationTokenSource();
                var progressDlg = new TransferProgressDialog(_copyEngine, _copyCts) { Owner = this };

                int lastTotal = 0;
                int lastCopied = 0;
                bool transferFinished = false;

                var progress = new Progress<CopyEngineProgress>(p =>
                {
                    progressDlg.UpdateProgress(p);
                    lastTotal = p.TotalFileCount;
                    lastCopied = p.CopiedCount;
                    TxtStatusFooter.Text = $"Downloading ({p.CurrentFileIndex}/{p.TotalFileCount}): {p.CurrentFileName} | ⚡ {p.SpeedMbPerSec:0.#} MB/s | Skipped: {p.SkippedCount}";
                });

                var logProg = new Progress<string>(msg => Log(msg));

                progressDlg.Show();
                _transferActive = true;

                try
                {
                    await _copyEngine.RunDownloadJobAsync(
                        _activeSerial,
                        _currentPath,
                        destFolder,
                        strategy,
                        progress,
                        logProg,
                        _copyCts.Token
                    );
                    transferFinished = true;
                    Log("Download operation completed successfully!");
                }
                catch (OperationCanceledException)
                {
                    Log("Transfer Canceled by User.");
                }
                catch (Exception ex)
                {
                    Log($"Download Exception: {ex.Message}");
                }
                finally
                {
                    _transferActive = false;
                    TransferHistoryService.Instance.Add(new TransferHistoryEntry
                    {
                        Direction = "Download", Device = _activeSerial, Source = _currentPath, Destination = destFolder,
                        TotalFiles = lastTotal, CompletedFiles = lastCopied,
                        Result = transferFinished ? (lastCopied == lastTotal ? "Completed" : "Completed with issues") : "Cancelled / Failed"
                    });
                    ShowTransferNotification("Download finished", $"{lastCopied} of {lastTotal} files completed.");
                    if (transferFinished && !_copyCts.IsCancellationRequested)
                    {
                        progressDlg.ShowCompleteState(lastCopied, lastTotal, destFolder, isDownload: true);
                    }
                    else
                    {
                        progressDlg.Close();
                    }
                    _transferGate.Release();
                }
            }
        }

        private async void BtnTroubleshoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AdbGuideDialog { Owner = this };
            if (dlg.ShowDialog() == true && dlg.RequestCheckConnection)
            {
                await CheckDevicesAsync();
            }
        }

        private async void BtnUploadFiles_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial))
            {
                MessageBox.Show("Please connect an Android device first.", "No Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Files from PC to Upload to Phone",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                await StartUploadProcessAsync(dialog.FileNames.ToList());
            }
        }

        private void LstFiles_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private async void LstFiles_Drop(object sender, DragEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial))
            {
                MessageBox.Show("Please connect an Android phone via USB first.", "No Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    await StartUploadProcessAsync(files.ToList());
                }
            }
        }

        private async Task StartUploadProcessAsync(List<string> sourcePaths)
        {
            if (string.IsNullOrEmpty(_activeSerial)) return;

            // Smart Pre-Check: Check if any remote files conflict with incoming source paths
            var existingNames = new HashSet<string>(_allItems.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            bool hasConflict = false;
            foreach (var sp in sourcePaths)
            {
                string fname = System.IO.Path.GetFileName(sp);
                if (existingNames.Contains(fname))
                {
                    hasConflict = true;
                    break;
                }
            }

            string? strategy = ResolveConflictStrategy(hasConflict);
            if (strategy == null) return;

            int queuePosition = Interlocked.Increment(ref _queuedTransferCount);
            if (queuePosition > 1) TxtStatusFooter.Text = $"Upload queued (position {queuePosition})";
            await _transferGate.WaitAsync();
            Interlocked.Decrement(ref _queuedTransferCount);

            // Show Interactive Progress Dialog
            _copyCts = new CancellationTokenSource();
            var progressDlg = new TransferProgressDialog(_copyEngine, _copyCts) { Owner = this };

            int lastTotal = 0;
            int lastCopied = 0;
            bool transferFinished = false;

            var progress = new Progress<CopyEngineProgress>(p =>
            {
                progressDlg.UpdateProgress(p);
                lastTotal = p.TotalFileCount;
                lastCopied = p.CopiedCount;
                TxtStatusFooter.Text = $"Uploading ({p.CurrentFileIndex}/{p.TotalFileCount}): {p.CurrentFileName} | ⚡ {p.SpeedMbPerSec:0.#} MB/s | Skipped: {p.SkippedCount}";
            });

            var logProg = new Progress<string>(msg => Log(msg));

            progressDlg.Show();
            _transferActive = true;

            try
            {
                await _copyEngine.RunUploadJobAsync(
                    _activeSerial,
                    sourcePaths,
                    _currentPath,
                    strategy,
                    progress,
                    logProg,
                    _copyCts.Token
                );
                transferFinished = true;
                Log("Upload operation completed successfully!");
            }
            catch (OperationCanceledException)
            {
                Log("Upload Canceled by User.");
            }
            catch (Exception ex)
            {
                Log($"Upload Exception: {ex.Message}");
            }
            finally
            {
                _transferActive = false;
                TransferHistoryService.Instance.Add(new TransferHistoryEntry
                {
                    Direction = "Upload", Device = _activeSerial, Source = string.Join("; ", sourcePaths.Select(Path.GetFileName)), Destination = _currentPath,
                    TotalFiles = lastTotal, CompletedFiles = lastCopied,
                    Result = transferFinished ? (lastCopied == lastTotal ? "Completed" : "Completed with issues") : "Cancelled / Failed"
                });
                ShowTransferNotification("Upload finished", $"{lastCopied} of {lastTotal} files completed.");
                if (transferFinished && !_copyCts.IsCancellationRequested)
                {
                    progressDlg.ShowCompleteState(lastCopied, lastTotal, _currentPath, isDownload: false);
                }
                else
                {
                    progressDlg.Close();
                }
                _ = LoadDirectoryAsync(_currentPath);
                _transferGate.Release();
            }
        }

        // --- DISK SPACE ANALYZER LAUNCHERS ---

        private void OpenDiskAnalyzer(string targetPath)
        {
            if (string.IsNullOrEmpty(_activeSerial))
            {
                MessageBox.Show("Please connect an Android device first.", "No Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new DiskSpaceAnalyzerDialog(_adbService, _activeSerial, targetPath) { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnDiskAnalyzer_Click(object sender, RoutedEventArgs e)
        {
            OpenDiskAnalyzer(_currentPath);
        }

        private void TxtFreeStorage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OpenDiskAnalyzer("/sdcard");
        }

        private void ContextAnalyzeStorage_Click(object sender, RoutedEventArgs e)
        {
            if (LstFiles.SelectedItem is AndroidFileItem item && item.IsDir)
            {
                OpenDiskAnalyzer(item.Path);
            }
            else
            {
                OpenDiskAnalyzer(_currentPath);
            }
        }

        private async void ContextSplitFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSerial))
            {
                MessageBox.Show("Please connect an Android device first.", "No Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AndroidFileItem? targetFolder = LstFiles.SelectedItem as AndroidFileItem;
            string folderPath = targetFolder != null && targetFolder.IsDir ? targetFolder.Path : _currentPath;
            string folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(folderName)) folderName = "Selected Folder";

            var dlg = new SplitFolderDialog(folderName) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                int filesPerFolder = dlg.FilesPerFolder;
                string separator = dlg.Separator;
                bool processInnerFolders = dlg.ProcessInnerFolders;

                Log($"Starting folder split operation on '{folderPath}' ({filesPerFolder} files/folder)...");
                PnlActivityLogs.Visibility = Visibility.Visible;

                var cts = new CancellationTokenSource();
                var progressDlg = new TransferProgressDialog(_copyEngine, cts) { Owner = this };
                progressDlg.Show();

                var progress = new Progress<SplitFolderProgress>(p =>
                {
                    Log(p.Message);
                    progressDlg.UpdateSplitProgress(p);
                });

                try
                {
                    int moved = await Task.Run(() => _adbService.SplitFolderAsync(_activeSerial, folderPath, filesPerFolder, separator, processInnerFolders, progress));
                    Log($"✅ Folder split complete! Moved {moved} total files.");
                    progressDlg.ShowSplitCompleteState(moved);
                }
                catch (Exception ex)
                {
                    Log($"❌ Error during folder split: {ex.Message}");
                    progressDlg.Close();
                    MessageBox.Show($"Failed to split folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    await LoadDirectoryAsync(_currentPath);
                }
            }
        }

        // --- VIEW MODE TOGGLE (Grid Tile View vs List View) ---

        private void BtnGridView_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(isGrid: true);
        }

        private void BtnListView_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(isGrid: false);
        }

        private void SetViewMode(bool isGrid)
        {
            _isGridView = isGrid;
            if (_defaultGridView == null && LstFiles.View is GridView gv)
            {
                _defaultGridView = gv;
            }

            var fluxPrimaryContainer = (System.Windows.Media.Brush)FindResource("FluxPrimaryContainer");
            var fluxOutline = (System.Windows.Media.Brush)FindResource("FluxOutline");

            if (isGrid)
            {
                LstFiles.View = null;
                LstFiles.ItemTemplate = (DataTemplate)Resources["TileItemTemplate"];
                LstFiles.ItemsPanel = (ItemsPanelTemplate)Resources["WrapItemsPanel"];

                BdrGridView.Background = fluxPrimaryContainer;
                TxtGridViewIcon.Foreground = System.Windows.Media.Brushes.White;

                BdrListView.Background = System.Windows.Media.Brushes.Transparent;
                TxtListViewIcon.Foreground = fluxOutline;
            }
            else
            {
                LstFiles.View = _defaultGridView;
                LstFiles.ItemTemplate = null;
                LstFiles.ItemsPanel = (ItemsPanelTemplate)Resources["DefaultItemsPanel"];

                BdrListView.Background = fluxPrimaryContainer;
                TxtListViewIcon.Foreground = System.Windows.Media.Brushes.White;

                BdrGridView.Background = System.Windows.Media.Brushes.Transparent;
                TxtGridViewIcon.Foreground = fluxOutline;
            }
        }

        private Button[]? _settingsTabButtons;
        private UIElement[]? _settingsPanels;

        // --- SETTINGS APPLIER & IN-APP NAVIGATION ---

        private void InitSettingsPanels()
        {
            _settingsTabButtons = new Button[] { TabGeneral, TabAppearance, TabTransfers, TabDevice, TabExplorer, TabAdvanced, TabAbout };
            _settingsPanels = new UIElement[] { PnlGeneral, PnlAppearance, PnlTransfers, PnlDevice, PnlExplorer, PnlAdvanced, PnlAbout };
            SelectSettingsTab(0);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsTabButtons == null)
            {
                InitSettingsPanels();
            }

            LoadSettingsIntoUI();
            PnlFileExplorer.Visibility = Visibility.Collapsed;
            PnlSettingsView.Visibility = Visibility.Visible;
        }

        private void BtnHideSettings_Click(object sender, RoutedEventArgs e)
        {
            PnlSettingsView.Visibility = Visibility.Collapsed;
            PnlFileExplorer.Visibility = Visibility.Visible;
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
            {
                SelectSettingsTab(idx);
            }
        }

        private void SelectSettingsTab(int selectedIndex)
        {
            if (_settingsTabButtons == null || _settingsPanels == null) return;

            var greenBrush = (System.Windows.Media.Brush)FindResource("FluxPrimaryContainer");
            var onPrimaryBrush = System.Windows.Media.Brushes.White;
            var onSurfaceBrush = (System.Windows.Media.Brush)FindResource("FluxOnSurface");

            for (int i = 0; i < _settingsTabButtons.Length; i++)
            {
                if (i == selectedIndex)
                {
                    _settingsTabButtons[i].Background = greenBrush;
                    _settingsTabButtons[i].Foreground = onPrimaryBrush;
                    _settingsPanels[i].Visibility = Visibility.Visible;
                }
                else
                {
                    _settingsTabButtons[i].Background = System.Windows.Media.Brushes.Transparent;
                    _settingsTabButtons[i].Foreground = onSurfaceBrush;
                    _settingsPanels[i].Visibility = Visibility.Collapsed;
                }
            }
        }

        private void LoadSettingsIntoUI()
        {
            var s = SettingsService.Instance.Settings;

            // General
            ChkLaunchStartup.IsChecked = s.LaunchAtStartup;
            ChkMinimizeTray.IsChecked = s.MinimizeToTray;
            ChkAutoDetect.IsChecked = s.AutoDetectDevices;
            ChkAutoReconnect.IsChecked = s.AutoReconnect;

            // Appearance
            CmbThemePalette.SelectedIndex = s.ThemePalette switch
            {
                "VSCode" => 1,
                "Nord" => 2,
                "Dracula" => 3,
                _ => 0
            };
            string themeMode = s.ThemeMode == "System" && s.Theme is "Light" or "Dark" ? s.Theme : s.ThemeMode;
            CmbThemeMode.SelectedIndex = themeMode switch { "Light" => 1, "Dark" => 2, _ => 0 };
            CmbFolderColor.SelectedIndex = s.FolderColor switch
            {
                "Blue" => 1,
                "Green" => 2,
                "Purple" => 3,
                "Red" => 4,
                "Orange" => 5,
                _ => 0
            };

            // Transfers
            CmbConflict.SelectedIndex = s.ConflictResolution switch
            {
                "Ask" => 0,
                "Replace" => 2,
                "Rename" => 3,
                _ => 1
            };
            CmbCompareMethod.SelectedIndex = s.CompareMethod switch
            {
                "FilenameOnly" => 0,
                "FilenameSizeDate" => 2,
                _ => 1
            };
            ChkVerifyCopied.IsChecked = s.VerifyCopiedFiles;
            ChkRetryFailed.IsChecked = s.RetryFailedTransfers;
            CmbRetryCount.SelectedIndex = s.RetryCount switch
            {
                1 => 0,
                5 => 2,
                _ => 1
            };

            // Device
            CmbDefaultStorage.SelectedIndex = s.DefaultAndroidStorage == "SD Card" ? 1 : 0;
            CmbRefreshInterval.SelectedIndex = s.RefreshIntervalSeconds switch
            {
                1 => 0,
                5 => 2,
                _ => 1
            };

            // Explorer
            CmbDefaultView.SelectedIndex = s.DefaultView == "LargeIcons" ? 1 : 0;
            ChkShowHidden.IsChecked = s.ShowHiddenFiles;
            ChkShowExtensions.IsChecked = s.ShowFileExtensions;
            ChkShowFolderSizes.IsChecked = s.ShowFolderSizes;
            ChkPreserveDate.IsChecked = s.PreserveModifiedDate;
            ChkPreserveStructure.IsChecked = s.PreserveFolderStructure;
            ChkCreateMissing.IsChecked = s.CreateMissingFolders;
            ChkAutoSplit.IsChecked = s.AutoSplitOnTransfer;
            CmbAutoSplitSize.SelectedIndex = s.AutoSplitBatchSize switch
            {
                100 => 0,
                250 => 1,
                1000 => 3,
                _ => 2
            };
            CmbAutoSplitNaming.SelectedIndex = s.AutoSplitNamingFormat == "Day" ? 1 : 0;

            // Advanced
            TxtCustomAdbPath.Text = s.CustomAdbPath;
            CmbAdbTimeout.SelectedIndex = s.AdbTimeoutSeconds switch
            {
                60 => 1,
                120 => 2,
                _ => 0
            };
            ChkDebugLogging.IsChecked = s.EnableDebugLogging;
            CmbLogRetention.SelectedIndex = s.LogRetentionDays switch { 7 => 0, 30 => 2, 90 => 3, _ => 1 };
        }

        private void BtnBrowseAdb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select adb.exe Executable",
                Filter = "adb.exe|adb.exe|All Executables (*.exe)|*.exe"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtCustomAdbPath.Text = dlg.FileName;
            }
        }

        private void BtnOpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string appDataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openTransferWPF");
                if (!System.IO.Directory.Exists(appDataDir)) System.IO.Directory.CreateDirectory(appDataDir);
                System.Diagnostics.Process.Start("explorer.exe", appDataDir);
            }
            catch { }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var s = new AppSettings
            {
                // General
                LaunchAtStartup = ChkLaunchStartup.IsChecked == true,
                MinimizeToTray = ChkMinimizeTray.IsChecked == true,
                AutoDetectDevices = ChkAutoDetect.IsChecked == true,
                AutoReconnect = ChkAutoReconnect.IsChecked == true,

                // Appearance
                Theme = "System",
                ThemePalette = SelectedThemePalette(),
                ThemeMode = SelectedThemeMode(),
                Language = "English",
                FolderColor = CmbFolderColor.SelectedIndex switch
                {
                    1 => "Blue",
                    2 => "Green",
                    3 => "Purple",
                    4 => "Red",
                    5 => "Orange",
                    _ => "Yellow"
                },

                // Transfers
                ConflictResolution = CmbConflict.SelectedIndex switch
                {
                    0 => "Ask",
                    2 => "Replace",
                    3 => "Rename",
                    _ => "Skip"
                },
                CompareMethod = CmbCompareMethod.SelectedIndex switch
                {
                    0 => "FilenameOnly",
                    2 => "FilenameSizeDate",
                    _ => "FilenameSize"
                },
                TransferMode = "Balanced",
                BufferSize = "Auto",
                VerifyCopiedFiles = ChkVerifyCopied.IsChecked == true,
                RetryFailedTransfers = ChkRetryFailed.IsChecked == true,
                RetryCount = CmbRetryCount.SelectedIndex switch
                {
                    0 => 1,
                    2 => 5,
                    _ => 3
                },

                // Device
                DefaultAndroidStorage = CmbDefaultStorage.SelectedIndex == 1 ? "SD Card" : "/sdcard",
                RefreshIntervalSeconds = CmbRefreshInterval.SelectedIndex switch
                {
                    0 => 1,
                    2 => 5,
                    _ => 3
                },

                // Explorer
                DefaultView = CmbDefaultView.SelectedIndex == 1 ? "LargeIcons" : "Details",
                ShowHiddenFiles = ChkShowHidden.IsChecked == true,
                ShowFileExtensions = ChkShowExtensions.IsChecked == true,
                ShowFolderSizes = ChkShowFolderSizes.IsChecked == true,
                PreserveModifiedDate = ChkPreserveDate.IsChecked == true,
                PreserveFolderStructure = ChkPreserveStructure.IsChecked == true,
                CreateMissingFolders = ChkCreateMissing.IsChecked == true,
                AutoSplitOnTransfer = ChkAutoSplit.IsChecked == true,
                AutoSplitBatchSize = CmbAutoSplitSize.SelectedIndex switch
                {
                    0 => 100,
                    1 => 250,
                    3 => 1000,
                    _ => 500
                },
                AutoSplitNamingFormat = CmbAutoSplitNaming.SelectedIndex == 1 ? "Day" : "Photo",

                // Advanced
                CustomAdbPath = TxtCustomAdbPath.Text.Trim(),
                AdbTimeoutSeconds = CmbAdbTimeout.SelectedIndex switch
                {
                    1 => 60,
                    2 => 120,
                    _ => 30
                },
                EnableDebugLogging = ChkDebugLogging.IsChecked == true
                ,LogRetentionDays = CmbLogRetention.SelectedIndex switch { 0 => 7, 2 => 30, 3 => 90, _ => 14 }
                ,FavoritePaths = SettingsService.Instance.Settings.FavoritePaths.ToList()
                ,SortBy = SettingsService.Instance.Settings.SortBy
                ,SortDescending = SettingsService.Instance.Settings.SortDescending
            };

            SettingsService.Instance.SaveSettings(s);
            Log("⚙️ Application settings saved successfully.");
            BtnHideSettings_Click(sender, e);
        }

        private void ApplySettings(AppSettings s)
        {
            _adbService.UpdateSettings(s);
            if (_deviceTimer != null)
            {
                _deviceTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, s.RefreshIntervalSeconds));
                if (s.AutoDetectDevices) _deviceTimer.Start();
                else _deviceTimer.Stop();
            }

            ApplyTheme(s.ThemePalette, s.ThemeMode == "System" && s.Theme is "Light" or "Dark" ? s.Theme : s.ThemeMode);
            ApplyLaunchAtStartup(s.LaunchAtStartup);
            ConfigureTrayIcon(s.MinimizeToTray);
            CleanupOldLogs(s.LogRetentionDays);

            if (s.DefaultView == "LargeIcons" && !_isGridView)
            {
                SetViewMode(isGrid: true);
            }
            else if (s.DefaultView == "Details" && _isGridView)
            {
                SetViewMode(isGrid: false);
            }

            foreach (var item in _allItems)
            {
                item.FolderBrush = GetFolderBrush(s.FolderColor);
                item.DisplayName = s.ShowFileExtensions || item.IsDir
                    ? item.Name
                    : System.IO.Path.GetFileNameWithoutExtension(item.Name);
            }
            FilterAndRender();

            if (!string.IsNullOrEmpty(_activeSerial))
                _ = LoadDirectoryAsync(_currentPath);
        }

        private static string GetFolderBrush(string color) => color switch
        {
            "Blue" => "#3B82F6",
            "Green" => "#10B981",
            "Purple" => "#8B5CF6",
            "Red" => "#EF4444",
            "Orange" => "#F97316",
            _ => "#F59E0B"
        };

        private async Task<string> GetStartupStoragePathAsync()
        {
            string configured = SettingsService.Instance.Settings.DefaultAndroidStorage;
            if (configured != "SD Card" || string.IsNullOrEmpty(_activeSerial)) return "/sdcard";
            string? removable = await _adbService.FindRemovableStorageAsync(_activeSerial);
            if (removable == null) Log("No removable SD card detected; using internal storage.");
            return removable ?? "/sdcard";
        }

        private void ConfigureTrayIcon(bool enabled)
        {
            if (!enabled)
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
                return;
            }
            if (_trayIcon != null) return;

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open OpenTransfer", null, (_, _) => RestoreFromTray());
            menu.Items.Add("Exit", null, (_, _) => { _allowClose = true; Close(); });
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "OpenTransfer",
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty),
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        }

        private void ShowTransferNotification(string title, string message)
        {
            _trayIcon?.ShowBalloonTip(2500, title, message, Forms.ToolTipIcon.Info);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && SettingsService.Instance.Settings.MinimizeToTray)
            {
                Hide();
                _trayIcon?.ShowBalloonTip(1500, "OpenTransfer", "OpenTransfer is still running in the tray.", Forms.ToolTipIcon.Info);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose && SettingsService.Instance.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                Hide();
                return;
            }
            _trayIcon?.Dispose();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            var settings = SettingsService.Instance.Settings;
            if (settings.ThemeMode == "System")
                Dispatcher.Invoke(() => ApplyTheme(settings.ThemePalette, "System"));
        }

        private void AppearanceTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbThemePalette == null || CmbThemeMode == null) return;
            ApplyTheme(SelectedThemePalette(), SelectedThemeMode());
        }

        private string SelectedThemePalette() => CmbThemePalette.SelectedIndex switch
        {
            1 => "VSCode",
            2 => "Nord",
            3 => "Dracula",
            _ => "Emerald"
        };

        private string SelectedThemeMode() => CmbThemeMode.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };

        private string? ResolveConflictStrategy(bool hasConflict)
        {
            if (!hasConflict) return "SkipExisting";
            return SettingsService.Instance.Settings.ConflictResolution switch
            {
                "Skip" => "SkipExisting",
                "Replace" => "OverwriteAll",
                "Rename" => "Rename",
                _ => ShowConflictDialog()
            };
        }

        private string? ShowConflictDialog()
        {
            var conflictDlg = new ConflictDialog { Owner = this };
            return conflictDlg.ShowDialog() == true ? conflictDlg.SelectedStrategy : null;
        }

        private void ApplyLaunchAtStartup(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null) return;
                if (enabled)
                {
                    string exePath = Environment.ProcessPath ?? string.Empty;
                    if (!string.IsNullOrEmpty(exePath)) key.SetValue("OpenTransfer", $"\"{exePath}\"");
                }
                else key.DeleteValue("OpenTransfer", throwOnMissingValue: false);
            }
            catch (Exception ex) { Log($"Startup setting could not be applied: {ex.Message}"); }
        }

        private void ApplyTheme(string palette, string mode)
        {
            bool dark = mode == "Dark" || (mode == "System" && IsWindowsDarkMode());
            var colors = (palette, dark) switch
            {
                ("VSCode", false) => new[] { "#007ACC", "#FFFFFF", "#0067B8", "#FFFFFF", "#F3F3F3", "#FFFFFF", "#F8F8F8", "#F0F0F0", "#E8E8E8", "#1E1E1E", "#4B4B4B", "#6B6B6B", "#D0D0D0" },
                ("VSCode", true)  => new[] { "#3794FF", "#FFFFFF", "#007ACC", "#FFFFFF", "#181818", "#1E1E1E", "#252526", "#2D2D30", "#333337", "#F1F1F1", "#CCCCCC", "#9D9D9D", "#454545" },
                ("Nord", false)   => new[] { "#5E81AC", "#FFFFFF", "#4C6F96", "#FFFFFF", "#ECEFF4", "#FFFFFF", "#F7F9FC", "#E5E9F0", "#D8DEE9", "#2E3440", "#4C566A", "#66728A", "#C4CAD4" },
                ("Nord", true)    => new[] { "#88C0D0", "#2E3440", "#5E81AC", "#FFFFFF", "#242933", "#2E3440", "#343B49", "#3B4252", "#434C5E", "#ECEFF4", "#D8DEE9", "#AAB3C5", "#4C566A" },
                ("Dracula", false)=> new[] { "#7C3AED", "#FFFFFF", "#6D28D9", "#FFFFFF", "#F8F7FC", "#FFFFFF", "#F1EFF8", "#EAE6F4", "#DDD7EC", "#282A36", "#56586A", "#727386", "#CEC9DB" },
                ("Dracula", true) => new[] { "#BD93F9", "#282A36", "#8B5CF6", "#FFFFFF", "#1E1F29", "#282A36", "#30313D", "#383A4A", "#44475A", "#F8F8F2", "#D7D7D2", "#A8A8A2", "#55586D" },
                ("Emerald", true) => new[] { "#34D399", "#062B22", "#059669", "#FFFFFF", "#101815", "#151E1A", "#1B2722", "#22312A", "#293B32", "#ECFDF5", "#C7E8DA", "#90AFA2", "#3D554B" },
                _                  => new[] { "#059669", "#FFFFFF", "#047857", "#FFFFFF", "#F3FAF7", "#FFFFFF", "#F7FCFA", "#EAF6F1", "#DDF0E8", "#14231D", "#405B50", "#687E75", "#C5D8D0" }
            };

            SetBrush("FluxPrimary", colors[0]);
            SetBrush("FluxOnPrimary", colors[1]);
            SetBrush("FluxPrimaryContainer", colors[2]);
            SetBrush("FluxOnPrimaryContainer", colors[3]);
            SetBrush("FluxSurface", colors[4]);
            SetBrush("FluxBackground", colors[4]);
            SetBrush("FluxSurfaceContainerLowest", colors[5]);
            SetBrush("FluxSurfaceContainerLow", colors[6]);
            SetBrush("FluxSurfaceContainer", colors[7]);
            SetBrush("FluxSurfaceContainerHigh", colors[8]);
            SetBrush("FluxSurfaceContainerHighest", colors[8]);
            SetBrush("FluxSurfaceVariant", colors[8]);
            SetBrush("FluxOnSurface", colors[9]);
            SetBrush("FluxOnBackground", colors[9]);
            SetBrush("FluxOnSurfaceVariant", colors[10]);
            SetBrush("FluxOutline", colors[11]);
            SetBrush("FluxOutlineVariant", colors[12]);
            SetBrush("FluxSurfaceTint", colors[0]);
            SetBrush("FluxSelectedRowTint", dark ? "#3034D399" : "#20059669");
            SetBrush("FluxHoverTint", dark ? "#12FFFFFF" : "#08000000");

            if (_settingsTabButtons != null)
            {
                int selected = Array.FindIndex(_settingsPanels!, panel => panel.Visibility == Visibility.Visible);
                SelectSettingsTab(Math.Max(0, selected));
            }
        }

        private static bool IsWindowsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch { return false; }
        }

        private void SetBrush(string key, string hex)
        {
            if (FindResource(key) is SolidColorBrush brush)
                brush.Color = (Color)ColorConverter.ConvertFromString(hex);
        }

        private static void CleanupOldLogs(int retentionDays)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openTransferWPF", "Logs");
                if (!Directory.Exists(dir)) return;
                DateTime cutoff = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
                foreach (string file in Directory.EnumerateFiles(dir, "*.log")) if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch { }
        }

        // --- AUTO-UPDATE & GITHUB RELEASE CHECKER ---

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                var updateInfo = await UpdateService.Instance.CheckForUpdateAsync();
                if (updateInfo.IsUpdateAvailable)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var dlg = new Dialogs.UpdateDialog(updateInfo) { Owner = this };
                        dlg.ShowDialog();
                    });
                }
            }
            catch { }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            Log("🔍 Checking for updates on GitHub...");
            var updateInfo = await UpdateService.Instance.CheckForUpdateAsync();
            if (updateInfo.IsUpdateAvailable)
            {
                var dlg = new Dialogs.UpdateDialog(updateInfo) { Owner = this };
                dlg.ShowDialog();
            }
            else
            {
                MessageBox.Show($"You are running the latest version of OpenTransfer (v{UpdateService.CurrentAppVersion}).", "No Updates Available", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnInstagram_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.instagram.com/magical_world_i_see/") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnGitHubProfile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/amorsoftlab/OpenTransfer") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
