using System;
using System.Linq;
using Clio.Common;

namespace Clio.Command.Theming;

public sealed record ThemeBackgroundArguments(
	string ImageId = null,
	string Color = null,
	bool KeepIconBackground = false,
	bool Remove = false) {

	private const int MaxEchoLength = 64;

	public bool HasImageId => !string.IsNullOrWhiteSpace(ImageId);

	public bool HasColor => !string.IsNullOrWhiteSpace(Color);

	public bool SetsBackground => HasImageId || HasColor;

	public bool IsPresent => SetsBackground || Remove;

	public bool TryValidate(out string error) {
		string[] sources = new (bool Given, string Name)[] {
			(HasImageId, "background-image-id"), (HasColor, "background-color"), (Remove, "remove-background")
		}.Where(source => source.Given).Select(source => source.Name).ToArray();
		error = null;
		if (sources.Length > 1) {
			error = $"{string.Join(" and ", sources)} cannot be combined: pass at most one background argument.";
		} else if (HasImageId && (!Guid.TryParse(ImageId.Trim(), out Guid imageId) || imageId == Guid.Empty)) {
			error = $"background-image-id '{TextUtilities.SanitizeForDisplay(ImageId, MaxEchoLength)}' is not a " +
				"valid id. Pass the id printed by upload-image.";
		}
		return error is null;
	}

	internal ThemeBackground ToBackground() {
		if (HasImageId) {
			return new ThemeBackground { Mode = ThemeBackground.ImageMode, ImageId = Guid.Parse(ImageId.Trim()).ToString() };
		}
		if (HasColor) {
			return new ThemeBackground { Mode = ThemeBackground.ColorMode, Color = Color.Trim() };
		}
		return Remove ? new ThemeBackground() : null;
	}
}
