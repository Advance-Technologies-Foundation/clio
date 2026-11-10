using System;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Theming;

namespace Clio.Command.Theming;

public sealed record ThemeBackground
{
	internal const string ImageMode = "Image";
	internal const string ColorMode = "Color";
	internal const string RemovalValue = "null";

	private const string ImageIdArgument = "background-image-id";
	private const string ColorArgument = "background-color";

	private const int MaxModeLength = 20;

	private const int MaxColorLength = 64;

	private const int MaxEchoLength = 64;

	private static readonly Regex HexColorPattern = new("^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	[JsonPropertyName("mode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Mode { get; init; }

	[JsonPropertyName("imageId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ImageId { get; init; }

	[JsonPropertyName("color")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Color { get; init; }

	internal bool RemovesBackground => Mode is null;

	internal static bool IsGiven(string imageId, string color) {
		return !string.IsNullOrWhiteSpace(imageId) || !string.IsNullOrWhiteSpace(color);
	}

	internal static bool TryParse(string imageId, string color, bool allowRemoval, out ThemeBackground background,
		out string error) {
		background = null;
		error = null;
		bool hasImageId = !string.IsNullOrWhiteSpace(imageId);
		if (hasImageId && !string.IsNullOrWhiteSpace(color)) {
			error = $"{ImageIdArgument} and {ColorArgument} cannot be combined: pass at most one background argument.";
			return false;
		}
		if (!IsGiven(imageId, color)) {
			return true;
		}
		(string argument, string given, string kind, string expected) = hasImageId
			? (ImageIdArgument, imageId, "a valid id", "the id printed by upload-image")
			: (ColorArgument, color, "a hex color", "#RGB, #RRGGBB or #RRGGBBAA");
		string value = given.Trim();
		if (string.Equals(value, RemovalValue, StringComparison.OrdinalIgnoreCase)) {
			if (allowRemoval) {
				background = new ThemeBackground();
				return true;
			}
			error = $"{argument} {RemovalValue} is not accepted: a new theme has no background to remove.";
			return false;
		}
		background = Parse(hasImageId, value);
		if (background is null) {
			error = $"{argument} '{TextUtilities.SanitizeForDisplay(given, MaxEchoLength)}' is not {kind}. Pass {expected}.";
		}
		return background is not null;
	}

	internal ThemeBackground SanitizeForDisplay() {
		if (string.IsNullOrWhiteSpace(Mode)) {
			return null;
		}
		return new ThemeBackground {
			Mode = Clean(Mode, MaxModeLength),
			ImageId = Clean(ImageId, ThemeParameterValidator.MaxIdLength),
			Color = Clean(Color, MaxColorLength)
		};
	}

	internal string Describe() {
		return string.IsNullOrWhiteSpace(ImageId)
			? $"{Mode} {Color}".Trim()
			: $"{Mode} {ImageId}";
	}

	private static ThemeBackground Parse(bool isImage, string value) {
		if (isImage) {
			return Guid.TryParse(value, out Guid id) && id != Guid.Empty
				? new ThemeBackground { Mode = ImageMode, ImageId = id.ToString() }
				: null;
		}
		return HexColorPattern.IsMatch(value) ? new ThemeBackground { Mode = ColorMode, Color = value } : null;
	}

	private static string Clean(string value, int maxLength) {
		return string.IsNullOrWhiteSpace(value) ? null : TextUtilities.SanitizeForDisplay(value.Trim(), maxLength);
	}
}
