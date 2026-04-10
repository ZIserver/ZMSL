using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ZMSL.App.Converters
{
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? paramStr = parameter as string;
            bool invert = !string.IsNullOrEmpty(paramStr) && paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase);

            if (value is int count)
            {
                bool hasItems = count > 0;
                return (invert ? !hasItems : hasItems) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (value is long longCount)
            {
                bool hasItems = longCount > 0;
                return (invert ? !hasItems : hasItems) ? Visibility.Visible : Visibility.Collapsed;
            }
            return invert ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
