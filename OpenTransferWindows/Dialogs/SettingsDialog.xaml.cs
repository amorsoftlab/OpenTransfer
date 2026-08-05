using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using openTransferWPF.Models;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly Button[] _tabButtons;
        private readonly UIElement[] _panels;

        public SettingsDialog()
        {
            InitializeComponent();

            _tabButtons = new Button[] { TabGeneral, TabAppearance, TabTransfers, TabDevice, TabExplorer, TabAdvanced, TabAbout };
            _panels = new UIElement[] { PnlGeneral, PnlAppearance, PnlTransfers, PnlDevice, PnlExplorer, PnlAdvanced, PnlAbout };

            SelectTab(0);
            LoadSettingsIntoUI();
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
            {
                SelectTab(idx);
            }
        }

        private void SelectTab(int selectedIndex)
        {
            var greenBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Emerald Green
            var onPrimaryBrush = Brushes.White;
            var onSurfaceBrush = (Brush)FindResource("FluxOnSurface");

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (i == selectedIndex)
                {
                    _tabButtons[i].Background = greenBrush;
                    _tabButtons[i].Foreground = onPrimaryBrush;
                    _panels[i].Visibility = Visibility.Visible;
                }
                else
                {
                    _tabButtons[i].Background = Brushes.Transparent;
                    _tabButtons[i].Foreground = onSurfaceBrush;
                    _panels[i].Visibility = Visibility.Collapsed;
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
            var dlg = new OpenFileDialog
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
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openTransferWPF");
                if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);
                Process.Start("explorer.exe", appDataDir);
            }
            catch { }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
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
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
