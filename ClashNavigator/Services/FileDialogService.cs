using System.Windows;
using Microsoft.Win32;

namespace ClashNavigator.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string AskForOpenPath(string title, string filter)
        {
            var dialog = new OpenFileDialog();
            dialog.Title = title;
            dialog.Filter = filter;
            dialog.CheckFileExists = true;
            dialog.Multiselect = false;

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }

        public string AskForSavePath(string title, string filter, string suggestedName)
        {
            var dialog = new SaveFileDialog();
            dialog.Title = title;
            dialog.Filter = filter;
            dialog.FileName = suggestedName;
            dialog.OverwritePrompt = true;

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }

        public void ShowMessage(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // The Windows clipboard is shared with every other process and can genuinely be busy,
        // so this reports whether it worked instead of throwing at the user.
        public bool CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
