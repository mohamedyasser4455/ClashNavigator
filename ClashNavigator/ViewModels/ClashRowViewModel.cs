using System.Text;
using ClashNavigator.Core;
using ClashNavigator.Models;

namespace ClashNavigator.ViewModels
{
    // One row of the grid.
    //
    // Each side of a clash is kept as separate facts - id, category, family type, model - rather
    // than one long string, because the grid, the tooltip, the CSV and the copied detail block all
    // want them arranged differently. Before the row is resolved they fall back to whatever the
    // Navisworks report said.
    public class ClashRowViewModel : ViewModelBase
    {
        private const string NotResolved = "-";
        private const string NotFound = "not found in this model";

        // Everything we know about one side of the clash.
        private class Side
        {
            public bool Found;
            public long Id = -1;
            public string Category = string.Empty;
            public string Type = string.Empty;
            public string Model = string.Empty;
            public bool FromLink;
        }

        private readonly Side _a = new Side();
        private readonly Side _b = new Side();

        private string _state = string.Empty;
        private bool _resolved;

        public ClashRowViewModel(ClashItem clash)
        {
            Clash = clash;
        }

        public ClashItem Clash { get; private set; }

        public int Index { get { return Clash.Index; } }
        public string TestName { get { return Clash.TestName; } }
        public string GroupName { get { return Clash.GroupName; } }
        public string Name { get { return Clash.Name; } }
        public string ClashType { get { return Clash.ClashType; } }
        public string GridLocation { get { return Clash.GridLocation; } }

        public string Distance
        {
            get { return Clash.DistanceMeters.ToString("0.000"); }
        }

        // Editable in the grid: the coordinator marks a clash Reviewed or Approved as they work
        // through the list, and the exported report carries those statuses.
        public string Status
        {
            get { return Clash.Status; }
            set
            {
                if (value == null || Clash.Status == value) return;

                Clash.Status = value;
                IsStatusEdited = true;

                OnPropertyChanged("Status");
                OnPropertyChanged("IsStatusEdited");
            }
        }

        public bool IsStatusEdited { get; private set; }

        // Who has to deal with this clash. Comes from the report's own Assigned To field when
        // Navisworks exported one, and is edited here from then on. Free text on purpose - one
        // project says "MEP contractor", another says "Ahmed".
        public string AssignedTo
        {
            get { return Clash.AssignedTo; }
            set
            {
                string trimmed = (value ?? string.Empty).Trim();
                if (Clash.AssignedTo == trimmed) return;

                Clash.AssignedTo = trimmed;

                OnPropertyChanged("AssignedTo");
                OnPropertyChanged("IsAssignmentEdited");
            }
        }

        public bool IsAssignmentEdited
        {
            get
            {
                return !string.Equals(Clash.AssignedTo ?? string.Empty,
                                      Clash.OriginalAssignedTo ?? string.Empty,
                                      System.StringComparison.OrdinalIgnoreCase);
            }
        }

        // ---------- what the grid shows ----------

        public string IdA { get { return FormatId(_a, Clash.ObjectA); } }
        public string IdB { get { return FormatId(_b, Clash.ObjectB); } }

        public string ElementA { get { return Describe(_a, Clash.ObjectA); } }
        public string ElementB { get { return Describe(_b, Clash.ObjectB); } }

        public string ModelA { get { return _a.Found ? _a.Model : Clash.ObjectA.SourceFile; } }
        public string ModelB { get { return _b.Found ? _b.Model : Clash.ObjectB.SourceFile; } }

        public string ElementATip { get { return BuildTip(_a, Clash.ObjectA); } }
        public string ElementBTip { get { return BuildTip(_b, Clash.ObjectB); } }

        // What makes two clashes "the same problem". A report of 272 clashes is rarely 272
        // separate decisions: the same slab type against the same hanger type repeats dozens of
        // times, and that is one systemic issue to solve once, not dozens.
        public string DuplicateKey
        {
            get { return ElementA + "   ×   " + ElementB; }
        }

        public string State
        {
            get { return _state; }
            set { SetProperty(ref _state, value); }
        }

        public bool IsResolved
        {
            get { return _resolved; }
        }

        public void ApplyResolution(ClashResolution resolution)
        {
            if (resolution == null) return;

            _resolved = true;
            Fill(_a, resolution.A);
            Fill(_b, resolution.B);

            State = resolution.State;

            OnPropertyChanged("IdA");
            OnPropertyChanged("IdB");
            OnPropertyChanged("ElementA");
            OnPropertyChanged("ElementB");
            OnPropertyChanged("ModelA");
            OnPropertyChanged("ModelB");
            OnPropertyChanged("ElementATip");
            OnPropertyChanged("ElementBTip");
            OnPropertyChanged("DuplicateKey");
            OnPropertyChanged("IsResolved");
        }

