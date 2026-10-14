using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClashNavigator.Services;
using ClashNavigator.ViewModels;

namespace ClashNavigator.Views
{
    // Code-behind stays this thin on purpose: it builds the view model, gives it the one service
    // that needs a window (file dialogs), and parents itself to Revit. Everything else is bindings.
    public partial class ClashNavigatorWindow : Window
    {
        public ClashNavigatorWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel(new FileDialogService());
        }

        // Making Revit the owner keeps the window above Revit instead of getting lost behind it,
        // and closes it automatically when Revit closes.
        public void AttachTo(IntPtr revitWindowHandle)
        {
            if (revitWindowHandle == IntPtr.Zero) return;
            new WindowInteropHelper(this).Owner = revitWindowHandle;
        }

        // WPF does not select a grid row on right-click, so the context menu would act on
        // whatever was selected before. Selecting it here is what makes "right-click this clash,
        // copy its details" do what the user expects.
        private void OnRowRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = sender as DataGridRow;
            if (row != null) row.IsSelected = true;
        }
    }
}
