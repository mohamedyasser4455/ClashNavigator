namespace ClashNavigator.Models
{
    // One clash from the Navisworks report: the two objects that hit each other plus
    // the numbers the coordinator cares about.
    public class ClashItem
    {
        public int Index { get; set; }

        public string TestName { get; set; }
        public string GroupName { get; set; }
        public string Name { get; set; }

        // new / active / reviewed / approved / resolved
        public string Status { get; set; }

        // Who has to deal with this clash - a discipline, a contractor or a person.
        // Navisworks Clash Detective has the same field, so it is read from the report when it
        // is there and the coordinator edits it from then on.
        public string AssignedTo { get; set; }

        // What the report itself said, kept so we can tell an edited value from an original one
        // and only save the differences.
        public string OriginalStatus { get; set; }
        public string OriginalAssignedTo { get; set; }

        // Hard / Clearance / Duplicates
        public string ClashType { get; set; }

        // Negative in Navisworks (how deep they overlap). Stored in metres for display.
        public double DistanceMeters { get; set; }

        public string GridLocation { get; set; }

        // Clash point in feet, still in the coordinate system Navisworks used.
        // Converting it into Revit internal coordinates is ClashNavigationService's job.
        public ReportPoint Point { get; set; }
        public bool HasPoint { get; set; }

        public ClashObjectInfo ObjectA { get; set; }
        public ClashObjectInfo ObjectB { get; set; }

        public ClashItem()
        {
            TestName = string.Empty;
            GroupName = string.Empty;
            Name = string.Empty;
            Status = string.Empty;
            AssignedTo = string.Empty;
            OriginalStatus = string.Empty;
            OriginalAssignedTo = string.Empty;
            ClashType = string.Empty;
            GridLocation = string.Empty;
            ObjectA = new ClashObjectInfo();
            ObjectB = new ClashObjectInfo();
        }
    }
}