        private static void Fill(Side side, ResolvedElement element)
        {
            if (element == null)
            {
                // Not found in this model - keep showing what the report said rather than
                // blanking the row, so no information is lost.
                side.Found = false;
                side.Id = -1;
                side.Category = string.Empty;
                side.Type = string.Empty;
                side.Model = string.Empty;
                side.FromLink = false;
                return;
            }

            side.Found = true;
            side.Id = element.ElementIdValue;
            side.Category = element.CategoryName;
            side.Type = element.TypeName;
            side.Model = element.ModelName;
            side.FromLink = element.IsFromLink;
        }

        // ---------- the block the user copies and sends ----------

        // A plain text summary of everything somebody else needs to act on this clash: which
        // report and test it came from, how deep the overlap is, where it is on the grid, and
        // for each side the category, the family type, the element id and the model it lives in.
        //
        // Plain text on purpose - it pastes intact into an email, a chat message or a cell.
        public string BuildDetailBlock(string reportFileName)
        {
            var text = new StringBuilder();

            text.AppendLine("CLASH INFO");
            text.AppendLine(Field("Test:", TestName));
            text.AppendLine(Field("Clash:", Name));
            text.AppendLine(Field("Status:", Status));
            text.AppendLine(Field("Action by:", AssignedTo));
            text.AppendLine(Field("Type:", ClashType));
            text.AppendLine(Field("Distance:", Distance + " m"));

            if (!string.IsNullOrEmpty(GridLocation))
                text.AppendLine(Field("Grid / Level:", GridLocation));

            text.AppendLine(Field("Clash point:", FormatPoint()));

            if (!string.IsNullOrEmpty(reportFileName))
                text.AppendLine(Field("Source:", reportFileName));

            text.AppendLine();
            AppendElement(text, "ELEMENT A", _a, Clash.ObjectA);
            text.AppendLine();
            AppendElement(text, "ELEMENT B", _b, Clash.ObjectB);

            return text.ToString();
        }

        private static void AppendElement(StringBuilder text, string heading, Side side, ClashObjectInfo info)
        {
            text.AppendLine(heading);

            if (!side.Found)
            {
                text.AppendLine(Field("Category:", NotFound));
                text.AppendLine(Field("Report says:", info.Display));
                return;
            }

            text.AppendLine(Field("Category:", side.Category));
            text.AppendLine(Field("Type:", side.Type));
            text.AppendLine(Field("ID:", side.Id.ToString()));
            text.AppendLine(Field("Model:", side.Model + (side.FromLink ? "   (link)" : "   (host)")));
        }

        // The clash point as Navisworks reported it - same numbers the coordinator sees there,
        // so the two can be compared directly.
        private string FormatPoint()
        {
            if (!Clash.HasPoint) return "not in the report";

            double x = UnitConverter.FeetToMeters(Clash.Point.X);
            double y = UnitConverter.FeetToMeters(Clash.Point.Y);
            double z = UnitConverter.FeetToMeters(Clash.Point.Z);

            return x.ToString("0.000") + ", " + y.ToString("0.000") + ", " + z.ToString("0.000")
                 + "   (shared coordinates, m)";
        }

        private static string Field(string label, string value)
        {
            return label.PadRight(14) + (string.IsNullOrEmpty(value) ? "-" : value);
        }

        // ---------- small helpers ----------

        private static string FormatId(Side side, ClashObjectInfo info)
        {
            if (side.Found && side.Id > 0) return side.Id.ToString();
            if (info.HasElementId) return info.ElementId.ToString();   // the id the report carried
            return NotResolved;
        }

        private static string Describe(Side side, ClashObjectInfo info)
        {
            if (!side.Found) return ReportName(info);

            string text = side.Category + " - " + side.Type;
            return side.FromLink ? text + "  (link)" : text;
        }

        private static string ReportName(ClashObjectInfo info)
        {
            if (!string.IsNullOrEmpty(info.ItemName)) return info.ItemName;
            if (!string.IsNullOrEmpty(info.ItemType)) return info.ItemType;
            return "Unknown";
        }

        private static string BuildTip(Side side, ClashObjectInfo info)
        {
            if (!side.Found)
                return NotFound + "\nReport says: " + info.Display;

            return "Revit: " + side.Category + " - " + side.Type
                 + "\nID: " + side.Id
                 + "\nModel: " + side.Model + (side.FromLink ? " (link)" : " (host)")
                 + "\nReport: " + info.Display;
        }

        // The search box looks at the report text and the Revit text, so typing a family type
        // finds the clash whether or not the row has been resolved yet.
        public bool Matches(string search)
        {
            if (string.IsNullOrEmpty(search)) return true;

            string needle = search.Trim().ToLowerInvariant();

            return Contains(Name, needle)
                || Contains(TestName, needle)
                || Contains(GroupName, needle)
                || Contains(GridLocation, needle)
                || Contains(Status, needle)
                || Contains(AssignedTo, needle)
                || Contains(IdA, needle)
                || Contains(IdB, needle)
                || Contains(ElementA, needle)
                || Contains(ElementB, needle)
                || Contains(ModelA, needle)
                || Contains(ModelB, needle)
                || Contains(Clash.ObjectA.Display, needle)
                || Contains(Clash.ObjectB.Display, needle);
        }

        private static bool Contains(string text, string needle)
        {
            return !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(needle);
        }
    }
}
