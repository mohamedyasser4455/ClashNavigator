using System;
using Autodesk.Revit.DB;

namespace ClashNavigator.Core
{
    // Bounding box maths. The only reason this file exists is that a bounding box coming out of
    // a linked model is expressed in the LINK's coordinates - dropping it into the host model
    // untouched puts the section box in the wrong place, usually far away from the clash.
    public static class GeometryHelper
    {
        // Move a box into another coordinate system. A box is axis-aligned, so we cannot just
        // transform Min and Max: we transform all eight corners and rebuild the box around them.
        public static BoundingBoxXYZ ToCoordinateSystem(BoundingBoxXYZ box, Transform transform)
        {
            if (box == null) return null;

            Transform total = transform ?? Transform.Identity;
            if (box.Transform != null && !box.Transform.IsIdentity)
                total = total.Multiply(box.Transform);

            XYZ min = box.Min;
            XYZ max = box.Max;

            var corners = new[]
            {
                new XYZ(min.X, min.Y, min.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(min.X, min.Y, max.Z),
                new XYZ(max.X, max.Y, min.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, max.Y, max.Z)
            };

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (XYZ corner in corners)
            {
                XYZ moved = total.OfPoint(corner);
                if (moved.X < minX) minX = moved.X;
                if (moved.Y < minY) minY = moved.Y;
                if (moved.Z < minZ) minZ = moved.Z;
                if (moved.X > maxX) maxX = moved.X;
                if (moved.Y > maxY) maxY = moved.Y;
                if (moved.Z > maxZ) maxZ = moved.Z;
            }

            var result = new BoundingBoxXYZ();
            result.Transform = Transform.Identity;
            result.Min = new XYZ(minX, minY, minZ);
            result.Max = new XYZ(maxX, maxY, maxZ);
            return result;
        }

        // Where the two boxes overlap - which for two elements that clash is the clash itself.
        // This is what makes the section box tight: a 30 m wall and a duct share only the small
        // pocket where the duct passes through, and that pocket is all the coordinator wants to see.
        // Returns null when the boxes do not overlap, which happens on a clearance clash where the
        // elements come close but never touch.
        public static BoundingBoxXYZ Intersection(BoundingBoxXYZ first, BoundingBoxXYZ second)
        {
            if (first == null || second == null) return null;

            double minX = Math.Max(first.Min.X, second.Min.X);
            double minY = Math.Max(first.Min.Y, second.Min.Y);
            double minZ = Math.Max(first.Min.Z, second.Min.Z);

            double maxX = Math.Min(first.Max.X, second.Max.X);
            double maxY = Math.Min(first.Max.Y, second.Max.Y);
            double maxZ = Math.Min(first.Max.Z, second.Max.Z);

            if (minX > maxX || minY > maxY || minZ > maxZ) return null;

            var result = new BoundingBoxXYZ();
            result.Transform = Transform.Identity;
            result.Min = new XYZ(minX, minY, minZ);
            result.Max = new XYZ(maxX, maxY, maxZ);
            return result;
        }

        // A small cube centred on a point. Used when there is no overlap to measure but the
        // report still told us where the clash is.
        public static BoundingBoxXYZ Around(XYZ point, double halfSizeFeet)
        {
            if (point == null) return null;

            double half = Math.Max(halfSizeFeet, 0.1);

            var result = new BoundingBoxXYZ();
            result.Transform = Transform.Identity;
            result.Min = new XYZ(point.X - half, point.Y - half, point.Z - half);
            result.Max = new XYZ(point.X + half, point.Y + half, point.Z + half);
            return result;
        }

        // Safety net: never let the section box grow past a sensible size, however big the two
        // elements are. The box is shrunk towards the anchor - the clash point when we have one -
        // so what stays in view is the part that matters.
        public static BoundingBoxXYZ LimitSize(BoundingBoxXYZ box, XYZ anchor, double maxHalfSizeFeet)
        {
            if (box == null) return null;

            XYZ centre = anchor ?? Center(box);

            double minX = Math.Max(box.Min.X, centre.X - maxHalfSizeFeet);
            double minY = Math.Max(box.Min.Y, centre.Y - maxHalfSizeFeet);
            double minZ = Math.Max(box.Min.Z, centre.Z - maxHalfSizeFeet);

            double maxX = Math.Min(box.Max.X, centre.X + maxHalfSizeFeet);
            double maxY = Math.Min(box.Max.Y, centre.Y + maxHalfSizeFeet);
            double maxZ = Math.Min(box.Max.Z, centre.Z + maxHalfSizeFeet);

            // If the anchor sits outside the box the clamp can invert an axis - fall back to the
            // original box rather than hand back something impossible.
            if (minX > maxX || minY > maxY || minZ > maxZ) return box;

            var result = new BoundingBoxXYZ();
            result.Transform = Transform.Identity;
            result.Min = new XYZ(minX, minY, minZ);
            result.Max = new XYZ(maxX, maxY, maxZ);
            return result;
        }

        public static BoundingBoxXYZ Union(BoundingBoxXYZ first, BoundingBoxXYZ second)
        {
            if (first == null) return second;
            if (second == null) return first;

            var result = new BoundingBoxXYZ();
            result.Transform = Transform.Identity;
            result.Min = new XYZ(
                Math.Min(first.Min.X, second.Min.X),
                Math.Min(first.Min.Y, second.Min.Y),
                Math.Min(first.Min.Z, second.Min.Z));
            result.Max = new XYZ(
                Math.Max(first.Max.X, second.Max.X),
                Math.Max(first.Max.Y, second.Max.Y),
                Math.Max(first.Max.Z, second.Max.Z));
            return result;
        }

        // The section box has to be a little larger than the two elements, otherwise the cut
        // runs exactly through their faces and you cannot see what is hitting what.
        public static BoundingBoxXYZ Inflate(BoundingBoxXYZ box, double paddingFeet)
        {
            if (box == null) return null;

            double pad = Math.Max(paddingFeet, 0.1);

            var result = new BoundingBoxXYZ();
            result.Transform = Transform.Identity;
            result.Min = new XYZ(box.Min.X - pad, box.Min.Y - pad, box.Min.Z - pad);
            result.Max = new XYZ(box.Max.X + pad, box.Max.Y + pad, box.Max.Z + pad);
            return result;
        }

        // Zero when the point is inside the box. Used to decide which document an ambiguous
        // element id really belongs to.
        public static double DistanceToPoint(BoundingBoxXYZ box, XYZ point)
        {
            if (box == null || point == null) return double.MaxValue;

            double dx = Math.Max(Math.Max(box.Min.X - point.X, 0.0), point.X - box.Max.X);
            double dy = Math.Max(Math.Max(box.Min.Y - point.Y, 0.0), point.Y - box.Max.Y);
            double dz = Math.Max(Math.Max(box.Min.Z - point.Z, 0.0), point.Z - box.Max.Z);

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static XYZ Center(BoundingBoxXYZ box)
        {
            if (box == null) return XYZ.Zero;
            return (box.Min + box.Max) * 0.5;
        }

        public static Outline ToOutline(XYZ point, double halfSizeFeet)
        {
            XYZ offset = new XYZ(halfSizeFeet, halfSizeFeet, halfSizeFeet);
            return new Outline(point - offset, point + offset);
        }

        public static double Diagonal(BoundingBoxXYZ box)
        {
            if (box == null) return 0.0;
            return box.Max.DistanceTo(box.Min);
        }
    }
}
