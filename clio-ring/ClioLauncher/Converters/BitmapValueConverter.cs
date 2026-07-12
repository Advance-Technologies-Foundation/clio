using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClioLauncher.Converters;

public class BitmapValueConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is string path && !string.IsNullOrEmpty(path))
		{
			try
			{
				return new Bitmap(path);
			}
			catch
			{
				return null;
			}
		}
		return null;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
