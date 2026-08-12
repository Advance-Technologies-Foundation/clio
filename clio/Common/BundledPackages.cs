namespace Clio.Common;

/// <summary>
/// Identities of the Creatio packages that ship inside the clio distribution.
/// </summary>
/// <remarks>
/// These constants are the single source of truth for a bundled package's IDENTITY — its name and the file
/// it ships as. Everything that needs to name the package must reference them rather than repeat a literal.
/// <para>
/// A bundled package's VERSION is deliberately NOT here, and used to be. It lives in the archive's own
/// descriptor and is read by <see cref="IBundledPackageCatalog"/>: the archive is a content file copied to
/// the build output, so it can be — and during this feature's development was — replaced without
/// recompiling anything, which makes a constant in this assembly a claim about bytes it no longer
/// describes. The reasoning, and the three concepts that constant was conflating, are recorded in
/// <c>spec/adr/adr-bundled-package-version-source-of-truth.md</c>. Do not reintroduce one.
/// </para>
/// <para>
/// <b>cliogate's version-shaped values, in one place.</b> This analysis lives here and nowhere else — the
/// other documents that used to carry a copy now point at this remark, because one of those copies had
/// already drifted into being wrong. cliogate carries THREE things that look like "the cliogate version",
/// and only one pair of them is duplication:
/// <list type="bullet">
/// <item><description>
/// the PACKAGE version <c>2.0.0.44</c> — <c>cliogate/descriptor.json</c> and the <c>_gateVersion</c> constant
/// in <c>InfoCommand</c>. Both are written from one variable at the top of <c>build.ps1</c>, and these two
/// ARE the genuine duplication.
/// </description></item>
/// <item><description>
/// the ASSEMBLY version <c>1.1.1.2</c> — <c>cliogate/Properties/AssemblyInfo.cs</c> and
/// <c>clio/cliogate/version.txt</c>, both hand-maintained and NOT touched by <c>build.ps1</c>.
/// </description></item>
/// <item><description>
/// seven <c>[RequiresPackage("cliogate", …)]</c> sites, which are NOT copies of either. Four require only
/// presence; the other three carry <c>2.0.0.41</c> and <c>2.0.0.42</c> — deliberately not <c>2.0.0.44</c>,
/// because each states the version that introduced the operation ITS command calls. That is a requirement,
/// not a fact about what ships, and keeping the two apart is the whole point of the version ADR. Add a
/// literal when a command starts needing one; do not "align" the existing ones.
/// </description></item>
/// </list>
/// The first two are unrelated by design: cliogate ships a PREBUILT assembly, so its assembly version is ours
/// and need not track the package version. <c>Program.CheckApiVersion</c> compares <c>version.txt</c> against
/// <c>rest/CreatioApiGateway/GetApiVersion</c>, which returns <c>Assembly.GetName().Version</c> — assembly
/// against assembly, so the upgrade nudge is consistent and works. Do not "fix" <c>version.txt</c> to
/// <c>2.0.0.44</c>; that would break it.
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
