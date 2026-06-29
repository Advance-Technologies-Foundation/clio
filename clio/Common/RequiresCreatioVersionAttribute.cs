using System;

namespace Clio.Common;

/// <summary>
/// Declares that the annotated command (or option) requires the target Creatio environment to run
/// at least a minimum platform (core) version.
/// </summary>
/// <remarks>
/// <para>
/// The minimum version is expressed as a dotted version string (for example <c>"10.0.0"</c>) and is
/// compared against the environment's reported core version. Missing version components are treated
/// as zero, so <c>"10"</c> and <c>"10.0.0.0"</c> denote the same floor.
/// </para>
/// <para>
/// The attribute can be placed on a <b>class</b> or on a <b>property</b>:
/// <list type="bullet">
/// <item>
/// <description>
/// On a class the requirement is <b>always</b> enforced — the command cannot run unless the target
/// environment's core version is greater than or equal to <see cref="MinVersion"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// On a <c>bool</c> option property the requirement is enforced <b>only when that property is
/// <c>true</c></b> for the current invocation (the flag selects a code path that needs the newer
/// platform). Only <c>bool</c> properties are supported; decorating a non-<c>bool</c> property is a
/// misuse and fails fast.
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// The attribute is purely declarative. The runtime check is performed by an
/// <c>ICreatioVersionChecker</c>, which reads the requirement, resolves the environment's core
/// version and, when the requirement is unmet, throws a <see cref="CreatioVersionRequirementException"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RequiresCreatioVersionAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="RequiresCreatioVersionAttribute"/> class.
	/// </summary>
	/// <param name="minVersion">
	/// The minimum required Creatio core version as a dotted version string (for example
	/// <c>"10.0.0"</c>). Missing components are treated as zero.
	/// </param>
	public RequiresCreatioVersionAttribute(string minVersion) {
		MinVersion = minVersion;
	}

	/// <summary>
	/// Gets the minimum required Creatio core version as a dotted version string.
	/// </summary>
	public string MinVersion { get; }

	/// <summary>
	/// Gets or sets free-text, actionable guidance shown to the user when the requirement is unmet
	/// (for example, how to update Creatio). When <c>null</c> or empty no hint is appended to the
	/// <see cref="CreatioVersionRequirementException"/> message.
	/// </summary>
	public string Hint { get; set; }

	/// <summary>
	/// Determines whether the specified type, or any of its properties, declares a
	/// <see cref="RequiresCreatioVersionAttribute"/>.
	/// </summary>
	/// <param name="type">The type to inspect.</param>
	/// <returns>
	/// <c>true</c> when <paramref name="type"/> — or any of its (possibly inherited) properties —
	/// carries a <see cref="RequiresCreatioVersionAttribute"/>; otherwise <c>false</c>.
	/// </returns>
	/// <remarks>
	/// This is a cheap, network-free pre-check used by the dispatch gates (CLI and MCP) to decide
	/// whether a core-version verification — which requires contacting the environment — is needed at
	/// all. Property-level attributes MUST be considered here so a property-only-decorated options type
	/// is not silently skipped.
	/// </remarks>
	public static bool IsDefinedOn(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		if (type.IsDefined(typeof(RequiresCreatioVersionAttribute), inherit: true)) {
			return true;
		}
		return Array.Exists(
			type.GetProperties(),
			property => property.IsDefined(typeof(RequiresCreatioVersionAttribute), inherit: true));
	}
}
