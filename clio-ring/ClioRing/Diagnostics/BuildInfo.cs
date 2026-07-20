using System;

namespace ClioRing.Diagnostics;

/// <summary>
/// Build identity. <see cref="Version"/>, <see cref="GitHash"/> and <see cref="BuildTimeUtc"/> are
/// compile-time constants injected by the <c>GenerateBuildInfo</c> MSBuild target (generated
/// <c>BuildInfo.g.cs</c>) so every published/preview/AOT copy self-reports its real source commit
/// with no runtime git and no reflection. <see cref="Location"/> is resolved at runtime.
/// The deployment <c>channel</c> is NOT here — it is read from app-settings.json at runtime so a
/// staged copy can be relabelled (preview/aot/install) without a rebuild.
/// </summary>
public static partial class BuildInfo {
	// Version / GitHash / BuildTimeUtc consts are supplied by the generated BuildInfo.g.cs partial.

	/// <summary>Absolute directory the app is running from.</summary>
	public static string Location => AppContext.BaseDirectory;

	/// <summary>Full, human-readable identity string (used for tooltip / settings / clipboard).</summary>
	public static string Describe(string channel) =>
		$"clio ring {Version} · {channel} · {GitHash} · built {BuildTimeUtc} · {Location}";

	/// <summary>Compact badge string shown in the ring corner.</summary>
	public static string Badge(string channel) =>
		$"{channel} · {GitHash} · {BuildTimeUtc}";
}
