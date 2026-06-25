namespace Clio.Common;

/// <summary>
/// Validates declarative Creatio platform version requirements
/// (<see cref="RequiresCreatioVersionAttribute"/>) against the version reported by the target
/// environment.
/// </summary>
/// <remarks>
/// The checker reads the requirement from the command's options type (class-level — always enforced)
/// and from its <c>bool</c> option properties (property-level — enforced only when the flag is
/// <c>true</c>), resolves the environment's core version through <see cref="ICreatioVersionProvider"/>,
/// and — when a triggered requirement is unmet — throws a
/// <see cref="CreatioVersionRequirementException"/>. A development build (<c>0.0.0</c> / <c>0.0.0.0</c>)
/// is treated as compatible; a reachable environment whose version is undeterminable, and an
/// environment whose version check could not be performed at all, both fail closed (deny execution).
/// </remarks>
public interface ICreatioVersionChecker
{
	/// <summary>
	/// Validates every <see cref="RequiresCreatioVersionAttribute"/> declared on the specified options
	/// instance — both class-level (always enforced) and property-level (enforced only when the
	/// decorated <c>bool</c> property is <c>true</c> on this instance).
	/// </summary>
	/// <param name="optionsInstance">
	/// The options instance whose version requirements must be satisfied. The instance (not just its
	/// type) is required because property-level requirements are gated on the current value of the
	/// decorated <c>bool</c> property.
	/// </param>
	/// <exception cref="CreatioVersionRequirementException">
	/// Thrown when a triggered requirement is not satisfied: the environment runs an older core version
	/// (<see cref="CreatioVersionRequirementException.VersionTooOldCode"/>), a source responded but the
	/// version could not be determined
	/// (<see cref="CreatioVersionRequirementException.VersionUndeterminableCode"/>), or no source
	/// responded so the check could not be performed at all
	/// (<see cref="CreatioVersionRequirementException.VersionCheckFailedCode"/>).
	/// </exception>
	/// <exception cref="System.InvalidOperationException">
	/// Thrown when a non-<c>bool</c> property carries <see cref="RequiresCreatioVersionAttribute"/>; only
	/// <c>bool</c> properties are supported as conditional triggers.
	/// </exception>
	/// <remarks>
	/// When no class-level attribute is present and no property-level attribute is triggered (its
	/// <c>bool</c> value is <c>false</c>), the method returns without resolving the environment version,
	/// so commands without an active requirement incur no cost.
	/// </remarks>
	void EnsureRequirements(object optionsInstance);

	/// <summary>
	/// Determines whether the target environment's core version satisfies the specified minimum version,
	/// without throwing.
	/// </summary>
	/// <param name="minVersion">
	/// The minimum required Creatio core version as a dotted version string (for example
	/// <c>"10.0.0"</c>). Missing components are treated as zero.
	/// </param>
	/// <returns>
	/// <c>true</c> when the environment's core version is greater than or equal to
	/// <paramref name="minVersion"/>, or when the environment reports a development build (<c>0.0.0</c> /
	/// <c>0.0.0.0</c>); <c>false</c> when the version is lower than required, undeterminable, or the
	/// version check could not be performed at all.
	/// </returns>
	/// <exception cref="System.InvalidOperationException">
	/// Thrown when <paramref name="minVersion"/> is not a valid dotted version string. A malformed floor
	/// is a developer error in the requirement declaration and is classified identically to the malformed
	/// case in <see cref="EnsureRequirements"/> — it never degrades to <c>false</c>. An undeterminable
	/// environment version, by contrast, returns <c>false</c> and never throws.
	/// </exception>
	bool IsCompatible(string minVersion);
}
