using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ClashNavigator.Models;

namespace ClashNavigator.Core
{
    // Turns a clash object from the report into a real Revit element.
    //
    // This is the hard part of the whole add-in. A Navisworks element id is only unique inside
    // the model it came from, so id 408214 can exist in the host model AND in every link, each
    // time pointing at something different. The resolver therefore works in three steps:
    //
    //   1. collect every document that owns an element with that id (host + loaded links)
    //   2. if more than one owns it, pick the right one using the source file name from the report
    //   3. if the report did not say which file, use the clash point - the correct element is the
    //      one whose geometry actually sits where Navisworks said the clash happened
    //
    // And if the report carries no element ids at all (the coordinator forgot to tick them in
    // Report > Contents), fall back to searching by the clash point and the item name.
    public class ElementResolver
    {
        private class LinkedModel
        {
            public RevitLinkInstance Instance;
            public Document Document;
            public Transform Transform;
            public List<string> Names;
            public string DisplayName;
        }

        private readonly Document _hostDocument;
        private readonly List<string> _hostNames;
        private readonly List<LinkedModel> _links = new List<LinkedModel>();

        public ElementResolver(Document hostDocument)
        {
            _hostDocument = hostDocument;
            _hostNames = BuildNames(hostDocument.Title, hostDocument.PathName, null);

            var instances = new FilteredElementCollector(hostDocument)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (RevitLinkInstance instance in instances)
            {
                Document linkDocument = instance.GetLinkDocument();
                if (linkDocument == null) continue;   // link is unloaded - nothing to resolve into

                var link = new LinkedModel();
                link.Instance = instance;
                link.Document = linkDocument;
                link.Transform = instance.GetTotalTransform();
                link.DisplayName = linkDocument.Title;
                link.Names = BuildNames(linkDocument.Title, linkDocument.PathName, instance.Name);

                _links.Add(link);
            }
        }

        public int LinkCount
        {
            get { return _links.Count; }
        }

        public IEnumerable<string> GetLoadedModelNames()
        {
            yield return _hostDocument.Title + " (host)";
            foreach (LinkedModel link in _links) yield return link.DisplayName + " (link)";
        }

        // hostPoint is the clash point already converted into host model internal coordinates.
        //
        // allowGeometricSearch turns on the slow last-resort search that scans the model around
        // the clash point. It is worth it for the one clash the user just clicked, but not when
        // resolving hundreds of rows at once, so the bulk path switches it off.
        public ResolvedElement Resolve(ClashObjectInfo info, XYZ hostPoint, bool hasPoint, bool allowGeometricSearch)
        {
            if (info == null) return null;

            if (info.HasElementId)
            {
                List<ResolvedElement> candidates = FindById(info.ElementId);

                if (candidates.Count == 1)
                {
                    candidates[0].MatchNote = "matched by element id";
                    return candidates[0];
                }

                if (candidates.Count > 1)
                {
                    ResolvedElement byName = PickByModelName(candidates, info.SourceFile);
                    if (byName != null)
                    {
                        byName.MatchNote = "matched by element id + source file";
                        return byName;
                    }

                    if (hasPoint)
                    {
                        ResolvedElement byPoint = PickNearest(candidates, hostPoint);
                        if (byPoint != null)
                        {
                            byPoint.MatchNote = "matched by element id + clash point";
                            return byPoint;
                        }
                    }

                    candidates[0].MatchNote = "element id found in more than one model - first match used";
                    return candidates[0];
                }
            }

            // No id, or the id does not exist in any loaded model.
            if (hasPoint && allowGeometricSearch)
            {
                ResolvedElement geometric = FindByPoint(info, hostPoint);
                if (geometric != null) return geometric;
            }

            return null;
        }

        // ---------- step 1: who owns this id ----------

        private List<ResolvedElement> FindById(long id)
        {
            var result = new List<ResolvedElement>();
            ElementId elementId = RevitCompat.ToElementId(id);

            Element hostElement = GetUsableElement(_hostDocument, elementId);
            if (hostElement != null)
            {
                result.Add(new ResolvedElement
                {
                    Element = hostElement,
                    OwnerDocument = _hostDocument,
                    LinkInstance = null,
                    LinkTransform = Transform.Identity,
                    ModelName = _hostDocument.Title
                });
            }

            foreach (LinkedModel link in _links)
            {
                Element linkElement = GetUsableElement(link.Document, elementId);
                if (linkElement == null) continue;

                result.Add(new ResolvedElement
                {
                    Element = linkElement,
                    OwnerDocument = link.Document,
                    LinkInstance = link.Instance,
                    LinkTransform = link.Transform,
                    ModelName = link.DisplayName
                });
            }

            return result;
        }

        private static Element GetUsableElement(Document document, ElementId id)
        {
            Element element;
            try
            {
                element = document.GetElement(id);
            }
            catch
            {
                return null;
            }

            if (element == null) return null;
            if (element is ElementType) return null;      // a type, not something that can clash
            if (element.Category == null) return null;    // no category means nothing to show

            return element;
        }

        // ---------- step 2: which model does the report mean ----------

