using System;

namespace Clio.Common;

/// <summary>
/// The outcome of attempting to resolve the target environment's Creatio core version. The status
/// distinguishes the three machine-readable failure classes the version gate must report, which a
/// bare <see cref="Version"/> (nullable) cannot express.
/// </summary>
public enum CreatioVersionResolutionStatus
{
	/// <summary>
	/// A source produced a parseable core version. The accompanying
	/// <see cref="CreatioVersionResolution.Version"/> is non-null.
	/// </summary>
	Resolved,

	/// <summary>
	/// At least one source responded (the HTTP call returned without throwing) but none carried a
	/// usable, parseable version — the environment is reachable yet its version is undeterminable.
	/// </summary>
	ReachableWithoutVersion,

	/// <summary>
	/// No source responded at all — every attempted probe threw a soft-degradable
	/// (transport / parse) exception, so the version check could not be performed.
	/// </summary>
	ProbeFailed
}

/// <summary>
/// The result of an <see cref="ICreatioVersionProvider"/> resolution: the resolution
/// <see cref="Status"/> and, when <see cref="CreatioVersionResolutionStatus.Resolved"/>, the parsed
/// core <see cref="Version"/>.
/// </summary>
/// <param name="Version">
/// The resolved core version; non-null only when <paramref name="Status"/> is
/// <see cref="CreatioVersionResolutionStatus.Resolved"/>, otherwise <c>null</c>.
/// </param>
/// <param name="Status">The resolution outcome class.</param>
public sealed record CreatioVersionResolution(Version Version, CreatioVersionResolutionStatus Status)
{
	/// <summary>
	/// Creates a <see cref="CreatioVersionResolutionStatus.Resolved"/> resolution carrying the parsed
	/// core version.
	/// </summary>
	/// <param name="version">The parsed core version (must be non-null).</param>
	/// <returns>A resolved result wrapping <paramref name="version"/>.</returns>
	public static CreatioVersionResolution Resolved(Version version) =>
		new(version, CreatioVersionResolutionStatus.Resolved);

	/// <summary>
	/// Creates a <see cref="CreatioVersionResolutionStatus.ReachableWithoutVersion"/> resolution — a
	/// source responded but no usable version was found.
	/// </summary>
	/// <returns>A reachable-without-version result with a <c>null</c> version.</returns>
	public static CreatioVersionResolution ReachableWithoutVersion() =>
		new(null, CreatioVersionResolutionStatus.ReachableWithoutVersion);

	/// <summary>
	/// Creates a <see cref="CreatioVersionResolutionStatus.ProbeFailed"/> resolution — no source
	/// responded, so the version check could not be performed.
	/// </summary>
	/// <returns>A probe-failed result with a <c>null</c> version.</returns>
	public static CreatioVersionResolution ProbeFailed() =>
		new(null, CreatioVersionResolutionStatus.ProbeFailed);
}
