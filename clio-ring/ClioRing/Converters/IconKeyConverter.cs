using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ClioRing.Views;

namespace ClioRing.Converters;

/// <summary>
/// Resolves a string icon-family key (e.g. <c>check</c>, <c>close</c>, <c>restart</c>, <c>dot</c>) to its
/// shared stroke <see cref="Geometry"/> from <see cref="RingIcons"/>, so list-templated pipeline steps can
/// bind their glyph without the view-model referencing any Avalonia visual type.
/// </summary>
public sealed class IconKeyConverter : IValueConverter {
	/// <inheritdoc />
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		RingIcons.Get(value as string);

	/// <inheritdoc />
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
