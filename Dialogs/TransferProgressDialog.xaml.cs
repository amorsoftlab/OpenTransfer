using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using openTransferWPF.Models;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class TransferProgressDialog : Window
    {
        private readonly CopyEngine _copyEngine;
        private readonly CancellationTokenSource _cts;
        private string _targetFolderPath = string.Empty;

        public TransferProgressDialog(CopyEngine copyEngine, CancellationTokenSource cts)
        {
            InitializeComponent();
            _copyEngine = copyEngine;
            _cts = cts;
        }

        public void UpdateProgress(CopyEngineProgress p)
        {
            // Current File Progress
            PrgFileBar.Value = p.CurrentFilePercent;
            TxtCurrentFilePercent.Text = $"{p.CurrentFilePercent:0}%";
            TxtCurrentFile.Text = string.IsNullOrEmpty(p.CurrentFileName) ? "Preparing transfer..." : p.CurrentFileName;

            // Total Progress
            PrgBar.Value = p.TotalPercent;
            TxtTotalPercent.Text = $"{p.TotalPercent:0}%";

            // Headers & Subheaders
            TxtSubHeader.Text = p.TotalFileCount > 0 ? $"({p.CurrentFileIndex}/{p.TotalFileCount} items)" : "Scanning files...";

            // Speed & ETA Stats
            TxtSpeed.Text = $"⚡ {p.SpeedMbPerSec:0.#} MB/s";
            TxtSkipped.Text = $"✨ {p.SkippedCount} Skipped | {p.CopiedCount} Copied";
            TxtEta.Text = p.EtaSeconds > 0 ? $"About {p.EtaSeconds}s remaining" : "Calculating...";

            if (p.IsPaused)
            {
                BtnPause.Content = "Resume";
                TxtHeader.Text = "Transfer Paused";
            }
            else
            {
                BtnPause.Content = "Pause";
                TxtHeader.Text = $"{p.DirectionLabel} files...";
            }
        }

        /// <summary>
        /// Updates progress UI during folder split operation.
        /// </summary>
        public void UpdateSplitProgress(SplitFolderProgress p)
        {
            PrgBar.Value = p.Percent;
            TxtTotalPercent.Text = $"{p.Percent}%";

            PrgFileBar.Value = p.Percent;
            TxtCurrentFilePercent.Text = $"{p.Percent}%";

            TxtHeader.Text = "Splitting Folder Files...";
            TxtSubHeader.Text = p.TotalCount > 0 ? $"({p.MovedCount}/{p.TotalCount} files moved)" : "Scanning files...";

            TxtCurrentFile.Text = p.Message;
            TxtSpeed.Text = $"📁 {p.MovedCount} Moved";
            TxtSkipped.Text = $"✨ Total {p.TotalCount} Files";
            TxtEta.Text = $"{p.Percent}% Complete";
        }

        public void ShowSplitCompleteState(int totalMoved)
        {
            PnlProgressState.Visibility = Visibility.Collapsed;
            PnlCompleteState.Visibility = Visibility.Visible;

            Title = "Split Complete";
            TxtCompleteMessage.Text = $"Folder split operation completed successfully!\nMoved {totalMoved} total files into batch subfolders.";
            BtnOpenFolder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Transitions the dialog into the Transfer Complete success state matching reference design.
        /// </summary>
        public void ShowCompleteState(int copiedCount, int totalCount, string destFolder, bool isDownload)
        {
            _targetFolderPath = destFolder;
            PnlProgressState.Visibility = Visibility.Collapsed;
            PnlCompleteState.Visibility = Visibility.Visible;

            Title = "Transfer Complete";
            string targetDeviceStr = isDownload ? "your local computer" : "your Android phone";
            TxtCompleteMessage.Text = $"All {totalCount} selected files have been successfully transferred to {targetDeviceStr}.";

            if (!isDownload || !Directory.Exists(_targetFolderPath))
            {
                BtnOpenFolder.Visibility = Visibility.Collapsed;
            }
            else
            {
                BtnOpenFolder.Visibility = Visibility.Visible;
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_copyEngine.IsPaused)
            {
                _copyEngine.Resume();
                BtnPause.Content = "Pause";
                TxtHeader.Text = "Copying files...";
            }
            else
            {
                _copyEngine.Pause();
                BtnPause.Content = "Resume";
                TxtHeader.Text = "Transfer Paused";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("Are you sure you want to cancel the transfer?", "Cancel Transfer", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                _cts.Cancel();
                Close();
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_targetFolderPath) && Directory.Exists(_targetFolderPath))
            {
                try
                {
                    Process.Start("explorer.exe", _targetFolderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            Close();
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
