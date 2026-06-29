using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Clio.Common;

/// <inheritdoc cref="ICreatioVersionChecker"/>
public sealed class CreatioVersionChecker : ICreatioVersionChecker
{

	#region Fields: Private

	private readonly ICreatioVersionProvider _creatioVersionProvider;

	#endregion

	#region Constructors: Public

	public CreatioVersionChecker(ICreatioVersionProvider creatioVersionProvider) {
		_creatioVersionProvider = creatioVersionProvider;
	}

	#endregion

	#region Methods: Private

	// Builds the list of requirements that must be enforced for this invocation, doing only metadata
	// reflection (no environment call). Class-level attributes are always included; property-level
	// attributes are included only when their decorated bool property is true on the given instance.
	// A non-bool decorated property is a misuse and fails fast here, before any version lookup.
	private static List<RequiresCreatioVersionAttribute> CollectTriggeredRequirements(object instance, Type optionsType) {
		List<RequiresCreatioVersionAttribute> triggered = [
			.. (RequiresCreatioVersionAttribute[])optionsType.GetCustomAttributes(typeof(RequiresCreatioVersionAttribute), inherit: true)
		];

		foreach (PropertyInfo property in optionsType.GetProperties()) {
			RequiresCreatioVersionAttribute[] propertyRequirements = (RequiresCreatioVersionAttribute[])
				property.GetCustomAttributes(typeof(RequiresCreatioVersionAttribute), inherit: true);
			if (propertyRequirements.Length == 0) {
				continue;
			}

			if (property.PropertyType != typeof(bool)) {
				throw new InvalidOperationException(
					$"[RequiresCreatioVersion] on property '{optionsType.Name}.{property.Name}' is unsupported: " +
					"only bool properties may carry a conditional Creatio version requirement.");
			}

			if (property.GetValue(instance) is true) {
				triggered.AddRange(propertyRequirements);
			}
		}

		return triggered;
	}

	// Appends the attribute-supplied actionable hint to the base message as its own line.
	private static string AppendHint(string message, string hint) =>
		string.IsNullOrEmpty(hint) ? message : $"{message}{Environment.NewLine}{hint}";

	// A development build is treated as compatible with any requirement so local dev environments are
	// never gated (mirrors DataForgePlatformVersionGuard's dev-build bypass). Recognises BOTH a 4-part
	// "0.0.0.0" (Build 0, Revision 0) AND a 3-part "0.0.0" (Build 0, Revision -1 — System.Version leaves
	// an unspecified component at -1), so the bypass does not hinge on how many components were reported.
	private static bool IsDevBuild(Version v) =>
		v.Major == 0 && v.Minor == 0 && v.Build <= 0 && v.Revision <= 0;

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void EnsureRequirements(object optionsInstance) {
		ArgumentNullException.ThrowIfNull(optionsInstance);
		Type optionsType = optionsInstance.GetType();

		// Reflect FIRST and collect every triggered requirement; resolve the environment version only
		// when at least one requirement actually fires. This preserves the zero-cost guarantee: a command
		// with a property-level requirement whose bool flag is false performs no version lookup at all.
		List<RequiresCreatioVersionAttribute> triggered = CollectTriggeredRequirements(optionsInstance, optionsType);
		if (triggered.Count == 0) {
			return;
		}

		// Parse EVERY triggered MinVersion up front (also validating each — a malformed floor fails fast
		// as a developer error before any environment lookup) and enforce the STRICTEST one: with several
		// requirements active the highest floor is the only one that matters, and reporting it gives the
		// user the version they actually need.
		RequiresCreatioVersionAttribute strictest = triggered
			.OrderByDescending(requirement => ParseMinVersion(requirement.MinVersion))
			.First();
		Version requiredVersion = ParseMinVersion(strictest.MinVersion);

		CreatioVersionResolution resolution = _creatioVersionProvider.Resolve();

		switch (resolution.Status) {
			case CreatioVersionResolutionStatus.ProbeFailed:
				// Fail-closed: the version check could not be performed because no source responded. This
				// is a CONNECTIVITY / ACCESS failure, not an out-of-date platform — say so, and do NOT tell
				// the user to update Creatio. The attribute Hint ("how to satisfy the version requirement",
				// e.g. "update Creatio") is coherent only on the too-old branch, so it is deliberately NOT
				// appended here.
				throw new CreatioVersionRequirementException(
					$"Could not perform the Creatio platform version check for the target environment " +
					$"(it could not be reached, or access was denied). This command requires " +
					$"{strictest.MinVersion} or later — verify connectivity and permissions, then retry.",
					CreatioVersionRequirementException.VersionCheckFailedCode);

			case CreatioVersionResolutionStatus.ReachableWithoutVersion:
				// Fail-closed: a source responded but yielded no usable version. The platform is reachable
				// yet its version is undeterminable, so deny execution rather than run against an unknown
				// platform. Report the strictest floor so the user sees the real requirement.
				throw new CreatioVersionRequirementException(
					$"Could not determine the Creatio platform version of the target environment; " +
					$"this command requires {strictest.MinVersion} or later.",
					CreatioVersionRequirementException.VersionUndeterminableCode);
		}

		Version currentVersion = resolution.Version;

		// Dev-build bypass: a development build satisfies every requirement.
		if (IsDevBuild(currentVersion)) {
			return;
		}

		if (currentVersion < requiredVersion) {
			throw new CreatioVersionRequirementException(
				AppendHint(
					$"This command requires Creatio {strictest.MinVersion} or later. " +
					$"The target environment runs {currentVersion}. Update Creatio and retry.",
					strictest.Hint),
				CreatioVersionRequirementException.VersionTooOldCode);
		}
	}

	/// <inheritdoc/>
	public bool IsCompatible(string minVersion) {
		// A malformed floor is a DEVELOPER error and must fail fast (InvalidOperationException), exactly
		// as EnsureRequirements classifies it — never silently reported as "not compatible".
		Version min = ParseMinVersion(minVersion);

		CreatioVersionResolution resolution = _creatioVersionProvider.Resolve();
		// Anything other than a resolved version is not compatible — but never throws (mirrors the
		// EnsureRequirements fail-closed stance without surfacing an exception to the caller). Both an
		// undeterminable (ReachableWithoutVersion) and an uncheckable (ProbeFailed) environment are false.
		if (resolution.Status != CreatioVersionResolutionStatus.Resolved) {
			return false;
		}

		Version currentVersion = resolution.Version;

		// Dev-build bypass: a development build is compatible with any requirement.
		if (IsDevBuild(currentVersion)) {
			return true;
		}

		return currentVersion >= min;
	}

	// Parses the requirement's minimum version. System.Version already treats missing components as
	// zero, so "10" and "10.0.0.0" denote the same floor. A malformed MinVersion is a DEVELOPER error
	// in the attribute declaration — it must fail fast as such and must NOT be conflated with
	// version-too-old (an older environment) or version-undeterminable (an unknown environment).
	private static Version ParseMinVersion(string minVersion) {
		if (!Version.TryParse(minVersion, out Version min)) {
			throw new InvalidOperationException(
				$"[RequiresCreatioVersion] declares an invalid minimum version '{minVersion}'. " +
				"Specify a valid dotted version such as \"10.0.0\".");
		}
		return min;
	}

	#endregion

}
