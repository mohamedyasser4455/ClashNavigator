using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace ClashNavigator.App
{
    // Builds the ribbon entry. The class is called RibbonApplication and not Application on
    // purpose - Revit already has Autodesk.Revit.ApplicationServices.Application and the two
    // names collide in every file that uses both.
    public class RibbonApplication : IExternalApplication
    {
        private const string TabName = "KAITECH-BD-R11";
        private const string PanelName = "General";
        private const string CommandClass = "ClashNavigator.App.ClashNavigatorCommand";
        private const string LinkedIn = "https://www.linkedin.com/in/mohamed-y-khalil";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Another add-in from the same round may have created the tab already.
                try { application.CreateRibbonTab(TabName); }
                catch (Exception) { }

                RibbonPanel panel = GetOrCreatePanel(application);
                if (panel == null) return Result.Failed;

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                var buttonData = new PushButtonData(
                    "ClashNavigatorButton",
                    "Clash" + Environment.NewLine + "Navigator",
                    assemblyPath,
                    CommandClass);

                var button = panel.AddItem(buttonData) as PushButton;
                if (button != null)
                {
                    button.ToolTip = "Open a Navisworks clash report and walk through the clashes in the model.";
                    button.LongDescription =
                        "Reads the XML report exported from Navisworks Clash Detective, lists every clash, " +
                        "and for the clash you pick puts a section box around the two elements, selects them " +
                        "and zooms in - whether they live in this model or in a linked one.";

                    button.LargeImage = LoadEmbeddedImage("ClashNavigator32.png");
                    button.Image = LoadEmbeddedImage("ClashNavigator16.png");

                    button.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, LinkedIn));
                }

                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                // A failed ribbon must never stop Revit from starting.
                TaskDialog.Show("Clash Navigator", "The ribbon could not be created:" +
                                Environment.NewLine + exception.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
        {
            foreach (RibbonPanel existing in application.GetRibbonPanels(TabName))
            {
                if (string.Equals(existing.Name, PanelName, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            return application.CreateRibbonPanel(TabName, PanelName);
        }

        // The icons are compiled into the DLL, so the installer only ever copies one file and
        // the button can never end up blank because an image went missing.
        private static ImageSource LoadEmbeddedImage(string fileName)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();

                string resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null) return null;

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
