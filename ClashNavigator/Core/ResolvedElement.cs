using Autodesk.Revit.DB;

namespace ClashNavigator.Core
{
    // A clash object from the report after we found the real Revit element behind it.
    public class ResolvedElement
    {
        public Element Element { get; set; }

        // The document the element actually lives in - the host model, or a linked model.
        public Document OwnerDocument { get; set; }

        // Null when the element is in the host model.
        public RevitLinkInstance LinkInstance { get; set; }

        // Link placement. Identity for host elements. Needed to move geometry into host coordinates.
        public Transform LinkTransform { get; set; }

        public string ModelName { get; set; }

        // How we found it, shown in the status bar so the user can trust (or question) the result.
        public string MatchNote { get; set; }

        public bool IsFromLink
        {
            get { return LinkInstance != null; }
        }

        public ResolvedElement()
        {
            LinkTransform = Transform.Identity;
            ModelName = string.Empty;
            MatchNote = string.Empty;
        }

        // The element id as a plain number, for the grid and for copying into
        // Revit's Select by ID box.
        public long ElementIdValue
        {
            get { return Element == null ? -1 : RevitCompat.GetIdValue(Element.Id); }
        }

        public string CategoryName
        {
            get { return (Element != null && Element.Category != null) ? Element.Category.Name : "Element"; }
        }

        public string TypeName
        {
            get { return SafeName(Element); }
        }

        // What the grid shows: category and family type only. The model name is deliberately
        // left out here - it is long, it repeats on every row, and it lives in its own column.
        public string ShortDescribe()
        {
            if (Element == null) return string.Empty;

            string text = CategoryName + " - " + TypeName;
            return IsFromLink ? text + "  (link)" : text;
        }

        // The long form, used in the status bar and in the tooltip.
        public string Describe()
        {
            if (Element == null) return "not found";

            string where = IsFromLink ? "link: " + ModelName : "host model";
            return CategoryName + " - " + TypeName + " (" + where + ")";
        }

        // The element's bounding box already moved into host model coordinates.
        public BoundingBoxXYZ GetHostBoundingBox()
        {
            if (Element == null) return null;

            BoundingBoxXYZ box = Element.get_BoundingBox(null);
            if (box == null) return null;

            return GeometryHelper.ToCoordinateSystem(box, LinkTransform);
        }

        // Element.Name throws for a few element kinds, and one bad element must never
        // take down a whole resolve-all run.
        private static string SafeName(Element element)
        {
            if (element == null) return string.Empty;
            try { return element.Name; }
            catch { return string.Empty; }
        }
    }
}
