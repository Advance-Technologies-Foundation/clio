using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Theming;

namespace Clio.Command.Theming;

public sealed record ThemeBackground
{
	internal const string ImageMode = "Image";
	internal const string ColorMode = "Color";

	private const int MaxModeLength = 20;
	private const int MaxColorLength = 20;

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
			Mode = TextUtilities.SanitizeForDisplay(background.Mode.Trim(), MaxModeLength),
			ImageId = string.IsNullOrWhiteSpace(background.ImageId)
				? null
				: TextUtilities.SanitizeForDisplay(background.ImageId.Trim(), ThemeParameterValidator.MaxIdLength),
			Color = string.IsNullOrWhiteSpace(background.Color)
				? null
				: TextUtilities.SanitizeForDisplay(background.Color.Trim(), MaxColorLength)
		};
	}

	internal static string UpdateRequest(string themeId, ThemeBackground background) {
		return JsonSerializer.Serialize(new { id = Guid.Parse(themeId).ToString(), background });
	}

	internal string Describe() {
		return string.IsNullOrWhiteSpace(ImageId)
			? $"{Mode} {Color}".Trim()
			: $"{Mode} {ImageId}";
	}
}
