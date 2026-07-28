using System.Text.Json.Serialization;

namespace Clio.Command.Theming;

/// <summary>
/// Common field contract for a Creatio theme write (create/update), serialized to the native
/// <c>ThemeService</c> request body. Command-specific request records derive from this and add their own fields.
/// </summary>
internal record ThemeRequest
{
	[JsonPropertyName("id")]
	public string Id { get; init; }

	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	[JsonPropertyName("cssClassName")]
	public string CssClassName { get; init; }

	[JsonPropertyName("cssContent")]
	public string CssContent { get; init; }
}
