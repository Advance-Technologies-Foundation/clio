namespace Clio.Package;

/// <summary>
/// Answers the question an install cannot answer for itself: after the target ACCEPTED a package, did it also
/// COMPILE it, so that the package's own code is now serving?
/// </summary>
/// <remarks>
/// The distinction is not pedantic, and it is the whole reason this abstraction exists. For a package that
/// ships as source, accepting the archive and compiling it are separate events, and NO database read
/// distinguishes them: <c>SysPackage.Version</c> records what was accepted either way, so a package can be
/// present, satisfy a name- or version-based <c>[RequiresPackage]</c> gate, and still have no compiled assembly
/// behind it. Only the package's own code, answering a request, is evidence that it was compiled. This was
/// observed on a stand, not reasoned about: an install reported <c>Configuration build finished</c> with no
/// errors while the service route was absent.
/// <para>
/// The verdict is deliberately LIVENESS ONLY — it does not establish that the serving build came from the
/// sources of this particular install. Doing that requires the shipped version to be readable back out of the
/// running code, which for a source-only package means a hand-maintained copy of it inside the sources: the
/// assembly version is not ours (a stand measured the platform stamping its own over the csproj's), and
/// <c>descriptor.json</c> is not present in the target's build directory. That copy would only have bought
/// detection of a stale build on an UPGRADE — on a first install, serving at all IS the proof — and the
/// duplicate was judged the more expensive of the two. Revisit if stale-build upgrades prove common.
/// </para>
/// <para>
/// Deliberately named for the QUESTION rather than for how it is answered today, so a package that triggers no
/// compilation, or a future platform-side signal, can be served by the same seam.
/// </para>
/// </remarks>
public interface IPackageInstallOutcomeVerifier {

	/// <summary>
	/// Determines whether the implementation's OWN package is serving on the target environment.
	/// </summary>
	/// <param name="packageName">
	/// The package the caller is asking about, used to name it in <paramref name="diagnosis"/>. It does NOT
	/// select what gets probed: an implementation answers for the one package it was written for, and the
	/// only one today probes <c>ProcessDesignService.Ping</c> unconditionally. So passing a name this
	/// implementation does not serve yields an answer about ITS package, not about the one named — say what
	/// you mean by resolving the right implementation, not by the argument. Stated because the summary used
	/// to read as though the name dispatched, and a second bundled package will need a keyed or explicit
	/// registration rather than the assembly scan that registers this one.
	/// </param>
	/// <param name="diagnosis">
	/// The caller-facing explanation when the verifier can name a cause the caller could not guess; otherwise
	/// <see langword="null"/>, and the caller's own message stands. It exists because a negative answer has more
	/// than one cause and they lead to different places: "something else is serving that route" (a proxy, a
	/// gateway, a session redirect) must NOT be reported as a failed configuration build. Deliberately null for
	/// the two causes the caller already describes correctly — nothing answered at all, and an unreachable
	/// instance, whose status is logged separately by the implementation.
	/// </param>
	/// <returns>
	/// <see langword="true"/> only on positive evidence that the package's service answered. Fails CLOSED: an
	/// unparseable, unreachable or ambiguous answer is <see langword="false"/>, because reporting success is
	/// what makes an uncompiled package look like a healthy install.
	/// </returns>
	bool IsPackageOperational(string packageName, out string diagnosis);

}
