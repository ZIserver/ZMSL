using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace ZMSL.App.Converters;

public class BoolToBrushConverter : DependencyObject, IValueConverter
{
    public Brush TrueBrush
    {
        get { return (Brush)GetValue(TrueBrushProperty); }
        set { SetValue(TrueBrushProperty, value); }
    }

    public static readonly DependencyProperty TrueBrushProperty =
        DependencyProperty.Register("TrueBrush", typeof(Brush), typeof(BoolToBrushConverter), new PropertyMetadata(new SolidColorBrush(Colors.Green)));

    public Brush FalseBrush
    {
        get { return (Brush)GetValue(FalseBrushProperty); }
        set { SetValue(FalseBrushProperty, value); }
    }

    public static readonly DependencyProperty FalseBrushProperty =
        DependencyProperty.Register("FalseBrush", typeof(Brush), typeof(BoolToBrushConverter), new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return TrueBrush;
        }
        return FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
