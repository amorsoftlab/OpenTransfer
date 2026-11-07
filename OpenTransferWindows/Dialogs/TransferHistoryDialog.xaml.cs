using System.Windows;
using openTransferWPF.Services;

namespace openTransferWPF.Dialogs
{
    public partial class TransferHistoryDialog : Window
    {
        public TransferHistoryDialog()
        {
            InitializeComponent();
            Reload();
        }
        private void Reload() => LstHistory.ItemsSource = TransferHistoryService.Instance.Load();
        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Clear all transfer history?", "Transfer History", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            { TransferHistoryService.Instance.Clear(); Reload(); }
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
