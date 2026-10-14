using Autodesk.Revit.DB;

namespace ClashNavigator.Core
{
    // Every place where the Revit API itself changed between 2022 and 2026 lives here,
    // so the rest of the code reads the same in all five builds.
    public static class RevitCompat
    {
        // Revit 2024 replaced ElementId.IntegerValue (int) with ElementId.Value (long).
        public static long GetIdValue(ElementId id)
        {
            if (id == null) return -1;
#if R2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId ToElementId(long value)
        {
#if R2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        // Selecting an element that lives inside a link needs Selection.SetReferences,
        // which only arrived in Revit 2023. On 2022 we can still section-box and zoom to it,
        // we just cannot leave it highlighted.
        public static bool CanSelectLinkedElements
        {
            get
            {
#if R2023_OR_GREATER
                return true;
#else
                return false;
#endif
            }
        }

        public static string SupportedRevitVersion
        {
            get
            {
#if REVIT2022
                return "2022";
#elif REVIT2023
                return "2023";
#elif REVIT2024
                return "2024";
#elif REVIT2025
                return "2025";
#elif REVIT2026
                return "2026";
#else
                return "unknown";
#endif
            }
        }
    }
}
