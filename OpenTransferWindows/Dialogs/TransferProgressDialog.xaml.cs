using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Animation;
using openTransferWPF.Models;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class TransferProgressDialog : Window
    {
        private readonly CopyEngine _copyEngine;
        private readonly CancellationTokenSource _cts;
        private string _targetFolderPath = string.Empty;
        private Storyboard? _transferAnimation;
        private string _animationDirection = string.Empty;

        public TransferProgressDialog(CopyEngine copyEngine, CancellationTokenSource cts)
        {
            InitializeComponent();
            _copyEngine = copyEngine;
            _cts = cts;
            Loaded += (_, _) => StartTransferAnimation("Downloading");
        }

        private void StartTransferAnimation(string direction)
        {
            bool upload = direction.Contains("Upload", StringComparison.OrdinalIgnoreCase);
            string normalized = upload ? "Upload" : "Download";
            if (_animationDirection == normalized && _transferAnimation != null) return;
            _animationDirection = normalized;

            _transferAnimation?.Stop(this);
            TxtSourceDevice.Text = upload ? "🖥️" : "📱";
            TxtTargetDevice.Text = upload ? "📱" : "🖥️";
            TxtSourceLabel.Text = upload ? "PC" : "PHONE";
            TxtTargetLabel.Text = upload ? "PHONE" : "PC";

            double from = upload ? 278 : 0;
            double to = upload ? 0 : 278;
            TimeSpan travelTime = TimeSpan.FromSeconds(1.7);
            _transferAnimation = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            AddMotion(_transferAnimation, FileGlowTranslate, from, to, travelTime, TimeSpan.Zero);
            AddMotion(_transferAnimation, FileTranslate, from, to, travelTime, TimeSpan.Zero);
            AddMotion(_transferAnimation, ParticleOneTranslate, from, to, travelTime, TimeSpan.FromMilliseconds(140));
            AddMotion(_transferAnimation, ParticleTwoTranslate, from, to, travelTime, TimeSpan.FromMilliseconds(290));
            AddMotion(_transferAnimation, ParticleThreeTranslate, from, to, travelTime, TimeSpan.FromMilliseconds(440));

            var pulseX = new DoubleAnimation(0.94, 1.06, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(pulseX, FileScale);
            Storyboard.SetTargetProperty(pulseX, new PropertyPath("ScaleX"));
            _transferAnimation.Children.Add(pulseX);

            var pulseY = pulseX.Clone();
            Storyboard.SetTarget(pulseY, FileScale);
            Storyboard.SetTargetProperty(pulseY, new PropertyPath("ScaleY"));
            _transferAnimation.Children.Add(pulseY);

            AddParticleFade(_transferAnimation, ParticleOne, 0.82, TimeSpan.Zero);
            AddParticleFade(_transferAnimation, ParticleTwo, 0.72, TimeSpan.FromMilliseconds(150));
            AddParticleFade(_transferAnimation, ParticleThree, 0.58, TimeSpan.FromMilliseconds(300));

            _transferAnimation.Begin(this, true);
        }

        private static void AddMotion(Storyboard storyboard, DependencyObject target, double from, double to, TimeSpan duration, TimeSpan delay)
        {
            var motion = new DoubleAnimation(from, to, duration)
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(motion, target);
            Storyboard.SetTargetProperty(motion, new PropertyPath("X"));
            storyboard.Children.Add(motion);
        }

        private static void AddParticleFade(Storyboard storyboard, DependencyObject target, double peakOpacity, TimeSpan delay)
        {
            var fade = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = delay,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(1.7)
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(peakOpacity, KeyTime.FromPercent(0.18), new SineEase { EasingMode = EasingMode.EaseOut }));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(peakOpacity * 0.65, KeyTime.FromPercent(0.62)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.9), new SineEase { EasingMode = EasingMode.EaseIn }));
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            storyboard.Children.Add(fade);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblBytes = bytes;
            while (dblBytes >= 1024 && i < suffixes.Length - 1)
            {
                dblBytes /= 1024;
                i++;
            }
            return $"{dblBytes:0.##} {suffixes[i]}";
        }

        private static string FormatEta(int seconds, double percent, double speedMbPerSec)
        {
            if (seconds <= 0 || speedMbPerSec <= 0 || percent < 1) return "Estimating time...";
            if (seconds < 60) return $"About {seconds} sec remaining";

            var time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 24) return "More than a day remaining";
            if (time.TotalHours >= 1)
                return $"About {(int)time.TotalHours} hr {time.Minutes} min remaining";
            return time.Seconds > 0
                ? $"About {time.Minutes} min {time.Seconds} sec remaining"
                : $"About {time.Minutes} min remaining";
        }

        public void UpdateProgress(CopyEngineProgress p)
        {
            StartTransferAnimation(p.DirectionLabel);
            // Current File Progress
            PrgFileBar.Value = p.CurrentFilePercent;
            TxtCurrentFilePercent.Text = $"{p.CurrentFilePercent:0}%";
            TxtCurrentFileBytes.Text = $"{FormatBytes(p.CurrentFileBytesTransferred)} / {FormatBytes(p.CurrentFileTotalBytes)}";
            TxtCurrentFile.Text = string.IsNullOrEmpty(p.CurrentFileName) ? "Preparing transfer..." : p.CurrentFileName;

            // Total Progress & Size Stats
            PrgBar.Value = p.TotalPercent;
            TxtTotalPercent.Text = $"{p.TotalPercent:0}%";
            TxtTotalBytes.Text = $"{FormatBytes(p.BytesTransferred)} / {FormatBytes(p.TotalBytes)}";

            long remainingBytes = Math.Max(0, p.TotalBytes - p.BytesTransferred);
            TxtRemainingSize.Text = $"📦 Remaining: {FormatBytes(remainingBytes)}";
            TxtFileCountRatio.Text = p.TotalFileCount > 0 ? $"{p.CurrentFileIndex} / {p.TotalFileCount} items" : "Scanning...";

            // Headers & Subheaders
            TxtSubHeader.Text = p.TotalFileCount > 0 ? $"({p.CurrentFileIndex}/{p.TotalFileCount} items)" : "Scanning files...";

            // Speed & ETA Stats
            TxtSpeed.Text = $"⚡ {p.SpeedMbPerSec:0.#} MB/s";
            TxtSkipped.Text = $"✨ {p.SkippedCount} Skipped | {p.CopiedCount} Copied";
            TxtEta.Text = FormatEta(p.EtaSeconds, p.TotalPercent, p.SpeedMbPerSec);

            if (p.IsPaused)
            {
                _transferAnimation?.Pause(this);
                BtnPause.Content = "Resume";
                TxtHeader.Text = "Transfer Paused";
            }
            else
            {
                _transferAnimation?.Resume(this);
                BtnPause.Content = "Pause";
                TxtHeader.Text = $"{p.DirectionLabel} files...";
            }
        }

        /// <summary>
        /// Updates progress UI during folder split operation.
        /// </summary>
        public void UpdateSplitProgress(SplitFolderProgress p)
        {
            StartTransferAnimation("Downloading");
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
            _transferAnimation?.Stop(this);
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
            _transferAnimation?.Stop(this);
            _targetFolderPath = destFolder;
            PnlProgressState.Visibility = Visibility.Collapsed;
            PnlCompleteState.Visibility = Visibility.Visible;

            Title = "Transfer Complete";
            string targetDeviceStr = isDownload ? "your local computer" : "your Android phone";
            TxtCompleteMessage.Text = $"All {totalCount} selected files have been successfully transferred to {targetDeviceStr}.";
            BtnShowFailed.Visibility = _copyEngine.LastFailedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_copyEngine.LastFailedItems.Count > 0)
                TxtCompleteMessage.Text = $"{copiedCount} of {totalCount} files completed. {_copyEngine.LastFailedItems.Count} failed and can be reviewed.";

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
                _transferAnimation?.Resume(this);
                BtnPause.Content = "Pause";
                TxtHeader.Text = "Copying files...";
            }
            else
            {
                _copyEngine.Pause();
                _transferAnimation?.Pause(this);
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

        private void BtnShowFailed_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(string.Join(Environment.NewLine, _copyEngine.LastFailedItems), "Failed Files", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
