using Autodesk.Revit.DB;

namespace ClashNavigator.Core
{
    // The two report objects after we looked them up in Revit.
    // Kept separate from the navigation itself so the list can be resolved in bulk
    // without touching the view.
    public class ClashResolution
    {
        public ResolvedElement A { get; set; }
        public ResolvedElement B { get; set; }

        // Clash point converted into host model internal coordinates.
        public XYZ ClashPoint { get; set; }

        public bool FoundBoth
        {
            get { return A != null && B != null; }
        }

        public bool FoundNone
        {
            get { return A == null && B == null; }
        }

        public string State
        {
            get
            {
                if (FoundBoth) return "Both found";
                if (FoundNone) return "Not found";
                return "One side only";
            }
        }

        // Null when that side was not found - the caller then keeps showing the name
        // the Navisworks report used, so no information is lost.
        public string DescribeA
        {
            get { return A == null ? null : A.Describe(); }
        }

        public string DescribeB
        {
            get { return B == null ? null : B.Describe(); }
        }
    }
}
