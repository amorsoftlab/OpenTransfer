using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using openTransferWPF.Models;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class DiskSpaceAnalyzerDialog : Window
    {
        private readonly AdbService _adbService;
        private readonly string _serial;
        private string _currentPath;
        private List<DiskUsageItem> _allItems = new();

        public DiskSpaceAnalyzerDialog(AdbService adbService, string serial, string initialPath)
        {
            InitializeComponent();
            _adbService = adbService;
            _serial = serial;
            _currentPath = string.IsNullOrEmpty(initialPath) ? "/sdcard" : initialPath;

            Loaded += async (s, e) => await LoadStorageDataAsync(_currentPath);
        }

        private async Task LoadStorageDataAsync(string remotePath)
        {
            if (string.IsNullOrEmpty(_serial)) return;

            PnlLoading.Visibility = Visibility.Visible;
            TxtLoadingStatus.Text = $"Analyzing space usage for [{remotePath}]...";
            TxtCurrentPath.Text = remotePath;
            _currentPath = remotePath;

            try
            {
                // 1. Fetch Capacity Details
                var capacity = await _adbService.GetStorageCapacityDetailsAsync(_serial);
                if (capacity.TotalBytes > 0)
                {
                    PrgStorageMeter.Value = capacity.UsedPercent;
                    TxtCapacityPercent.Text = $"{capacity.UsedPercent:0.#}% Used";
                    TxtStorageDetails.Text = $"Used {FormatBytes(capacity.UsedBytes)} / Free {FormatBytes(capacity.FreeBytes)} of {FormatBytes(capacity.TotalBytes)}";
                    TxtFreeSpaceText.Text = $"Free Space: {FormatBytes(capacity.FreeBytes)}";
                }
                else
                {
                    PrgStorageMeter.Value = 0;
                    TxtCapacityPercent.Text = "--";
                    TxtStorageDetails.Text = "Storage capacity details unavailable";
                }

                // 2. Fetch Analyzed Space Items
                var items = await _adbService.AnalyzeDiskSpaceAsync(_serial, remotePath);
                _allItems = items;

                FilterAndRender();
                long totalScan = items.Sum(i => i.SizeBytes);
                TxtStatusSummary.Text = $"Scanned {items.Count} item(s) in [{remotePath}] (Total: {FormatBytes(totalScan)})";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Disk analysis error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PnlLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void FilterAndRender()
        {
            string q = TxtSearchFilter.Text.Trim().ToLower();
            var filtered = _allItems.Where(i => string.IsNullOrEmpty(q) || i.Name.ToLower().Contains(q)).ToList();

            LstDiskUsage.ItemsSource = filtered;
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndRender();
        }

        private async void LstDiskUsage_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstDiskUsage.SelectedItem is DiskUsageItem item && item.IsDir)
            {
                await LoadStorageDataAsync(item.Path);
            }
        }

        private async void ContextDrillDown_Click(object sender, RoutedEventArgs e)
        {
            if (LstDiskUsage.SelectedItem is DiskUsageItem item && item.IsDir)
            {
                await LoadStorageDataAsync(item.Path);
            }
            else
            {
                MessageBox.Show("Please select a folder to drill down.", "Drill Down", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ContextDelete_Click(object sender, RoutedEventArgs e)
        {
            if (LstDiskUsage.SelectedItem is DiskUsageItem item)
            {
                var confirm = MessageBox.Show($"Are you sure you want to permanently delete '{item.Name}' ({item.FormattedSize}) from Android?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    bool ok = await _adbService.DeleteItemAsync(_serial, item.Path);
                    if (ok)
                    {
                        await LoadStorageDataAsync(_currentPath);
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete item from Android.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnUpFolder_Click(object sender, RoutedEventArgs e)
        {
            string parent = System.IO.Path.GetDirectoryName(_currentPath.TrimEnd('/'))?.Replace('\\', '/') ?? "/sdcard";
            if (string.IsNullOrEmpty(parent) || parent == "/") parent = "/sdcard";
            await LoadStorageDataAsync(parent);
        }

        private async void BtnRescan_Click(object sender, RoutedEventArgs e)
        {
            await LoadStorageDataAsync(_currentPath);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.#} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";
        }
    }
}
