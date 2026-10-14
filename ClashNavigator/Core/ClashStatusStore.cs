using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ClashNavigator.Models;

namespace ClashNavigator.Core
{
    // Keeps the review decisions the coordinator makes - the status and who the clash is
    // assigned to - between sessions.
    //
    // The Navisworks XML is read-only as far as we are concerned - it is an export, and the next
    // export would overwrite anything we wrote into it. So the statuses live in a small companion
    // file saved next to the report: ClashReport.xml -> ClashReport.xml.status.xml
    //
    // Only clashes whose status was actually changed are written, which keeps the file small and
    // means a fresh export from Navisworks still supplies the baseline.
    //
    // Written with XDocument, the same way the report is read - no external library, nothing extra
    // to ship in the installer.
    public static class ClashStatusStore
    {
        private const string Extension = ".status.xml";

        public static string PathFor(string reportPath)
        {
            return string.IsNullOrEmpty(reportPath) ? null : reportPath + Extension;
        }

        // Returns how many changes were written. Never throws: losing the statuses is annoying,
        // crashing the add-in in the middle of a review is worse.
        public static int Save(string reportPath, IEnumerable<ClashItem> items)
        {
            string path = PathFor(reportPath);
            if (path == null || items == null) return 0;

            try
            {
                List<ClashItem> changed = items.Where(IsChanged).ToList();

                if (changed.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return 0;
                }

                var root = new XElement("clashNavigatorStatus",
                    new XAttribute("report", System.IO.Path.GetFileName(reportPath)),
                    new XAttribute("saved", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

                foreach (ClashItem clash in changed)
                {
                    root.Add(new XElement("clash",
                        new XAttribute("test", clash.TestName ?? string.Empty),
                        new XAttribute("name", clash.Name ?? string.Empty),
                        new XAttribute("idA", clash.ObjectA.ElementId),
                        new XAttribute("idB", clash.ObjectB.ElementId),
                        new XAttribute("status", clash.Status ?? string.Empty),
                        new XAttribute("assignedTo", clash.AssignedTo ?? string.Empty)));
                }

                new XDocument(root).Save(path);
                return changed.Count;
            }
            catch
            {
                return 0;
            }
        }

        // Applies any saved statuses onto a freshly parsed report. Returns how many were restored.
        public static int Load(string reportPath, IList<ClashItem> items)
        {
            string path = PathFor(reportPath);
            if (path == null || items == null || !File.Exists(path)) return 0;

            try
            {
                XDocument document = XDocument.Load(path);
                if (document.Root == null) return 0;

                var byExactKey = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
                var byPairKey = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);

                foreach (XElement saved in document.Root.Elements("clash"))
                {
                    var decision = new Decision
                    {
                        Status = Attr(saved, "status"),
                        AssignedTo = Attr(saved, "assignedTo")
                    };

                    if (decision.IsEmpty) continue;

                    string test = Attr(saved, "test");
                    string name = Attr(saved, "name");
                    string idA = Attr(saved, "idA");
                    string idB = Attr(saved, "idB");

                    byExactKey[Join(test, name, idA, idB)] = decision;

                    // Second chance key: if Navisworks renumbered the clashes on the next export,
                    // the same pair of elements in the same test is still the same problem.
                    byPairKey[Join(test, idA, idB)] = decision;
                }

                int restored = 0;

                foreach (ClashItem clash in items)
                {
                    Decision decision;

                    if (!byExactKey.TryGetValue(ExactKey(clash), out decision) &&
                        !byPairKey.TryGetValue(PairKey(clash), out decision))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(decision.Status)) clash.Status = decision.Status;
                    if (!string.IsNullOrEmpty(decision.AssignedTo)) clash.AssignedTo = decision.AssignedTo;

                    restored++;
                }

                return restored;
            }
            catch
            {
                return 0;
            }
        }

        // What one saved line carries.
        private class Decision
        {
            public string Status;
            public string AssignedTo;

            public bool IsEmpty
            {
                get { return string.IsNullOrEmpty(Status) && string.IsNullOrEmpty(AssignedTo); }
            }
        }

        // A clash is worth saving once the coordinator has touched either field.
        private static bool IsChanged(ClashItem clash)
        {
            if (clash == null) return false;

            bool statusChanged = !string.Equals(clash.Status ?? string.Empty,
                                                clash.OriginalStatus ?? string.Empty,
                                                StringComparison.OrdinalIgnoreCase);

            bool assigneeChanged = !string.Equals(clash.AssignedTo ?? string.Empty,
                                                  clash.OriginalAssignedTo ?? string.Empty,
                                                  StringComparison.OrdinalIgnoreCase);

            return statusChanged || assigneeChanged;
        }

        private static string ExactKey(ClashItem clash)
        {
            return Join(clash.TestName, clash.Name,
                        clash.ObjectA.ElementId.ToString(), clash.ObjectB.ElementId.ToString());
        }

        private static string PairKey(ClashItem clash)
        {
            return Join(clash.TestName, clash.ObjectA.ElementId.ToString(), clash.ObjectB.ElementId.ToString());
        }

        private static string Join(params string[] parts)
        {
            return string.Join("|", parts.Select(p => (p ?? string.Empty).Trim()).ToArray());
        }

        private static string Attr(XElement element, string name)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value;
        }
    }
}
