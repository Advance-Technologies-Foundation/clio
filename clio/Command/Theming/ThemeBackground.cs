using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Theming;

namespace Clio.Command.Theming;

public sealed record ThemeBackground
{
	internal const string ImageMode = "Image";
	internal const string ColorMode = "Color";

	private const int MaxModeLength = 20;

	private const int MaxColorLength = 64;

	[JsonPropertyName("mode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Mode { get; init; }

	[JsonPropertyName("imageId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ImageId { get; init; }

	[JsonPropertyName("color")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Color { get; init; }

	internal static ThemeBackground FromCatalog(ThemeBackground background) {
		if (background is null || string.IsNullOrWhiteSpace(background.Mode)) {
			return null;
		}
		return new ThemeBackground {
			Mode = Clean(background.Mode, MaxModeLength),
			ImageId = Clean(background.ImageId, ThemeParameterValidator.MaxIdLength),
			Color = Clean(background.Color, MaxColorLength)
		};
	}

	private static string Clean(string value, int maxLength) {
		return string.IsNullOrWhiteSpace(value) ? null : TextUtilities.SanitizeForDisplay(value.Trim(), maxLength);
	}

	internal string Describe() {
		return string.IsNullOrWhiteSpace(ImageId)
			? $"{Mode} {Color}".Trim()
			: $"{Mode} {ImageId}";
	}
}
