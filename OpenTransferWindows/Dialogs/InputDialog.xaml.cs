using System;
using System.Windows;

namespace openTransferWPF.Dialogs
{
    public partial class InputDialog : Window
    {
        public string InputText => TxtInput.Text;

        public InputDialog(string title, string prompt, string defaultText = "", string okButtonText = "OK")
        {
            InitializeComponent();

            Title = title;
            TxtTitle.Text = title;
            TxtPrompt.Text = prompt;
            TxtInput.Text = defaultText;
            BtnOk.Content = okButtonText;

            Loaded += (s, e) =>
            {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        public static string? Show(Window owner, string title, string prompt, string defaultText = "", string okButtonText = "OK")
        {
            var dlg = new InputDialog(title, prompt, defaultText, okButtonText)
            {
                Owner = owner
            };

            if (dlg.ShowDialog() == true)
            {
                return dlg.InputText;
            }

            return null;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
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
