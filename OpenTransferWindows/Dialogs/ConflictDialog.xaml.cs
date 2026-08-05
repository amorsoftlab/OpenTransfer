using System.Windows;

namespace openTransferWPF.Dialogs
{
    public partial class ConflictDialog : Window
    {
        public string SelectedStrategy { get; private set; } = "SkipExisting";

        public ConflictDialog()
        {
            InitializeComponent();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (RadOverwrite.IsChecked == true)
                SelectedStrategy = "OverwriteAll";
            else
                SelectedStrategy = "SkipExisting";

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
