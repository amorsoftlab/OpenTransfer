using System.Windows;
using System.Windows.Controls;
using openTransferWPF.Models;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class TransferHistoryDialog : Window
    {
        public TransferHistoryEntry? RetryRequestedEntry { get; private set; }

        public TransferHistoryDialog()
        {
            InitializeComponent();
            Reload();
        }
        private void Reload() => LstHistory.ItemsSource = TransferHistoryService.Instance.Load();
        private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstHistory.SelectedItem is TransferHistoryEntry entry && entry.CanRetry)
            {
                BtnRetryFailed.IsEnabled = true;
                BtnRetryFailed.Content = $"↻ Retry Failed ({entry.FailedCount})";
            }
            else
            {
                BtnRetryFailed.IsEnabled = false;
                BtnRetryFailed.Content = "↻ Retry Failed";
            }
        }

        private void RetryFailed_Click(object sender, RoutedEventArgs e)
        {
            if (LstHistory.SelectedItem is not TransferHistoryEntry { CanRetry: true } entry) return;
            RetryRequestedEntry = entry;
            DialogResult = true;
        }
        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Clear all transfer history?", "Transfer History", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            { TransferHistoryService.Instance.Clear(); Reload(); }
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
