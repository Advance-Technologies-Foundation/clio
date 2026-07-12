namespace ClioLauncher.Services;

/// <summary>
/// Read/write access to the ring's mutable clio-connection settings in <c>app-settings.json</c>.
/// Currently owns the optional dev-clio path override (see <see cref="ClioLauncher.Models.AppSettings.DevClioPath"/>).
/// Persistence preserves every other setting in the file untouched.
/// </summary>
public interface IClioSettingsStore {
	/// <summary>The path to the backing <c>app-settings.json</c> file (for display in the settings panel).</summary>
	string SettingsPath { get; }

	/// <summary>Reads the persisted dev-clio path override, or null when none is configured.</summary>
	string? ReadDevClioPath();

	/// <summary>
	/// Persists the dev-clio path override, updating only the <c>DevClioPath</c> field and leaving every
	/// other setting in the file intact. A null or blank value removes the override (reverts to the
	/// normal clio).
	/// </summary>
	/// <param name="path">The dev-clio build path, or null/blank to clear the override.</param>
	void SaveDevClioPath(string? path);
}
