using Clio.Project.NuGet;

namespace Clio.Common;

/// <summary>
/// The convergence rule: an environment should carry the version of a bundled package that the running
/// clio distribution carries.
/// </summary>
/// <remarks>
/// This is deliberately a DIFFERENT rule from <see cref="RequiresPackageAttribute"/>, not a flag on it.
/// The attribute states what the CODE needs in order to work — a fact about this build of clio, written by
/// whoever wrote the calling code. Convergence states what environments should be brought to — a delivery
/// policy, owned by the product. The two can legitimately disagree: a command requiring <c>1.2.0.0</c>, a
/// distribution carrying <c>1.5.0.0</c> and an environment on <c>1.3.0.0</c> is compatible but not
/// converged, and the reader's reason for acting is not the same in the two cases even though the remedy
/// is. Merging them into one number, which is what this feature originally did, makes both unsayable
/// separately. See <c>spec/adr/adr-bundled-package-version-source-of-truth.md</c>.
/// <para>
/// What it produces is a REFUSAL naming the install verb, never an install: clio does not run a
/// configuration build and restart a live instance as a side effect of an unrelated command. The agent (or
/// the developer) reads the refusal and runs the installer, which is the flow that already exists.
/// </para>
/// </remarks>
public interface IBundledPackageConvergence {

	/// <summary>
	/// Determines whether the environment must be brought up to the bundled version before proceeding.
	/// </summary>
	/// <param name="packageName">Package name (case-insensitive).</param>
	/// <param name="installedVersion">
	/// The version recorded in the target environment, or <c>null</c> when the package is not installed.
	/// </param>
	/// <param name="message">The user-facing refusal, without the caller's remediation hint.</param>
	/// <returns>
	/// <c>true</c> when the environment carries an older version than the distribution and must be updated.
	/// <c>false</c> for a package clio does not ship, for a package that is absent (that is the
	/// <see cref="RequiresPackageAttribute"/> gate's business, not this rule's), and for an environment
	/// already at or ahead of the bundled version.
	/// </returns>
	bool TryGetConvergenceRefusal(string packageName, PackageVersion installedVersion, out string message);

}

/// <inheritdoc cref="IBundledPackageConvergence"/>
public class BundledPackageConvergence : IBundledPackageConvergence {

	#region Fields: Private

	private readonly IBundledPackageCatalog _bundledPackageCatalog;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="BundledPackageConvergence"/> class.
	/// </summary>
	/// <param name="bundledPackageCatalog">Catalog answering what this distribution carries.</param>
	/// <param name="logger">Logger used to report a distribution that cannot describe itself.</param>
	public BundledPackageConvergence(IBundledPackageCatalog bundledPackageCatalog, ILogger logger) {
		bundledPackageCatalog.CheckArgumentNull(nameof(bundledPackageCatalog));
		logger.CheckArgumentNull(nameof(logger));
		_bundledPackageCatalog = bundledPackageCatalog;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public bool TryGetConvergenceRefusal(
		string packageName, PackageVersion installedVersion, out string message) {
		message = null;
		if (installedVersion is null || !_bundledPackageCatalog.IsBundled(packageName)) {
			return false;
		}
		if (!_bundledPackageCatalog.TryGetVersion(
				packageName, out PackageVersion bundledVersion, out string diagnosis)) {
			// Warn rather than refuse, and the asymmetry is the point. A distribution that cannot read its
			// own archive is broken and must not stay quiet about it — but the environment in front of the
			// user has the package installed and the command would succeed, so blocking it achieves nothing
			// except turning clio's defect into the user's. The requirement gate has already established that
			// the code can work here; convergence only decides whether it should be brought further forward,
			// and with no bundled version to compare against there is nothing it can decide.
			_logger.WriteWarning(diagnosis);
			return false;
		}
		if (!string.IsNullOrWhiteSpace(bundledVersion.Suffix)) {
			// CANNOT DECIDE, so warn and allow — the same answer as an unreadable version above, for the same
			// reason: clio's own artifact is malformed, and blocking would turn its defect into the user's.
			// Refusing here instead is a TRAP, and that was measured rather than reasoned about. The comparison
			// below uses PackageVersion's operator, whose CompareSuffix ranks an empty suffix BELOW a non-empty
			// one, so a bundled 1.0.1.0-rc makes an environment recording the GA 1.0.1.0 read as BEHIND — and so
			// does every lower version. Convergence would then refuse every gated call and point at the
			// installer as the remedy, while the installer refuses that very same distribution as malformed, and
			// the override for it is deliberately unavailable over MCP. Every gated tool dead, with no in-band
			// way out, over a defect in clio rather than anything about the environment.
			// Enforcement therefore lives in the install command, which can afford to refuse: refusing there
			// costs one command rather than the whole surface, and its message names the real problem.
			_logger.WriteWarning(
				$"This clio's bundled {packageName} declares version "
				+ $"{TextUtilities.SanitizeVersionForDisplay(bundledVersion)}, which carries a pre-release "
				+ "suffix. A bundled package version must be a plain four-part number, so this distribution "
				+ "cannot be compared against the version the environment records — the environment is NOT "
				+ "being reported as out of date. Reinstall or update clio itself; installing this package is "
				+ "refused separately until then.");
			return false;
		}
		if (installedVersion >= bundledVersion) {
			return false;
		}
		// BOTH versions go through the allowlist, and the installed one is the reason. It is read from the
		// target's SysPackage.Version column, whose text comes from a package's own descriptor — so anyone able
		// to install a package on that environment chooses it. PackageVersion treats everything after the first
		// '-' as a free-text suffix and re-emits it verbatim, newlines included, and this message does not stop
		// at a console: RequiredPackageChecker throws it as PackageRequirementException and BaseTool returns it
		// through FromValidationError, which does NOT redact — so it lands in an MCP agent's context, on EVERY
		// gated call rather than only on an install. That is a wider exposure than the install command's own
		// refusal, which has sanitised the identical value all along.
		// The bundled version is clio's own artifact and cannot be suffixed by the time it reaches here (the
		// branch above returned), so clamping it is belt-and-braces against a future edit rather than a
		// defence — but it costs nothing and the catalog is a reader, so it will hand over whatever it finds.
		message =
			$"This clio carries {packageName} {TextUtilities.SanitizeVersionForDisplay(bundledVersion)}, but the "
			+ $"target environment has {TextUtilities.SanitizeVersionForDisplay(installedVersion)}. Update the "
			+ "package in the target environment and retry.";
		return true;
	}

	#endregion

}
