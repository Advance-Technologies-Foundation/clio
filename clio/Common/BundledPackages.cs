using System;
using System.Collections.Generic;

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
/// the PACKAGE version — <c>cliogate/descriptor.json</c> and the <c>_gateVersion</c> constant
/// in <c>InfoCommand</c>. Both are written from one variable at the top of <c>build.ps1</c>, and these two
/// ARE the genuine duplication.
/// </description></item>
/// <item><description>
/// the ASSEMBLY version — <c>cliogate/Properties/AssemblyInfo.cs</c> and
/// <c>clio/cliogate/version.txt</c>, both hand-maintained and NOT touched by <c>build.ps1</c>.
/// </description></item>
/// <item><description>
/// <c>[RequiresPackage("cliogate", …)]</c> sites, which are NOT copies of either. Some require only
/// presence; the others carry operation-specific minimum versions — deliberately not all the current package version —
/// because each states the version that introduced the operation ITS command calls. That is a requirement,
/// not a fact about what ships, and keeping the two apart is the whole point of the version ADR. Add a
/// literal when a command starts needing one; do not "align" the existing ones.
/// </description></item>
/// </list>
/// The first two are unrelated by design: cliogate ships a PREBUILT assembly, so its assembly version is ours
/// and need not track the package version. <c>Program.CheckApiVersion</c> compares <c>version.txt</c> against
/// <c>rest/CreatioApiGateway/GetApiVersion</c>, which returns <c>Assembly.GetName().Version</c> — assembly
/// against assembly, so the upgrade nudge is consistent and works. Do not "fix" <c>version.txt</c> to
/// match the package version; that would break it.
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
	/// <para>
	/// It states the COST before the command (ENG-102116): the package ships as source, so installing it runs
	/// a configuration build on the target and the platform restarts afterwards, for every user of that
	/// environment. A refusal read by an agent on a shared stand is not the user's consent to that, and a hint
	/// that only named the command read as one. Keep it short - it is appended to every refusal.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderInstallHint =
		"Installing or updating " + ProcessBuilderPackageName + " builds the environment's configuration and "
		+ "restarts the site for every connected user. This refusal is not the user's approval: ask them first, "
		+ "then run 'clio install-process-builder -e <environment>' (or call the install-process-builder MCP tool).";

	/// <summary>
	/// The remedy clause the post-write read-back warnings put in parentheses after the action they recommend
	/// (install, update or reinstall the package): the verb plus the approval it needs. It names
	/// install-process-builder without the "clio" prefix because the same text reaches an agent over MCP, where it
	/// is the tool name, and a developer on the CLI, where it is the verb.
	/// </summary>
	/// <remarks>
	/// Those warnings ride on a SUCCESSFUL build or modify, so <see cref="ProcessBuilderInstallHint"/> - which
	/// only a refusal carries - never reaches them; without this clause they told the agent to install
	/// unconditionally (ENG-102116).
	/// </remarks>
	public const string ProcessBuilderInstallRemedy =
		"install-process-builder, after user approval - it builds the configuration and restarts the site for "
		+ "every user";

	/// <summary>
	/// Package name of the bundled dashboards-migrator package (the "Dashboards migrator" app), which
	/// migrates 7.x dashboards to Freedom UI and serves <c>DashboardsMigratorPingService</c> as its probe.
	/// </summary>
	/// <remarks>
	/// Same byte-for-byte contract with the archive's <c>descriptor.json</c> as
	/// <see cref="ProcessBuilderPackageName"/>. Unlike the process builder this package ships PREBUILT, like
	/// cliogate: the archive is the package's SDLC build and carries its assembly for both runtimes, so the
	/// target loads it instead of compiling it. It still goes through the shared install command, because the
	/// downgrade refusals, the restart wait and the Ping outcome check apply to it unchanged.
	/// </remarks>
	public const string DashboardsMigratorPackageName = "CrtDashboardsMigratorApp";

	/// <summary>
	/// File name of the bundled dashboards-migrator archive, inside the folder of the same name.
	/// </summary>
	public const string DashboardsMigratorArchiveFileName = DashboardsMigratorPackageName + ".gz";

	#endregion

	#region Fields: Private

	// Package name -> the ungated Ping route its install is verified through. The ONE place this pairing
	// lives: the install command quotes it to the operator and the outcome verifier probes it, so a route
	// stated twice could send someone to check a URL nobody called.
	private static readonly IReadOnlyDictionary<string, ServiceUrlBuilder.KnownRoute> PingRoutes =
		new Dictionary<string, ServiceUrlBuilder.KnownRoute>(StringComparer.OrdinalIgnoreCase) {
			[ProcessBuilderPackageName] = ServiceUrlBuilder.KnownRoute.ProcessBuilderPing,
			[DashboardsMigratorPackageName] = ServiceUrlBuilder.KnownRoute.DashboardsMigratorPing
		};

	#endregion

	#region Methods: Public

	/// <summary>
	/// Returns the Ping route of a bundled package.
	/// </summary>
	/// <param name="packageName">A package name from this class.</param>
	/// <exception cref="ArgumentException">The package is not bundled — a programming error, not a verdict.</exception>
	public static ServiceUrlBuilder.KnownRoute PingRouteOf(string packageName) {
		if (packageName is not null && PingRoutes.TryGetValue(packageName, out ServiceUrlBuilder.KnownRoute route)) {
			return route;
		}
		throw new ArgumentException(
			$"No Ping route is known for package '{packageName}'; only bundled packages can be verified.",
			nameof(packageName));
	}

	#endregion

}
