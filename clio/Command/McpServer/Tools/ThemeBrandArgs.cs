using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Palette and font inputs shared by the MCP tools that drive the theme CSS engine, declared once so
/// <c>create-theme</c> (brand mode) and <c>build-theme</c> advertise an identical brand contract.
/// <para>
/// <c>primary</c> and <c>version</c> deliberately stay on the concrete argument records rather than joining
/// this base: their MCP attributes differ per tool and attributes live on the declaration, so a shared
/// declaration could only carry one of each. <c>build-theme</c> marks <c>primary</c> <see cref="System.ComponentModel.DataAnnotations.RequiredAttribute"/>
/// (it is the only CSS source there) while <c>create-theme</c> must leave it optional (<c>css-content</c> is
/// the alternative source), and each tool documents a different resolution rule for <c>version</c>. Moving
/// them here would drop build-theme's schema-level requirement and give both tools a description that is
/// wrong for one of them.
/// </para>
/// </summary>
public abstract record ThemeBrandArgs {
	/// <summary>Secondary colour; derived from the primary when omitted.</summary>
	[JsonPropertyName("secondary")]
	[Description("Secondary colour; derived from the primary when omitted.")]
	public string? Secondary { get; init; }

	/// <summary>Accent colour; chosen from the primary when omitted.</summary>
	[JsonPropertyName("accent")]
	[Description("Accent colour; chosen from the primary when omitted.")]
	public string? Accent { get; init; }

	/// <summary>Success colour; the platform default when omitted.</summary>
	[JsonPropertyName("success")]
	[Description("Success colour; the platform default when omitted.")]
	public string? Success { get; init; }

	/// <summary>Error colour; the platform default when omitted.</summary>
	[JsonPropertyName("error")]
	[Description("Error colour; the platform default when omitted.")]
	public string? Error { get; init; }

	/// <summary>Heading font family; Montserrat when omitted.</summary>
	[JsonPropertyName("heading-font")]
	[Description("Heading font family; Montserrat when omitted. The name must start with a letter or digit and contain only letters, digits, spaces and hyphens (max 100 chars); a malformed name fails the build with INVALID_FONT_FAMILY.")]
	public string? HeadingFont { get; init; }

	/// <summary>Body font family; Montserrat when omitted.</summary>
	[JsonPropertyName("body-font")]
	[Description("Body font family; Montserrat when omitted. The name must start with a letter or digit and contain only letters, digits, spaces and hyphens (max 100 chars); a malformed name fails the build with INVALID_FONT_FAMILY.")]
	public string? BodyFont { get; init; }

	/// <summary>Font weights to load; ignored without a custom heading or body font.</summary>
	[JsonPropertyName("font-weights")]
	[Description("Font weights to load (e.g. [400,500,600]); ignored without a custom heading/body font; defaults to 400,500,600.")]
	public int[]? FontWeights { get; init; }
}
