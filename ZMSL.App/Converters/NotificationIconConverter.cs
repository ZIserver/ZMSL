using Microsoft.UI.Xaml.Data;
using System;

namespace ZMSL.App.Converters
{
    public class NotificationIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string type)
            {
                return type switch
                {
                    "LIKE_POST" => "\uEB51", // Heart
                    "LIKE_COMMENT" => "\uEB51",
                    "REPLY_POST" => "\uE8BD", // Message
                    "REPLY_COMMENT" => "\uE8BD",
                    "SYSTEM" => "\uE7F3", // Info
                    _ => "\uE7F3"
                };
            }
            return "\uE7F3";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
