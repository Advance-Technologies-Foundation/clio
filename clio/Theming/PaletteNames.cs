namespace Clio.Theming;

/// <summary>
/// The canonical names of the five generated theme palettes, shared by the palette generator,
/// the CSS builder, and the text-token resolver so the names cannot drift apart.
/// </summary>
internal static class PaletteNames {

	/// <summary>The primary brand palette.</summary>
	internal const string Primary = "primary";

	/// <summary>The secondary brand palette.</summary>
	internal const string Secondary = "secondary";

	/// <summary>The accent palette.</summary>
	internal const string Accent = "accent";

	/// <summary>The success-state palette.</summary>
	internal const string Success = "success";

	/// <summary>The error-state palette.</summary>
	internal const string Error = "error";
}
