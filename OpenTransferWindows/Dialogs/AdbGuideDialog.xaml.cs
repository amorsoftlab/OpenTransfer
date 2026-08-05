using System.Windows;

namespace openTransferWPF.Dialogs
{
    public partial class AdbGuideDialog : Window
    {
        public bool RequestCheckConnection { get; private set; }

        public AdbGuideDialog()
        {
            InitializeComponent();
        }

        private void BtnCheckConnection_Click(object sender, RoutedEventArgs e)
        {
            RequestCheckConnection = true;
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
