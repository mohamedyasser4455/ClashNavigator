using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClashNavigator.Models;

namespace ClashNavigator.Core
{
    // Everything the add-in does to the Revit model lives here: find the two elements, put a
    // section box around them, select them, isolate them, and undo all of that again.
    // Every method assumes it is already running inside a valid Revit API context.
    public class ClashNavigationService
    {
        private const string SectionBoxTransactionName = "Clash Navigator - show clash";
        private const string ResetTransactionName = "Clash Navigator - reset view";
        private const string CreateViewTransactionName = "Clash Navigator - create 3D view";

        // The tool works in its own view and nothing else. Borrowing whatever 3D view happened
        // to be open meant leaving a section box behind in someone's working view.
        private const string NavigatorViewName = "Clash View";

        private readonly UIDocument _uiDocument;
        private readonly Document _document;
        private readonly ElementResolver _resolver;

        public ClashNavigationService(UIDocument uiDocument)
        {
            _uiDocument = uiDocument;
            _document = uiDocument.Document;
            _resolver = new ElementResolver(_document);
        }

        public ElementResolver Resolver
        {
            get { return _resolver; }
        }

        // Look the two report objects up in the model without touching the view.
        // Used on its own by "Resolve all", and as the first step of ShowClash.
        public ClashResolution Resolve(ClashItem clash, NavigationSettings settings, bool allowGeometricSearch)
        {
            var resolution = new ClashResolution();
            if (clash == null) return resolution;

            XYZ clashPoint = ToInternalPoint(clash, settings.CoordinateBasis);

            resolution.ClashPoint = clashPoint;
            resolution.A = _resolver.Resolve(clash.ObjectA, clashPoint, clash.HasPoint, allowGeometricSearch);
            resolution.B = _resolver.Resolve(clash.ObjectB, clashPoint, clash.HasPoint, allowGeometricSearch);

            return resolution;
        }

        public NavigationResult ShowClash(ClashItem clash, NavigationSettings settings)
        {
            if (clash == null) return NavigationResult.Failed("No clash selected.");

            ClashResolution resolution = Resolve(clash, settings, true);

            XYZ clashPoint = resolution.ClashPoint;
            ResolvedElement a = resolution.A;
            ResolvedElement b = resolution.B;

            var found = new List<ResolvedElement>();
            if (a != null) found.Add(a);
            if (b != null) found.Add(b);

            if (found.Count == 0)
            {
                NavigationResult failure = NavigationResult.Failed(
                    "Neither element of this clash exists in the open model or its loaded links. " +
                    "Open the model the report was made from, or reload the links.");
                failure.Resolution = resolution;
                return failure;
            }

            BoundingBoxXYZ box = BuildFocusBox(a, b, clashPoint, clash.HasPoint);

            if (box == null)
            {
                NavigationResult noGeometry = NavigationResult.Failed(
                    "The elements were found but they have no geometry to frame.");
                noGeometry.Resolution = resolution;
                return noGeometry;
            }

            double paddingFeet = UnitConverter.MetersToFeet(settings.PaddingMeters);
            BoundingBoxXYZ sectionBox = GeometryHelper.Inflate(box, paddingFeet);
            sectionBox = GeometryHelper.LimitSize(sectionBox, clash.HasPoint ? clashPoint : null,
                                                  UnitConverter.MetersToFeet(MaxHalfSizeMeters));

            View3D view = EnsureThreeDView();
            if (view == null)
            {
                NavigationResult noView = NavigationResult.Failed(
                    "No 3D view is available and one could not be created.");
                noView.Resolution = resolution;
                return noView;
            }

            // Activating a view is not allowed inside a transaction, so it happens first.
            if (_uiDocument.ActiveView == null || _uiDocument.ActiveView.Id != view.Id)
                _uiDocument.ActiveView = view;

            using (var transaction = new Transaction(_document, SectionBoxTransactionName))
            {
                transaction.Start();

                ApplyIsolation(view, found, settings.IsolateElements);

                view.IsSectionBoxActive = true;
                view.SetSectionBox(sectionBox);

                transaction.Commit();
            }

            _uiDocument.RefreshActiveView();

            if (settings.SelectElements) SelectElements(found);
            if (settings.ZoomToClash) ZoomTo(view, sectionBox);

            return new NavigationResult
            {
                Success = true,
                Resolution = resolution,
                Message = BuildMessage(a, b, settings)
            };
        }

