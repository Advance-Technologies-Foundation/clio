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
	/// Version of the package inside the bundled archive.
	/// </summary>
	/// <remarks>
	/// <b>Descriptive, not a gate.</b> It is what <c>clio info</c> reports and what
	/// <c>BundledProcessBuilderPackageTests</c> compares against the archive descriptor, so a rebundle that
	/// forgets to move one of the two fails there. It is deliberately NOT the floor of the
	/// <c>[RequiresPackage]</c> gates: those are PRESENCE-ONLY.
	/// <para>
	/// A version floor cannot work here. Creatio does not rewrite a package's <c>SysPackage</c> row when it
	/// re-installs a package it already has: the archive arrives with <c>PackageStorageObjectState.NotChanged</c>
	/// (nothing on the zip-install path compares it against the database), so
	/// <c>PackageDBStorage.SavePackageDescriptor</c> returns early at its
	/// <c>GetIsPackageDescriptorModified</c> guard and never reaches the <c>SysPackage.Version</c> assignment.
	/// Verified on both runtimes on 2026-08-05: after installing a 1.1.0.0 archive the row still held 1.0.0.0
	/// AND the original <c>ModifiedOn</c> — the row was not touched at all. Bumping the descriptor's
	/// <c>ModifiedOnUtc</c> does not help; that comparison lives in <c>PackageStorageComposer</c>, which the
	/// zip-install path does not use.
	/// </para>
	/// <para>
	/// So a raised floor would refuse the five gated commands FOREVER on every environment that already
	/// carries the package, however correctly it was upgraded. Presence-only sidesteps that instead of
	/// documenting it. Whether the shipped build actually compiled is a different question, answered
	/// package-agnostically in clio from the platform's own signals (the installation log clio already
	/// receives, and the <c>ConfActivityLog</c> Compilation record) — not by a per-package endpoint, which
	/// would have to be re-implemented in every bundled package.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderVersion = "1.0.0.0";

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
