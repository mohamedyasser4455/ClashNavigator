using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClashNavigator.Views
{
    // Paints the Status cell the way Navisworks paints its own clash list, so the table is read
    // by colour instead of word by word.
    //
    // The same converter gives back either the fill or the text colour for a status, chosen by
    // ConverterParameter ("fill" or "text"), because every fill needs a readable partner:
    // white on the dark reds and greens, near-black on the light blue and the yellow.
    public class ClashTextBrushConverter : IValueConverter
    {
        private static readonly Brush NoFill = Brushes.Transparent;
        private static readonly Brush DarkInk = Frozen("#1E2A30");
        private static readonly Brush LightInk = Frozen("#FFFFFF");

        private class StatusColour
        {
            public Brush Fill;
            public Brush Text;
        }

        private static readonly Dictionary<string, StatusColour> Palette =
            new Dictionary<string, StatusColour>(StringComparer.OrdinalIgnoreCase)
            {
                { "New",      Pair("#E8140C", LightInk) },   // red         - nobody has looked at it yet
                { "Active",   Pair("#F5A623", LightInk) },   // orange      - being worked on
                { "Reviewed", Pair("#ADD8E6", DarkInk)  },   // light blue  - checked
                { "Approved", Pair("#008000", LightInk) },   // green       - accepted
                { "Resolved", Pair("#FFF100", DarkInk)  }    // yellow      - closed
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool wantsFill = string.Equals(parameter as string, "fill", StringComparison.OrdinalIgnoreCase);

            string key = value as string;
            StatusColour colour;

            if (string.IsNullOrEmpty(key) || !Palette.TryGetValue(key.Trim(), out colour))
                return wantsFill ? NoFill : DarkInk;

            return wantsFill ? colour.Fill : colour.Text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static StatusColour Pair(string fillHex, Brush text)
        {
            return new StatusColour { Fill = Frozen(fillHex), Text = text };
        }

        // Frozen brushes are created once and shared by every row instead of per cell.
        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
