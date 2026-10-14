using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ClashNavigator.Models;

namespace ClashNavigator.Core
{
    // Reads the XML report that Navisworks Clash Detective exports.
    //
    // The tag names below are stable, but WHAT ends up inside <clashobject> depends on the
    // Navisworks version and on the boxes the coordinator ticked in Report > Contents. So the
    // parser never looks for one fixed tag: it harvests every name/value pair it can find for
    // each object into a dictionary, then asks that dictionary for the two things we actually
    // need - the Revit element id and the model the element came from.
    //
    // Rough shape of the file:
    //   exchange (units="m")
    //     batchtest
    //       clashtests
    //         clashtest (name, test_type)
    //           clashresults
    //             clashgroup (optional)
    //               clashresult (name, status, distance, description)
    //                 clashpoint > pos3f (x, y, z)
    //                 clashobjects
    //                   clashobject   <- side A
    //                   clashobject   <- side B
    public class ClashReportParser
    {
        // Unit written in the <exchange units="..."> attribute. Defaults to metres.
        public string ReportUnits { get; private set; }

        // The .nwf / .nwd the report was produced from, when the file says so.
        public string ReportSourceModel { get; private set; }

        public ClashReportParser()
        {
            ReportUnits = "m";
            ReportSourceModel = string.Empty;
        }

        public List<ClashItem> Parse(string xmlPath)
        {
            if (!File.Exists(xmlPath))
                throw new FileNotFoundException("Clash report not found.", xmlPath);

            XDocument doc = XDocument.Load(xmlPath, LoadOptions.None);
            return ParseDocument(doc);
        }

        public List<ClashItem> ParseXml(string xmlContent)
        {
            return ParseDocument(XDocument.Parse(xmlContent));
        }

        private List<ClashItem> ParseDocument(XDocument doc)
        {
            var clashes = new List<ClashItem>();
            if (doc.Root == null) return clashes;

            ReportUnits = Attr(doc.Root, "units");
            if (string.IsNullOrEmpty(ReportUnits)) ReportUnits = "m";

            ReportSourceModel = Attr(doc.Root, "filename");

            double toFeet = UnitConverter.FeetFactor(ReportUnits);
            int index = 0;

            foreach (XElement test in Descendants(doc.Root, "clashtest"))
            {
                string testName = Attr(test, "name");
                if (string.IsNullOrEmpty(testName)) testName = "Clash Test";

                // Descendants covers both flat results and results nested inside <clashgroup>.
                foreach (XElement result in Descendants(test, "clashresult"))
                {
                    index++;
                    clashes.Add(ReadClashResult(result, testName, index, toFeet));
                }
            }

            return clashes;
        }

        private ClashItem ReadClashResult(XElement result, string testName, int index, double toFeet)
        {
            var clash = new ClashItem();
            clash.Index = index;
            clash.TestName = testName;
            clash.Name = Attr(result, "name");
            if (string.IsNullOrEmpty(clash.Name)) clash.Name = "Clash" + index;

            clash.GroupName = FindParentGroupName(result);

            clash.Status = Attr(result, "status");
            if (string.IsNullOrEmpty(clash.Status))
                clash.Status = ChildText(result, "resultstatus");
            clash.Status = NormalizeStatus(clash.Status);
            clash.OriginalStatus = clash.Status;

            clash.AssignedTo = ReadAssignedTo(result);
            clash.OriginalAssignedTo = clash.AssignedTo;

            clash.ClashType = Attr(result, "description");
            if (string.IsNullOrEmpty(clash.ClashType)) clash.ClashType = "Hard";

            // Navisworks reports the overlap as a negative number; the coordinator reads it as a depth.
            double distance = UnitConverter.ParseDouble(Attr(result, "distance"), 0.0);
            clash.DistanceMeters = UnitConverter.FeetToMeters(distance * toFeet);

            clash.GridLocation = ChildText(result, "gridlocation");

            XElement pos = Descendants(result, "pos3f").FirstOrDefault();
            if (pos != null)
            {
                clash.Point = new ReportPoint(
                    UnitConverter.ParseDouble(Attr(pos, "x"), 0.0) * toFeet,
                    UnitConverter.ParseDouble(Attr(pos, "y"), 0.0) * toFeet,
                    UnitConverter.ParseDouble(Attr(pos, "z"), 0.0) * toFeet);
                clash.HasPoint = true;
            }

            List<XElement> objects = Descendants(result, "clashobject").ToList();
            if (objects.Count > 0) clash.ObjectA = ReadClashObject(objects[0]);
            if (objects.Count > 1) clash.ObjectB = ReadClashObject(objects[1]);

            return clash;
        }

