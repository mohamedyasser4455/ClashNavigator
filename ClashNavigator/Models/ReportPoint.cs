namespace ClashNavigator.Models
{
    // A point read from the clash report, already converted to feet.
    // Kept as a plain struct on purpose: the Models folder must stay free of Revit types
    // so the parser can be tested outside Revit.
    public struct ReportPoint
    {
        public double X;
        public double Y;
        public double Z;

        public ReportPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