        private ResolvedElement PickByModelName(List<ResolvedElement> candidates, string sourceFile)
        {
            if (string.IsNullOrEmpty(sourceFile)) return null;

            string wanted = Normalize(sourceFile);
            if (wanted.Length == 0) return null;

            var matches = new List<ResolvedElement>();

            foreach (ResolvedElement candidate in candidates)
            {
                List<string> names = candidate.IsFromLink
                    ? FindLink(candidate.LinkInstance).Names
                    : _hostNames;

                if (names.Any(name => NamesMatch(name, wanted))) matches.Add(candidate);
            }

            return matches.Count == 1 ? matches[0] : null;
        }

        private LinkedModel FindLink(RevitLinkInstance instance)
        {
            return _links.FirstOrDefault(l => l.Instance.Id == instance.Id);
        }

        private static bool NamesMatch(string candidate, string wanted)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(wanted)) return false;
            if (candidate == wanted) return true;

            // "Project-ARC.nwc" in the report against "Project-ARC.rvt" in Revit, or a model
            // that got a suffix on export ("Project-ARC-2026.nwc").
            return candidate.Contains(wanted) || wanted.Contains(candidate);
        }

        // ---------- step 3: which one is actually at the clash point ----------

        private ResolvedElement PickNearest(List<ResolvedElement> candidates, XYZ hostPoint)
        {
            ResolvedElement best = null;
            double bestDistance = double.MaxValue;

            foreach (ResolvedElement candidate in candidates)
            {
                BoundingBoxXYZ box = candidate.GetHostBoundingBox();
                if (box == null) continue;

                double distance = GeometryHelper.DistanceToPoint(box, hostPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            // More than a couple of metres away means we are guessing, not matching.
            return bestDistance < 10.0 ? best : null;
        }

        // ---------- fallback: no usable id in the report ----------

        private ResolvedElement FindByPoint(ClashObjectInfo info, XYZ hostPoint)
        {
            double[] searchRadii = { 0.5, 1.5, 4.0 };   // feet

            foreach (double radius in searchRadii)
            {
                ResolvedElement best = null;
                double bestScore = double.MinValue;

                Consider(_hostDocument, null, Transform.Identity, _hostDocument.Title,
                         hostPoint, radius, info, ref best, ref bestScore);

                foreach (LinkedModel link in _links)
                {
                    XYZ localPoint = link.Transform.Inverse.OfPoint(hostPoint);
                    Consider(link.Document, link.Instance, link.Transform, link.DisplayName,
                             localPoint, radius, info, ref best, ref bestScore);
                }

                if (best != null)
                {
                    best.MatchNote = "no element id in the report - matched by clash point";
                    return best;
                }
            }

            return null;
        }

        private void Consider(Document document, RevitLinkInstance instance, Transform transform,
                              string modelName, XYZ localPoint, double radius, ClashObjectInfo info,
                              ref ResolvedElement best, ref double bestScore)
        {
            Outline outline = GeometryHelper.ToOutline(localPoint, radius);

            IEnumerable<Element> nearby;
            try
            {
                nearby = new FilteredElementCollector(document)
                    .WhereElementIsNotElementType()
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .ToElements();
            }
            catch
            {
                return;
            }

            foreach (Element element in nearby)
            {
                if (element.Category == null) continue;

                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null) continue;

                double distance = GeometryHelper.DistanceToPoint(box, localPoint);
                double score = -distance;

                if (!string.IsNullOrEmpty(info.ItemName) && Similar(element.Name, info.ItemName))
                    score += 100.0;

                if (!string.IsNullOrEmpty(info.ItemType) && Similar(element.Category.Name, info.ItemType))
                    score += 50.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new ResolvedElement
                    {
                        Element = element,
                        OwnerDocument = document,
                        LinkInstance = instance,
                        LinkTransform = transform,
                        ModelName = modelName
                    };
                }
            }
        }

        private static bool Similar(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            string a = left.Trim().ToLowerInvariant();
            string b = right.Trim().ToLowerInvariant();
            return a == b || a.Contains(b) || b.Contains(a);
        }

        // ---------- names ----------

        private static List<string> BuildNames(string title, string path, string instanceName)
        {
            var names = new List<string>();
            AddName(names, title);
            AddName(names, SafeFileName(path));
            AddName(names, instanceName);
            return names;
        }

        private static void AddName(List<string> names, string value)
        {
            string normalized = Normalize(value);
            if (normalized.Length > 0 && !names.Contains(normalized)) names.Add(normalized);
        }

        private static string SafeFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            // Fully qualified so it can never be mistaken for a Revit type of the same name.
            try { return System.IO.Path.GetFileName(path); }
            catch { return string.Empty; }
        }

        // Strip the extension, the link instance suffix and the case, so "Project-ARC.nwc",
        // "Project-ARC.rvt" and "Project-ARC.rvt : 1" all collapse to "project-arc".
        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            string text = value.Trim();

            int colon = text.LastIndexOf(" : ", StringComparison.Ordinal);
            if (colon > 0) text = text.Substring(0, colon);

            int slash = text.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0 && slash < text.Length - 1) text = text.Substring(slash + 1);

            int dot = text.LastIndexOf('.');
            if (dot > 0) text = text.Substring(0, dot);

            return text.Trim().ToLowerInvariant();
        }
    }
}
