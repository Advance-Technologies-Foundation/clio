namespace Clio.Common;

/// <summary>
/// Identities of the Creatio packages that ship inside the clio distribution.
/// </summary>
/// <remarks>
/// These constants are the single source of truth for a bundled package's identity. Everything that
/// needs to name the package — the install command's artifact path, the <c>[RequiresPackage]</c> gate on
/// the consuming commands, and the version surfaced by <c>clio info</c> — must reference them rather
/// than repeat a literal.
/// <para>
/// The cliogate version is deliberately NOT here yet: it is currently spread across a constant in
/// <c>InfoCommand</c>, <c>cliogate/descriptor.json</c> and a stale <c>cliogate/version.txt</c> that
/// nothing writes. Collapsing that triple is separate work; do not add a fourth copy.
/// </para>
/// </remarks>
public static class BundledPackages {

	#region Constants: Public

	/// <summary>
	/// Package name of the bundled process-builder package, which serves <c>ProcessDesignService</c>.
	/// </summary>
	/// <remarks>
	/// Must match, byte for byte, the <c>Name</c> in the bundled archive's <c>descriptor.json</c> and the
	/// name of the assembly inside it. The platform resolves the assembly it loads as
	/// <c>&lt;packageName&gt;.dll</c> under <c>Files/Bin</c> (net472) or <c>Files/Bin/netstandard</c>
	/// (.NET), and that lookup is case-sensitive on Linux hosts.
	/// </remarks>
	public const string ProcessBuilderPackageName = "CrtProcessBuilder";

	/// <summary>
	/// Version of the package inside the bundled archive, and the floor the <c>[RequiresPackage]</c> gates
	/// enforce against the version the target environment reports.
	/// </summary>
	/// <remarks>
	/// Bump it with <c>clio set-pkg-version &lt;package-path&gt; --PackageVersion X.Y.Z.W</c> rather than by
	/// editing <c>descriptor.json</c> by hand. That command writes <c>PackageVersion</c> AND stamps
	/// <c>ModifiedOnUtc</c>, and both are needed for a bump to take effect: the date decides WHETHER Creatio
	/// rewrites the package's <c>SysPackage</c> row at all, the version decides WHAT lands there.
	/// <para>
	/// The mechanism, for anyone auditing a version that did not move:
	/// <c>PackageStorageComposer.ApplySourcePackageChanges</c> sets <c>IsPackageDescriptorChanged</c> when the
	/// descriptor's <c>ModifiedOnUtc</c> (or repository revision) differs — <c>PackageVersion</c> is not part
	/// of that comparison — and without that flag <c>PackageDBStorage.SavePackageDescriptor</c> returns early
	/// at its <c>GetIsPackageDescriptorModified</c> guard, never reaching the <c>SysPackage.Version</c>
	/// assignment. This is coherent platform behaviour, not a defect: <c>ModifiedOnUtc</c> IS the field that
	/// means "this descriptor changed", and the supported tooling maintains it. Only a hand edit can leave it
	/// stale while the version moves.
	/// </para>
	/// <para>
	/// Both halves were observed live on 2026-08-05: with only <c>PackageVersion</c> moved the row kept
	/// <c>1.0.0.0</c>; once <c>ModifiedOnUtc</c> moved too the row took the new version and the descriptor's
	/// own timestamp, on both .NET Framework and .NET.
	/// </para>
	/// <para>
	/// Because THIS archive is hand-produced, <c>BundledProcessBuilderPackageTests</c> pins this value and the
	/// descriptor's <c>ModifiedOnUtc</c> beside the archive SHA-256 — cheap insurance against exactly the hand
	/// edit that cannot happen through <c>set-pkg-version</c>. The producing repository documents the step in
	/// <c>docs/bundling-into-clio.md</c>.
	/// </para>
	/// <para>
	/// Must stay four-part: <c>RequiredPackageChecker.IsCompatible</c> compares through
	/// <see cref="System.Version"/>, which gives a three-part string a <c>Revision</c> of <c>-1</c>, so a
	/// four-part floor against a three-part installed version compares as installed &lt; required.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderVersion = "1.1.0.1";

	/// <summary>
	/// File name of the bundled process-builder archive, inside the folder of the same name.
	/// </summary>
	public const string ProcessBuilderArchiveFileName = ProcessBuilderPackageName + ".gz";

	/// <summary>
	/// The remediation text every process-designer gate puts on its <c>[RequiresPackage]</c> hint.
	/// </summary>
	/// <remarks>
	/// One constant because it was five identical literals, and a rename of the verb would have been an
	/// invitation to update four of them. It names BOTH surfaces on purpose: the same refusal is read by a
	/// developer on the CLI and by an agent over MCP, and each needs the name it can actually invoke.
	/// <para>
	/// Attribute arguments must be compile-time constants, so this is a <c>const</c> built by concatenation
	/// rather than an interpolated string.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderInstallHint =
		"Run 'clio install-process-builder -e <environment>' (or call the install-process-builder "
		+ "MCP tool) to install or update " + ProcessBuilderPackageName + ".";

	#endregion

}
