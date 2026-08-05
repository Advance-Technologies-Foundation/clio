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
	/// Raising this is safe ONLY if the archive's <c>descriptor.json</c> bumps <c>ModifiedOnUtc</c> in the
	/// same change. Creatio decides whether to rewrite a package's <c>SysPackage</c> row from
	/// <c>ModifiedOnUtc</c>, not from <c>PackageVersion</c>:
	/// <c>PackageStorageComposer.ApplySourcePackageChanges</c> sets <c>IsPackageDescriptorChanged</c> when the
	/// dates differ, and without it <c>PackageDBStorage.SavePackageDescriptor</c> returns early at its
	/// <c>GetIsPackageDescriptorModified</c> guard and never reaches the <c>SysPackage.Version</c> assignment.
	/// <para>
	/// So a one-sided bump installs cleanly and leaves the RECORDED version at the old value — and this floor
	/// then refuses the five gated commands on an environment that was upgraded correctly. Both halves were
	/// observed live on 2026-08-05: with only <c>PackageVersion</c> moved the row kept <c>1.0.0.0</c>; once
	/// <c>ModifiedOnUtc</c> moved too the row took the new version and the descriptor's own timestamp.
	/// </para>
	/// <para>
	/// <c>BundledProcessBuilderPackageTests</c> pins this value AND the descriptor's <c>ModifiedOnUtc</c>
	/// beside the archive SHA-256, so a one-sided bump cannot pass review. The producing repository documents
	/// the paired step in <c>docs/bundling-into-clio.md</c>.
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
