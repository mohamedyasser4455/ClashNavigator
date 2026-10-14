using System;
using System.Collections.Generic;

namespace ClashNavigator.Models
{
    // One side of a clash, exactly as Navisworks wrote it in the XML report.
    // Nothing here is resolved against Revit yet - that happens later in ElementResolver.
    public class ClashObjectInfo
    {
        // -1 means the report did not carry a Revit element id for this object.
        public long ElementId { get; set; }

        // Name of the model this object came from (Project-ARC.nwc, Project-STR.rvt ...).
        // This is what tells us whether the element lives in the host model or in a link.
        public string SourceFile { get; set; }

        public string ItemName { get; set; }
        public string ItemType { get; set; }
        public string Layer { get; set; }
        public string Path { get; set; }

        // Every name/value pair we found for this object. Navisworks writes different tags
        // depending on version and on what the user ticked in the report settings, so we keep
        // everything and let the Diagnostics button show it when a report does not map cleanly.
        public Dictionary<string, string> Attributes { get; private set; }

        public ClashObjectInfo()
        {
            ElementId = -1;
            SourceFile = string.Empty;
            ItemName = string.Empty;
            ItemType = string.Empty;
            Layer = string.Empty;
            Path = string.Empty;
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasElementId
        {
            get { return ElementId > 0; }
        }

        // What the user sees in the grid: "Basic Wall [408214] - Project-ARC.nwc"
        public string Display
        {
            get
            {
                string name = string.IsNullOrEmpty(ItemName) ? ItemType : ItemName;
                if (string.IsNullOrEmpty(name)) name = "Unknown";

                string id = HasElementId ? " [" + ElementId + "]" : " [no id]";
                string file = string.IsNullOrEmpty(SourceFile) ? string.Empty : " - " + SourceFile;

                return name + id + file;
            }
        }
    }
}
