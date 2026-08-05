using System;
using System.Windows;

namespace openTransferWPF.Dialogs
{
    public partial class SplitFolderDialog : Window
    {
        public int FilesPerFolder { get; private set; } = 500;
        public string Separator { get; private set; } = "-";
        public bool ProcessInnerFolders { get; private set; } = true;

        public SplitFolderDialog(string folderName)
        {
            InitializeComponent();
            Title = $"Split Folder - {folderName}";
        }

        private void BtnSplit_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtFilesPerFolder.Text.Trim(), out int count) || count <= 0)
            {
                MessageBox.Show("Please enter a valid positive number for files per batch folder.", 
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FilesPerFolder = count;
            Separator = CmbSeparator.SelectedIndex == 1 ? "_" : "-";
            ProcessInnerFolders = ChkProcessInnerFolders.IsChecked == true;

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