        public void ResetView()
        {
            var view = _uiDocument.ActiveView as View3D;
            if (view == null) return;

            using (var transaction = new Transaction(_document, ResetTransactionName))
            {
                transaction.Start();

                if (view.IsTemporaryHideIsolateActive())
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                view.IsSectionBoxActive = false;

                transaction.Commit();
            }

            _uiDocument.Selection.SetElementIds(new List<ElementId>());
            _uiDocument.RefreshActiveView();
        }

        // Never show more than this much around the clash, whatever the elements measure.
        private const double MaxHalfSizeMeters = 3.0;

        // How big a window to open when there is nothing to measure but we know where the clash is.
        private const double PointBoxHalfSizeMeters = 0.4;

        // ---------- pieces ----------

        // Deciding what the section box should frame.
        //
        // The obvious answer - the two elements' bounding boxes merged - is wrong in practice.
        // A wall or a slab is tens of metres long, so merging its box with a duct's produces a
        // section box covering half the floor, and the clash is a dot somewhere inside it.
        //
        // What the coordinator actually wants to see is the small pocket where the two elements
        // overlap, so that is what we look for first:
        //
        //   1. the intersection of the two boxes - the clash itself, and always small
        //   2. a small cube on the clash point - for a clearance clash, where the elements
        //      never actually touch so there is no intersection to find
        //   3. the merged boxes - last resort, when only one element was found and there is
        //      no clash point either
        private BoundingBoxXYZ BuildFocusBox(ResolvedElement a, ResolvedElement b, XYZ clashPoint, bool hasPoint)
        {
            BoundingBoxXYZ boxA = a == null ? null : a.GetHostBoundingBox();
            BoundingBoxXYZ boxB = b == null ? null : b.GetHostBoundingBox();

            BoundingBoxXYZ overlap = GeometryHelper.Intersection(boxA, boxB);
            if (overlap != null) return overlap;

            if (hasPoint)
            {
                BoundingBoxXYZ atPoint =
                    GeometryHelper.Around(clashPoint, UnitConverter.MetersToFeet(PointBoxHalfSizeMeters));

                if (atPoint != null) return atPoint;
            }

            return GeometryHelper.Union(boxA, boxB);
        }

        // The report stores the clash point in Navisworks coordinates. When the NWC was exported
        // with "Shared" coordinates - the normal setting on a coordinated project - that point is
        // in shared coordinates and has to be pushed back into Revit's internal system before it
        // means anything. GetTotalTransform() maps shared -> internal; its inverse is what you use
        // to read Easting/Northing off an element.
        private XYZ ToInternalPoint(ClashItem clash, ClashCoordinateBasis basis)
        {
            if (!clash.HasPoint) return XYZ.Zero;

            var raw = new XYZ(clash.Point.X, clash.Point.Y, clash.Point.Z);

            if (basis == ClashCoordinateBasis.ProjectInternal) return raw;

            ProjectLocation location = _document.ActiveProjectLocation;
            if (location == null) return raw;

            return location.GetTotalTransform().OfPoint(raw);
        }

        private void ApplyIsolation(View3D view, List<ResolvedElement> found, bool isolate)
        {
            if (!isolate)
            {
                if (view.IsTemporaryHideIsolateActive())
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                return;
            }

            // Revit can only temporarily isolate elements of the host model. An element inside a
            // link cannot be isolated on its own - the closest Revit allows is isolating the whole
            // link instance, so that is what we do, and the section box keeps the result tight.
            var ids = new List<ElementId>();

            foreach (ResolvedElement element in found)
            {
                if (element.IsFromLink) ids.Add(element.LinkInstance.Id);
                else ids.Add(element.Element.Id);
            }

            if (ids.Count > 0) view.IsolateElementsTemporary(ids);
        }

