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
		bool hasColor = !string.IsNullOrWhiteSpace(color);
		if (hasImageId && hasColor) {
			error = $"{ImageIdArgument} and {ColorArgument} cannot be combined: pass at most one background argument.";
			return false;
		}
		if (hasImageId) {
			return TryParseImage(imageId, allowRemoval, out background, out error);
		}
		return !hasColor || TryParseColor(color, allowRemoval, out background, out error);
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

	private static bool TryParseImage(string imageId, bool allowRemoval, out ThemeBackground background,
		out string error) {
		background = null;
		string value = imageId.Trim();
		if (IsRemoval(value)) {
			return TryRemove(ImageIdArgument, "the id printed by upload-image", allowRemoval, out background, out error);
		}
		if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty) {
			error = $"{ImageIdArgument} '{TextUtilities.SanitizeForDisplay(imageId, MaxEchoLength)}' is not a " +
				"valid id. Pass the id printed by upload-image.";
			return false;
		}
		error = null;
		background = new ThemeBackground { Mode = ImageMode, ImageId = id.ToString() };
		return true;
	}

	private static bool TryParseColor(string color, bool allowRemoval, out ThemeBackground background,
		out string error) {
		background = null;
		string value = color.Trim();
		if (IsRemoval(value)) {
			return TryRemove(ColorArgument, "#RGB, #RRGGBB or #RRGGBBAA", allowRemoval, out background, out error);
		}
		if (!HexColorPattern.IsMatch(value)) {
			error = $"{ColorArgument} '{TextUtilities.SanitizeForDisplay(color, MaxEchoLength)}' is not a hex color. " +
				"Pass #RGB, #RRGGBB or #RRGGBBAA.";
			return false;
		}
		error = null;
		background = new ThemeBackground { Mode = ColorMode, Color = value };
		return true;
	}

	private static bool TryRemove(string argument, string expected, bool allowRemoval, out ThemeBackground background,
		out string error) {
		if (allowRemoval) {
			background = new ThemeBackground();
			error = null;
			return true;
		}
		background = null;
		error = $"{argument} {RemovalValue} removes a theme background, and only update-theme removes one: " +
			$"pass {expected} or leave {argument} out.";
		return false;
	}

	private static bool IsRemoval(string value) {
		return string.Equals(value, RemovalValue, StringComparison.OrdinalIgnoreCase);
	}

	private static string Clean(string value, int maxLength) {
		return string.IsNullOrWhiteSpace(value) ? null : TextUtilities.SanitizeForDisplay(value.Trim(), maxLength);
	}
}
