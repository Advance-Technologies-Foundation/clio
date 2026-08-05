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
	/// Version of the bundled process-builder archive, and therefore the minimum version the consuming
	/// commands require on a target environment.
	/// </summary>
	/// <remarks>
	/// This constant and the archive descriptor's <c>PackageVersion</c> MUST have the SAME NUMBER OF PARTS.
	/// <c>RequiredPackageChecker.IsCompatible</c> compares through <see cref="System.Version"/>, which gives
	/// a three-part string a <c>Revision</c> of <c>-1</c>: so a four-part floor of <c>1.0.0.0</c> against a
	/// three-part installed <c>1.0.0</c> evaluates as installed &lt; required, and the five gated commands
	/// refuse forever after a SUCCESSFUL install — while this command reinstalls (and forces the target to
	/// recompile) on every invocation, because its own short-circuit never fires either.
	/// <para>
	/// Both are four-part today. Changing the part count here without changing the descriptor — or the other
	/// way round — creates that unsatisfiable gate, which is why
	/// <c>BundledProcessBuilderPackageTests.BundledArchive_ShouldCarryADescriptorMatchingBundledPackages</c>
	/// compares the two.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderVersion = "1.0.0.0";

	/// <summary>
	/// File name of the bundled process-builder archive, inside the folder of the same name.
	/// </summary>
	public const string ProcessBuilderArchiveFileName = ProcessBuilderPackageName + ".gz";

	#endregion

}