        private ClashObjectInfo ReadClashObject(XElement objectElement)
        {
            var info = new ClashObjectInfo();

            // 1. attributes written straight on the tag
            foreach (XAttribute attribute in objectElement.Attributes())
                Put(info.Attributes, attribute.Name.LocalName, attribute.Value);

            // 2. <objectattribute><name>..</name><value>..</value></objectattribute>
            foreach (XElement pair in Descendants(objectElement, "objectattribute"))
                Put(info.Attributes, ChildText(pair, "name"), ChildText(pair, "value"));

            // 3. <smarttags><smarttag><name>..</name><value>..</value></smarttag></smarttags>
            foreach (XElement tag in Descendants(objectElement, "smarttag"))
                Put(info.Attributes, ChildText(tag, "name"), ChildText(tag, "value"));

            // 4. loose single-value tags Navisworks sometimes writes directly
            Put(info.Attributes, "Layer", ChildText(objectElement, "layer"));
            Put(info.Attributes, "Item Name", ChildText(objectElement, "objectname"));

            // 5. the model tree path, if the report carries readable node names
            string path = ReadPathLink(objectElement);
            Put(info.Attributes, "Path", path);

            info.Path = Value(info.Attributes, "Path");
            info.ElementId = ReadElementId(info.Attributes);
            info.SourceFile = ReadSourceFile(info.Attributes);
            info.ItemName = FirstOf(info.Attributes, "Item Name", "Name", "Entity Name", "Family Name", "Type Name");
            info.ItemType = FirstOf(info.Attributes, "Item Type", "Type", "Category", "Revit Category", "Entity Type");
            info.Layer = FirstOf(info.Attributes, "Layer", "Level", "Revit Level");

            if (string.IsNullOrEmpty(info.ItemName) && !string.IsNullOrEmpty(info.Path))
                info.ItemName = LastPathSegment(info.Path);

            return info;
        }

        // ---------- the two lookups that matter ----------

        // The Revit element id. Navisworks exposes it as an "Element ID" property on every
        // item that came from an NWC exported out of Revit, but the exact spelling moves around,
        // so match on meaning rather than on one fixed key.
        private long ReadElementId(Dictionary<string, string> attributes)
        {
            string[] exact =
            {
                "Element ID", "ElementId", "Element Id", "ElementID",
                "Revit Element ID", "Revit ID", "RevitId"
            };

            foreach (string key in exact)
            {
                long id = ParseId(Value(attributes, key));
                if (id > 0) return id;
            }

            // anything whose key mentions both "element" and "id"
            foreach (var entry in attributes)
            {
                string key = entry.Key.ToLowerInvariant();
                if (key.Contains("element") && key.Contains("id"))
                {
                    long id = ParseId(entry.Value);
                    if (id > 0) return id;
                }
            }

            // last resort - a bare "Id" column
            foreach (string key in new[] { "Id", "Item ID", "ItemId" })
            {
                long id = ParseId(Value(attributes, key));
                if (id > 0) return id;
            }

            return -1;
        }

        // Which model the element belongs to. Without this an id like 408214 is ambiguous:
        // the host model and every link can each own an element with that number.
        private string ReadSourceFile(Dictionary<string, string> attributes)
        {
            string[] exact =
            {
                "Source File", "Source File Name", "SourceFile", "File", "File Name",
                "Filename", "Document", "Model", "Model File", "Document Name"
            };

            foreach (string key in exact)
            {
                string value = Value(attributes, key);
                if (!string.IsNullOrEmpty(value)) return CleanFileName(value);
            }

            foreach (var entry in attributes)
            {
                string key = entry.Key.ToLowerInvariant();
                if ((key.Contains("source") || key.Contains("file")) && LooksLikeModelFile(entry.Value))
                    return CleanFileName(entry.Value);
            }

            // Fall back to the tree path. It usually starts with the federated container
            // ("Coordination.nwd > Project-MEP.nwc > Level 2 > ...") and the container is the
            // one name that tells us nothing, so look for a real model first and only settle
            // for the container if the path has nothing else.
            string path = Value(attributes, "Path");
            if (!string.IsNullOrEmpty(path))
            {
                string container = string.Empty;

                foreach (string token in SplitPath(path))
                {
                    if (!LooksLikeModelFile(token)) continue;

                    if (IsContainerFile(token))
                    {
                        if (string.IsNullOrEmpty(container)) container = token;
                        continue;
                    }

                    return CleanFileName(token);
                }

                if (!string.IsNullOrEmpty(container)) return CleanFileName(container);
            }

            return string.Empty;
        }

