namespace ClashNavigator.Services
{
    // The view model needs to ask for a file path, but a view model must not open windows itself -
    // that is what keeps it testable. The window supplies the real implementation.
    public interface IFileDialogService
    {
        string AskForOpenPath(string title, string filter);
        string AskForSavePath(string title, string filter, string suggestedName);
        void ShowMessage(string title, string message);
        bool CopyToClipboard(string text);
    }
}
