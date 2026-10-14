using System;

namespace ClashNavigator.Core
{
    // Revit works internally in decimal feet. Navisworks writes the report in whatever unit
    // the NWF was set to, and tells us which one in the <exchange units="..."> attribute.
    //
    // The conversion is done by hand instead of UnitUtils because the Units API changed shape
    // between Revit 2020 and 2021 (DisplayUnitType -> UnitTypeId). Hard-coded factors keep the
    // same source file compiling for every version we support.
    public static class UnitConverter
    {
        public const double MetersPerFoot = 0.3048;

        public static double ToFeet(double value, string navisworksUnit)
        {
            return value * FeetFactor(navisworksUnit);
        }

        public static double FeetToMeters(double feet)
        {
            return feet * MetersPerFoot;
        }

        public static double MetersToFeet(double meters)
        {
            return meters / MetersPerFoot;
        }

        // How many feet one unit of the report is worth.
        public static double FeetFactor(string navisworksUnit)
        {
            string unit = (navisworksUnit ?? string.Empty).Trim().ToLowerInvariant();

            switch (unit)
            {
                case "ft":
                case "feet":
                case "foot":
                    return 1.0;

                case "in":
                case "inch":
                case "inches":
                    return 1.0 / 12.0;

                case "yd":
                case "yard":
                case "yards":
                    return 3.0;

                case "mi":
                case "mile":
                case "miles":
                    return 5280.0;

                case "mm":
                case "millimeter":
                case "millimeters":
                case "millimetre":
                case "millimetres":
                    return 0.001 / MetersPerFoot;

                case "cm":
                case "centimeter":
                case "centimeters":
                case "centimetre":
                case "centimetres":
                    return 0.01 / MetersPerFoot;

                case "km":
                case "kilometer":
                case "kilometers":
                    return 1000.0 / MetersPerFoot;

                case "m":
                case "meter":
                case "meters":
                case "metre":
                case "metres":
                    return 1.0 / MetersPerFoot;

                default:
                    // Navisworks defaults to metres, and so do we.
                    return 1.0 / MetersPerFoot;
            }
        }

        public static double ParseDouble(string text, double fallback)
        {
            double result;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            // Some locales export with a comma decimal separator.
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.CurrentCulture, out result))
            {
                return result;
            }

            return fallback;
        }
    }
}