        // .nwd / .nwf are the federated file everything was appended into, not a discipline model.
        private static bool IsContainerFile(string text)
        {
            string lower = (text ?? string.Empty).Trim().ToLowerInvariant();
            return lower.EndsWith(".nwd") || lower.EndsWith(".nwf");
        }

        // Clash Detective has its own "Assigned To" field, exported when the coordinator ticks it
        // in Report > Contents. Navisworks has written it under a few different shapes over the
        // years, so try each one and fall back to empty - the user can always type it in.
        private static string ReadAssignedTo(XElement result)
        {
            foreach (string tag in new[] { "assignedto", "assigned_to", "assignto" })
            {
                XElement element = Descendants(result, tag).FirstOrDefault();
                if (element == null) continue;

                // Newer exports put the name in an attribute, older ones in the element text.
                string value = Attr(element, "to");
                if (string.IsNullOrEmpty(value)) value = Attr(element, "name");
                if (string.IsNullOrEmpty(value)) value = (element.Value ?? string.Empty).Trim();

                if (!string.IsNullOrEmpty(value)) return value;
            }

            return Attr(result, "assignedto");
        }

        // ---------- small helpers ----------

        private static string ReadPathLink(XElement objectElement)
        {
            XElement pathLink = Descendants(objectElement, "pathlink").FirstOrDefault();
            if (pathLink == null) return string.Empty;

            List<string> nodes = Descendants(pathLink, "node")
                .Select(n => (n.Value ?? string.Empty).Trim())
                .Where(v => v.Length > 0)
                .ToList();

            if (nodes.Count == 0) return string.Empty;

            // Some exports write numeric tree indices instead of names - useless to us.
            bool allNumeric = nodes.All(n => n.All(char.IsDigit));
            if (allNumeric) return string.Empty;

            return string.Join(" > ", nodes.ToArray());
        }

        private static long ParseId(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;

            var digits = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsDigit(c)) digits.Append(c);
                else if (digits.Length > 0) break;   // stop at the first break after a run of digits
            }

            if (digits.Length == 0) return -1;

            long value;
            if (long.TryParse(digits.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
                return value;

            return -1;
        }

        private static bool LooksLikeModelFile(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.Trim().ToLowerInvariant();
            return lower.EndsWith(".rvt") || lower.EndsWith(".nwc") || lower.EndsWith(".nwd")
                || lower.EndsWith(".nwf") || lower.EndsWith(".ifc") || lower.EndsWith(".dwg");
        }

        private static string CleanFileName(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string value = text.Trim();

            int slash = value.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0 && slash < value.Length - 1) value = value.Substring(slash + 1);

            return value;
        }

        private static IEnumerable<string> SplitPath(string path)
        {
            return path.Split(new[] { '>', '\\', '/', '|' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 0);
        }

        private static string LastPathSegment(string path)
        {
            List<string> parts = SplitPath(path).ToList();
            return parts.Count == 0 ? string.Empty : parts[parts.Count - 1];
        }

        private static string NormalizeStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return "New";
            string trimmed = status.Trim();
            return char.ToUpperInvariant(trimmed[0]) + (trimmed.Length > 1 ? trimmed.Substring(1).ToLowerInvariant() : string.Empty);
        }

        private static string FindParentGroupName(XElement result)
        {
            XElement parent = result.Parent;
            while (parent != null)
            {
                if (string.Equals(parent.Name.LocalName, "clashgroup", StringComparison.OrdinalIgnoreCase))
                    return Attr(parent, "name");
                parent = parent.Parent;
            }
            return string.Empty;
        }

        private static void Put(Dictionary<string, string> target, string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(value)) return;

            key = key.Trim();
            if (key.Length == 0) return;

            if (!target.ContainsKey(key)) target[key] = value.Trim();
        }

        private static string Value(Dictionary<string, string> source, string key)
        {
            string value;
            return source.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static string FirstOf(Dictionary<string, string> source, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = Value(source, key);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }

        // Navisworks does not use an XML namespace today, but matching on the local name
        // means a future namespaced export will not silently return zero clashes.
        private static IEnumerable<XElement> Descendants(XElement parent, string localName)
        {
            return parent.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        }

        private static string Attr(XElement element, string name)
        {
            if (element == null) return string.Empty;
            XAttribute attribute = element.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attribute == null ? string.Empty : attribute.Value.Trim();
        }

        private static string ChildText(XElement parent, string localName)
        {
            if (parent == null) return string.Empty;
            XElement child = parent.Elements()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
            return child == null ? string.Empty : (child.Value ?? string.Empty).Trim();
        }
    }
}
