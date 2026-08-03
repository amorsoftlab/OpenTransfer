using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

        public MainWindow()
        {
            InitializeComponent();

            _adbService = new AdbService();
            _copyEngine = new CopyEngine(_adbService);

            Log("Connected to Redmi Note 7 Pro");

            // Device Polling Timer
            _deviceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(SettingsService.Instance.Settings.RefreshIntervalSeconds)
            };
            _deviceTimer.Tick += async (s, e) => await CheckDevicesAsync();
            _deviceTimer.Start();

            // Initial check & Settings listener
            Loaded += async (s, e) =>
            {
                ApplySettings(SettingsService.Instance.Settings);
                await CheckDevicesAsync();
                _ = CheckForUpdatesOnStartupAsync();
            };

            SettingsService.Instance.SettingsChanged += (s, settings) => ApplySettings(settings);
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

        private async Task CheckDevicesAsync()
        {
            bool available = await _adbService.IsAdbAvailableAsync();
            if (!available)
            {
                TxtStatusFooter.Text = "ADB Binary Not Found";
                CmbDevices.ItemsSource = null;
                PnlNoDevice.Visibility = Visibility.Visible;
                LstFiles.Visibility = Visibility.Collapsed;
                return;
            }

            var devices = await _adbService.GetDevicesAsync();
            if (devices.Count == 0)
            {
                TxtStatusFooter.Text = "No Android device connected";
                CmbDevices.ItemsSource = null;
                PnlNoDevice.Visibility = Visibility.Visible;
                LstFiles.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(_activeSerial))
                {
                    Log("🔴 Device Disconnected.");
                    _activeSerial = string.Empty;
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
                    CmbDevices.SelectedIndex = 0;
                }
            }
        }

        private void CmbDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDevices.SelectedItem is DeviceItem dev)
            {
                if (_activeSerial != dev.Serial)
                {
                    _activeSerial = dev.Serial;
                    Title = $"Android File Transfer - {dev.Model}";
                    Log($"Connected to {dev.Model}");
                    _ = UpdateStorageInfoAsync();
                    _ = LoadDirectoryAsync("/sdcard");
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

            TxtStatusFooter.Text = $"Loading {remotePath}...";
            Log($"{remotePath} folder indexed");
            var items = await _adbService.ListDirectoryAsync(_activeSerial, remotePath);

            foreach (var item in items)
            {
                item.ResolveCategoryIcon();
            }

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
            string q = TxtSearch.Text.Trim().ToLower();
            var filtered = _allItems.Where(i => string.IsNullOrEmpty(q) || i.Name.ToLower().Contains(q)).ToList();

            LstFiles.ItemsSource = filtered;
            TxtStatusFooter.Text = $"{filtered.Count} Items";
            UpdateSelectionStats();
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
            await CheckDevicesAsync();
            if (!string.IsNullOrEmpty(_activeSerial))
            {
                _ = LoadDirectoryAsync(_currentPath);
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
                bool ok = await _adbService.CleanEmptyFoldersAsync(_activeSerial, targetPath);
                if (ok)
                {
                    Log($"Cleaned empty folders in '{targetPath}'");
                    TxtStatusFooter.Text = "Cleanup complete.";
                    
                    var successDialog = new openTransferWPF.Dialogs.SuccessDialog(
                        "Cleanup Complete", 
                        "All empty folders and .DAT files have been removed successfully."
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
                _ = LoadDirectoryAsync(_currentPath);
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

                string strategy = "SkipExisting";
                if (hasConflict)
                {
                    var conflictDlg = new ConflictDialog { Owner = this };
                    if (conflictDlg.ShowDialog() != true) return;
                    strategy = conflictDlg.SelectedStrategy;
                }

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
                    if (transferFinished && !_copyCts.IsCancellationRequested)
                    {
                        progressDlg.ShowCompleteState(lastCopied, lastTotal, destFolder, isDownload: true);
                    }
                    else
                    {
                        progressDlg.Close();
                    }
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

            string strategy = "SkipExisting";
            if (hasConflict)
            {
                var conflictDlg = new ConflictDialog { Owner = this };
                if (conflictDlg.ShowDialog() != true) return;
                strategy = conflictDlg.SelectedStrategy;
            }

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
                if (transferFinished && !_copyCts.IsCancellationRequested)
                {
                    progressDlg.ShowCompleteState(lastCopied, lastTotal, _currentPath, isDownload: false);
                }
                else
                {
                    progressDlg.Close();
                }
                _ = LoadDirectoryAsync(_currentPath);
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

            var greenBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
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
            CmbTheme.SelectedIndex = s.Theme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };
            CmbLanguage.SelectedIndex = s.Language == "Malayalam" ? 1 : 0;
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
            CmbTransferMode.SelectedIndex = s.TransferMode switch
            {
                "MaxSpeed" => 0,
                "MaxCompatibility" => 2,
                _ => 1
            };
            CmbBufferSize.SelectedIndex = s.BufferSize switch
            {
                "512 KB" => 1,
                "1 MB" => 2,
                "2 MB" => 3,
                "4 MB" => 4,
                "8 MB" => 5,
                _ => 0
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
                Theme = CmbTheme.SelectedIndex switch
                {
                    1 => "Light",
                    2 => "Dark",
                    _ => "System"
                },
                Language = CmbLanguage.SelectedIndex == 1 ? "Malayalam" : "English",
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
                TransferMode = CmbTransferMode.SelectedIndex switch
                {
                    0 => "MaxSpeed",
                    2 => "MaxCompatibility",
                    _ => "Balanced"
                },
                BufferSize = CmbBufferSize.SelectedIndex switch
                {
                    1 => "512 KB",
                    2 => "1 MB",
                    3 => "2 MB",
                    4 => "4 MB",
                    5 => "8 MB",
                    _ => "Auto"
                },
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
            }

            if (s.DefaultView == "LargeIcons" && !_isGridView)
            {
                SetViewMode(isGrid: true);
            }
            else if (s.DefaultView == "Details" && _isGridView)
            {
                SetViewMode(isGrid: false);
            }
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