        private void SelectElements(List<ResolvedElement> found)
        {
            try
            {
#if R2023_OR_GREATER
                // Revit 2023 and up can hold host elements and linked elements in one selection.
                var references = new List<Reference>();

                foreach (ResolvedElement element in found)
                {
                    var reference = new Reference(element.Element);
                    if (element.IsFromLink) reference = reference.CreateLinkReference(element.LinkInstance);
                    references.Add(reference);
                }

                if (references.Count > 0) _uiDocument.Selection.SetReferences(references);
#else
                // Revit 2022 cannot select inside a link, so only the host side gets highlighted.
                var ids = found.Where(e => !e.IsFromLink).Select(e => e.Element.Id).ToList();
                _uiDocument.Selection.SetElementIds(ids);
#endif
            }
            catch
            {
                // A failed selection must never stop the section box from being useful.
            }
        }

        private void ZoomTo(View3D view, BoundingBoxXYZ box)
        {
            try
            {
                foreach (UIView uiView in _uiDocument.GetOpenUIViews())
                {
                    if (uiView.ViewId != view.Id) continue;
                    uiView.ZoomAndCenterRectangle(box.Min, box.Max);
                    return;
                }
            }
            catch
            {
                // Zoom is a convenience, not a requirement.
            }
        }

        // Always the tool's own "Clash View": reuse it if it is already there, otherwise make it.
        // The user's own 3D views are never modified.
        private View3D EnsureThreeDView()
        {
            var active = _uiDocument.ActiveView as View3D;
            if (IsUsable(active) && IsNavigatorView(active)) return active;

            View3D existing = new FilteredElementCollector(_document)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(IsUsable)
                .FirstOrDefault(IsNavigatorView);

            if (existing != null) return existing;

            return CreateThreeDView();
        }

        private static bool IsNavigatorView(View3D view)
        {
            return view != null && !view.IsPerspective
                && view.Name != null
                && view.Name.StartsWith(NavigatorViewName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsable(View3D view)
        {
            return view != null && !view.IsTemplate && !view.IsLocked;
        }

        private View3D CreateThreeDView()
        {
            ViewFamilyType viewType = new FilteredElementCollector(_document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewType == null) return null;

            View3D created = null;

            using (var transaction = new Transaction(_document, CreateViewTransactionName))
            {
                transaction.Start();

                created = View3D.CreateIsometric(_document, viewType.Id);
                if (created != null) NameTheView(created);

                transaction.Commit();
            }

            return created;
        }

        // Revit refuses a view name that is already used anywhere in the project, so try a few
        // suffixes rather than leaving the view called "3D View 1".
        private static void NameTheView(View3D view)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string name = attempt == 0 ? NavigatorViewName : NavigatorViewName + " " + attempt;

                try
                {
                    view.Name = name;
                    return;
                }
                catch
                {
                    // name taken - try the next one
                }
            }
        }

        private string BuildMessage(ResolvedElement a, ResolvedElement b, NavigationSettings settings)
        {
            string left = a == null ? "Element A not found" : "A: " + a.Describe();
            string right = b == null ? "Element B not found" : "B: " + b.Describe();

            string note = string.Empty;

            bool anyLink = (a != null && a.IsFromLink) || (b != null && b.IsFromLink);

            if (anyLink && settings.IsolateElements)
                note = "   |   a linked element can only be isolated together with its whole link";
            else if (anyLink && settings.SelectElements && !RevitCompat.CanSelectLinkedElements)
                note = "   |   Revit 2022 cannot select inside a link - the section box still frames it";

            return left + "   |   " + right + note;
        }
    }
}
