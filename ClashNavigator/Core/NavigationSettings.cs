namespace ClashNavigator.Core
{
    // Which coordinate system the numbers in the report are written in. This is decided by the
    // "Coordinates" option used when the NWC was exported out of Revit - Shared is the default
    // and the one used on real coordinated projects.
    public enum ClashCoordinateBasis
    {
        Shared = 0,
        ProjectInternal = 1
    }

    public class NavigationSettings
    {
        public double PaddingMeters { get; set; }
        public ClashCoordinateBasis CoordinateBasis { get; set; }
        public bool SelectElements { get; set; }
        public bool IsolateElements { get; set; }
        public bool ZoomToClash { get; set; }

        public NavigationSettings()
        {
            PaddingMeters = 0.5;
            CoordinateBasis = ClashCoordinateBasis.Shared;
            SelectElements = true;
            IsolateElements = false;
            ZoomToClash = true;
        }
    }

    public class NavigationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        // What the two report objects turned out to be. Carried back so the grid row can show
        // the real Revit names instead of the names Navisworks wrote.
        public ClashResolution Resolution { get; set; }

        public NavigationResult()
        {
            Message = string.Empty;
        }

        public static NavigationResult Failed(string message)
        {
            return new NavigationResult { Success = false, Message = message };
        }
    }
}
