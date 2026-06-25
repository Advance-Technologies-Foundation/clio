using System;
using System.Collections.Generic;
using System.Reflection;

namespace Clio.Common;

/// <inheritdoc cref="ICreatioVersionChecker"/>
public sealed class CreatioVersionChecker : ICreatioVersionChecker
{

	#region Constants: Private

	// A development build reports this version; treat it as compatible with any requirement so local
	// dev environments are never gated (mirrors DataForgePlatformVersionGuard.DevBuildVersion).
	private static readonly Version DevBuildVersion = new(0, 0, 0, 0);

	#endregion

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

	#endregion

	#region Methods: Public

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

		Version currentVersion = _creatioVersionProvider.GetCoreVersion();

		// Fail-closed: an undeterminable version must deny execution rather than run against an unknown
		// platform.
		if (currentVersion is null) {
			RequiresCreatioVersionAttribute firstRequirement = triggered[0];
			throw new CreatioVersionRequirementException(
				$"Could not determine the Creatio platform version of the target environment; " +
				$"this command requires {firstRequirement.MinVersion} or later.",
				CreatioVersionRequirementException.VersionUndeterminableCode);
		}

		// Dev-build bypass: a development build (0.0.0.0) satisfies every requirement.
		if (currentVersion == DevBuildVersion) {
			return;
		}

		foreach (RequiresCreatioVersionAttribute requirement in triggered) {
			Version minVersion = ParseMinVersion(requirement.MinVersion);
			if (currentVersion < minVersion) {
				throw new CreatioVersionRequirementException(
					AppendHint(
						$"This command requires Creatio {requirement.MinVersion} or later. " +
						$"The target environment runs {currentVersion}. Update Creatio and retry.",
						requirement.Hint),
					CreatioVersionRequirementException.VersionTooOldCode);
			}
		}
	}

	public bool IsCompatible(string minVersion) {
		if (!Version.TryParse(minVersion, out Version min)) {
			return false;
		}

		Version currentVersion = _creatioVersionProvider.GetCoreVersion();
		// Undeterminable version is not compatible — but never throws (mirrors the EnsureRequirements
		// fail-closed stance without surfacing an exception to the caller).
		if (currentVersion is null) {
			return false;
		}

		// Dev-build bypass: a development build (0.0.0.0) is compatible with any requirement.
		if (currentVersion == DevBuildVersion) {
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
