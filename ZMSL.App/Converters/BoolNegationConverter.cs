using Microsoft.UI.Xaml.Data;
using System;

namespace ZMSL.App.Converters;

public class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolean)
        {
            return !boolean;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolean)
        {
            return !boolean;
        }
        return false;
    }
}
