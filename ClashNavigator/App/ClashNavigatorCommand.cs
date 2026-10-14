using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClashNavigator.Core;
using ClashNavigator.Views;

namespace ClashNavigator.App
{
    // What the ribbon button runs. It only opens the window - all the real work happens later,
    // driven by the user and routed back into Revit through RevitTask.
    [Transaction(TransactionMode.Manual)]
    public class ClashNavigatorCommand : IExternalCommand
    {
        // Kept static so a second click focuses the window already on screen instead of
        // opening a second copy with a second clash list.
        private static ClashNavigatorWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;

            if (uiApplication.ActiveUIDocument == null)
            {
                TaskDialog.Show("Clash Navigator", "Open a Revit model before running Clash Navigator.");
                return Result.Cancelled;
            }

            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            // ExternalEvent.Create has to happen here, inside a real API context.
            RevitTask.Initialize();

            _window = new ClashNavigatorWindow();
            _window.AttachTo(uiApplication.MainWindowHandle);
            _window.Closed += delegate { _window = null; };
            _window.Show();

            return Result.Succeeded;
        }
    }
}
