using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.PageModalSize;

/// <summary>
/// The modal window sizes the Freedom UI Page Designer offers, mapped to the <c>size</c> value it stores in a
/// page's <c>AppearanceSettings</c> add-on (<c>modalSettings.size</c>).
/// </summary>
/// <remarks>
/// The platform enum (<c>SchemaSize</c> in creatio-ui) also has <c>None = 0</c>, <c>Custom = 5</c> and
/// <c>ExtraSmall = 6</c>. The designer never offers them, so a page given one of them would show no selected size
/// in the designer; they are refused on write and reported by their stored value on read.
/// </remarks>
public static class PageModalSizeCatalog {

	private static readonly IReadOnlyList<(string Name, string Caption, int Value)> Sizes = [
		("small", "S", 1),
		("medium", "M", 2),
		("large", "L", 3),
		("extra-large", "XL", 4)
	];

	/// <summary>The stored <c>size</c> the runtime treats as "no size set".</summary>
	public const int NoneValue = 0;

	/// <summary>
	/// The size a modal page (one inheriting <c>BaseMiniPageTemplate</c>) opens with when no size is set: Small
	/// (creatio-ui <c>AppearanceSettingsDefaultLoaderService</c>). Any other page defaults to ExtraLarge.
	/// </summary>
	public const int ModalDefaultValue = 1;

	/// <summary>The accepted size names, in designer order.</summary>
	public static IReadOnlyList<string> Names { get; } = Sizes.Select(size => size.Name).ToList();

	/// <summary>The accepted values as one human-readable list, for error messages and tool descriptions.</summary>
	public static string AcceptedValuesText { get; } =
		string.Join(", ", Sizes.Select(size => $"{size.Name} ({size.Caption})"));

	/// <summary>
	/// Parses a size name (<c>small</c>, <c>medium</c>, <c>large</c>, <c>extra-large</c>) or a designer caption
	/// (<c>S</c>, <c>M</c>, <c>L</c>, <c>XL</c>), case-insensitively.
	/// </summary>
	/// <param name="size">The requested size.</param>
	/// <returns>The stored <c>size</c> value.</returns>
	/// <exception cref="UnsupportedPageModalSizeException">The value is empty or not a size the designer offers.</exception>
	public static int Parse(string size) {
		string normalized = size?.Trim();
		if (string.IsNullOrEmpty(normalized)) {
			throw new UnsupportedPageModalSizeException($"size is required. Use one of: {AcceptedValuesText}.");
		}
		foreach ((string name, string caption, int value) in Sizes) {
			if (string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, caption, StringComparison.OrdinalIgnoreCase)) {
				return value;
			}
		}
		throw new UnsupportedPageModalSizeException(
			$"size '{size}' is not a modal window size the Page Designer supports. Use one of: {AcceptedValuesText}.");
	}

	/// <summary>Returns the size name for a stored value, or <c>null</c> when the designer does not offer it.</summary>
	public static string NameOf(int value) =>
		Sizes.Where(size => size.Value == value).Select(size => size.Name).FirstOrDefault();
}

/// <summary>
/// A modal window size the Page Designer does not offer. Distinct from other argument errors so a caller lists the
/// accepted sizes only when the size itself was the problem.
/// </summary>
public sealed class UnsupportedPageModalSizeException(string message) : ArgumentException(message);
