using System;
using System.IO;
using System.Windows;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class UpdateDialog : Window
    {
        private readonly UpdateInfo _info;

        public UpdateDialog(UpdateInfo info)
        {
            InitializeComponent();
            _info = info;

            TxtCurrentVersion.Text = $"v{info.CurrentVersion}";
            TxtLatestVersion.Text = $"v{info.LatestVersion}";
            TxtChangelog.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "New stability and performance updates available." : info.ReleaseNotes;
        }

        private async void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_info.DownloadUrl))
            {
                // Fallback to opening GitHub release page in browser
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_info.HtmlUrl) { UseShellExecute = true });
                }
                catch { }
                Close();
                return;
            }

            BtnUpdateNow.IsEnabled = false;
            BtnLater.IsEnabled = false;
            PnlProgress.Visibility = Visibility.Visible;
            TxtProgressStatus.Text = "Downloading installer...";

            try
            {
                string installerPath = await UpdateService.Instance.DownloadUpdateAsync(_info.DownloadUrl, (downloaded, total) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (total > 0)
                        {
                            int pct = (int)((downloaded * 100) / total);
                            PbDownload.Value = pct;
                            TxtProgressPercent.Text = $"{pct}%";
                            double mbRead = downloaded / (1024.0 * 1024.0);
                            double mbTotal = total / (1024.0 * 1024.0);
                            TxtProgressStatus.Text = $"Downloaded {mbRead:F1} MB of {mbTotal:F1} MB";
                        }
                    });
                });

                TxtProgressStatus.Text = "Launching installer...";
                UpdateService.Instance.LaunchInstaller(installerPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to download update: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnUpdateNow.IsEnabled = true;
                BtnLater.IsEnabled = true;
                PnlProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
