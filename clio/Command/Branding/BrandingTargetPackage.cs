namespace Clio.Command.Branding;

/// <summary>
/// Pure formatting helpers for naming the branding delivery target in user-facing text. Deliberately a static
/// utility rather than part of <see cref="IBrandingBindingService"/>: it holds no state, performs no I/O, and is
/// needed exactly where the service is NOT usable — in the failure text a command writes when resolving the
/// target package is what failed. Keeping it off the behaviour interface lets a command depend only on
/// <see cref="IBrandingBindingService"/> and never on the concrete implementation.
/// </summary>
internal static class BrandingTargetPackage {

	/// <summary>
	/// The system setting that names the package design-time writes land in when the caller chooses none. The
	/// platform's own tooling keys off it (and the <c>create-theme</c> server call falls back to it), so branding
	/// follows the same convention instead of hardcoding a well-known package name.
	/// </summary>
	internal const string CurrentPackageSettingCode = "CurrentPackageId";

	/// <summary>
	/// Names the binding target in a message written before the package is resolved — either the package the
	/// caller asked for, or the environment's current one. Used for failure text, where the resolved name may not
	/// exist yet because resolution is what failed.
	/// </summary>
	/// <param name="packageName">The package the caller named, or blank/null when they named none.</param>
	internal static string Describe(string packageName) =>
		string.IsNullOrWhiteSpace(packageName)
			? $"the environment's current package ({CurrentPackageSettingCode})"
			: $"package '{packageName}'";
}
