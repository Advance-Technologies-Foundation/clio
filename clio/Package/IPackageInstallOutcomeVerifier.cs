namespace Clio.Package;

/// <summary>
/// Answers the question an install cannot answer for itself: after the target ACCEPTED a package, did that
/// package actually become operational there?
/// </summary>
/// <remarks>
/// The distinction is not pedantic, and it is the whole reason this abstraction exists. For a package that
/// ships as source, accepting the archive and compiling it are separate events: the platform records the
/// descriptor version on ACCEPT and keeps serving the assembly it last built successfully, so a failed
/// configuration build leaves an environment where the package is present, a name-based
/// <c>[RequiresPackage]</c> gate is satisfied, and every call into the package fails. No database read
/// distinguishes those two states.
/// <para>
/// Deliberately named for the QUESTION rather than for how it is answered today. The current implementation
/// probes the installed package's own service, which is the weakest form of the answer — it proves that
/// something answers, not WHICH build answered, so on an upgrade a still-serving old assembly passes it. The
/// planned replacement is package-agnostic and reads the platform's own signals instead: the installation log
/// clio already receives, plus the <c>ConfActivityLog</c> compilation record (a normal entity schema,
/// readable through DataService, carrying <c>Operation</c>, <c>Status</c>, <c>PackageName</c> and
/// <c>CreatedOn</c>). That replacement swaps the implementation without touching this interface, and serves
/// every bundled package rather than one.
/// </para>
/// </remarks>
public interface IPackageInstallOutcomeVerifier {

	/// <summary>
	/// Determines whether <paramref name="packageName"/> is operational on the target environment.
	/// </summary>
	/// <param name="packageName">Name of the package whose outcome is being verified.</param>
	/// <param name="diagnosis">
	/// When the verdict is <see langword="false"/> and the cause is known more precisely than "it does not
	/// work", the caller-facing explanation; otherwise <see langword="null"/>, leaving the caller to report
	/// its own generic message. It exists because the interesting negative answers are not all the same
	/// failure: "the package never compiled" and "the package is serving but refused the check" send the
	/// reader to different places, and only the verifier can tell them apart.
	/// </param>
	/// <returns>
	/// <see langword="true"/> only on positive evidence that the package works. Fails CLOSED: an
	/// unparseable, unreachable or ambiguous answer is <see langword="false"/>, because reporting success
	/// here is what makes an uncompiled package look like a healthy install.
	/// </returns>
	bool IsPackageOperational(string packageName, out string diagnosis);

}
