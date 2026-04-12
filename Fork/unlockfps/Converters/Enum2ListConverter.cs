using System.Globalization;
using Avalonia.Data.Converters;

namespace UnlockFps.Converters;

internal sealed class Enum2ListConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is Type t && t.IsSubclassOf(typeof(Enum)))
            return GetTypeList(t);
        if (value is Enum)
            return GetTypeList(value.GetType());
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static object GetTypeList(Type t)
    {
        var list = Enum.GetValues(t).Cast<Enum>().ToList();
        return list;
    }
}