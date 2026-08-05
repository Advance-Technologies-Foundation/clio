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
	/// Minimum process-builder version the consuming commands require on a target environment — the floor
	/// enforced by <c>[RequiresPackage]</c> against the version the PLATFORM reports.
	/// </summary>
	/// <remarks>
	/// <b>Effectively frozen. Do not raise this to match a rebundled archive.</b> Creatio does not update the
	/// recorded version when it re-installs a package it already has (it matches by <c>UId</c>), so the
	/// version clio reads back stays whatever the FIRST install wrote. Verified on both runtimes on
	/// 2026-08-05: after installing an archive whose descriptor said <c>1.1.0.0</c>,
	/// <c>ProcessDesignService.GetVersion</c> reported <c>1.1.0.0</c> — the new build was serving — while
	/// <c>clio list-packages</c> still reported <c>1.0.0.0</c> on both stands.
	/// <para>
	/// Raising this floor would therefore refuse the five gated commands FOREVER on every environment that
	/// already carries the package, no matter how correctly it was upgraded, and would make
	/// <c>install-process-builder</c> reinstall on every invocation because its short-circuit could never
	/// fire. Both were observed before this was understood. In practice the floor can only move when the
	/// package <c>UId</c> changes, i.e. for a genuinely different package.
	/// </para>
	/// <para>
	/// So this constant answers "is a compatible package installed at all", and
	/// <see cref="ProcessBuilderBuildVersion"/> answers "did the target compile the build we ship". Those are
	/// different questions with different sources — the database versus the running assembly — which is why
	/// they are two constants and not one.
	/// </para>
	/// <para>
	/// Must stay four-part: <c>RequiredPackageChecker.IsCompatible</c> compares through
	/// <see cref="System.Version"/>, which gives a three-part string a <c>Revision</c> of <c>-1</c>, so a
	/// four-part floor against a three-part installed version compares as installed &lt; required.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderVersion = "1.0.0.0";

	/// <summary>
	/// Version of the build inside the bundled archive — what <c>ProcessDesignService.GetVersion</c> must
	/// report after a successful install.
	/// </summary>
	/// <remarks>
	/// Moves with every rebundle, unlike <see cref="ProcessBuilderVersion"/>. Must equal BOTH the archive
	/// descriptor's <c>PackageVersion</c> and the <c>ProcessDesignConstants.PackageVersion</c> constant
	/// compiled into the shipped sources; <c>BundledProcessBuilderPackageTests</c> asserts both.
	/// <para>
	/// This is the value that detects a failed upgrade. The platform records a descriptor version when it
	/// ACCEPTS an archive and keeps serving the assembly from its last successful configuration build, so
	/// after a build failure the database and the running code disagree — and only the running code can be
	/// asked which it is.
	/// </para>
	/// </remarks>
	public const string ProcessBuilderBuildVersion = "1.1.0.0";

	/// <summary>
	/// File name of the bundled process-builder archive, inside the folder of the same name.
	/// </summary>
	public const string ProcessBuilderArchiveFileName = ProcessBuilderPackageName + ".gz";

	#endregion

}
