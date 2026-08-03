using System.Windows;

namespace openTransferWPF.Dialogs
{
    public partial class SuccessDialog : Window
    {
        public SuccessDialog(string title, string message)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtMessage.Text = message;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
