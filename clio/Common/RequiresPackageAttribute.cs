using System;

namespace Clio.Common;

/// <summary>
/// Declares that the annotated command (or any type) requires a Creatio package to be installed
/// in the target environment, optionally at a minimum version.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is package-agnostic: it names the required package by its package name and an
/// optional minimum version. When <see cref="Version"/> is <c>null</c> or empty the requirement is
/// presence-only — the package merely has to be installed, with no version comparison.
/// </para>
/// <para>
/// The attribute is purely declarative. The runtime check is performed by
/// <see cref="IRequiredPackageChecker"/>, which reads every <see cref="RequiresPackageAttribute"/>
/// declared on a type and validates the requirements against the installed packages.
/// </para>
/// <para>
/// Multiple attributes may be applied to a single type to declare several package requirements.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPackageAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="RequiresPackageAttribute"/> class.
	/// </summary>
	/// <param name="name">The name of the required Creatio package.</param>
	/// <param name="version">
	/// The optional minimum version of the required package. When <c>null</c> or empty the
	/// requirement is presence-only and no version comparison is performed.
	/// </param>
	public RequiresPackageAttribute(string name, string version = null) {
		Name = name;
		Version = version;
	}

	/// <summary>
	/// Gets the name of the required Creatio package.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the minimum required version of the package, or <c>null</c>/empty for a presence-only
	/// requirement.
	/// </summary>
	public string Version { get; }

	/// <summary>
	/// Gets or sets free-text, actionable guidance shown to the user when the requirement is unmet
	/// (for example, the command to run to install or update the package). When <c>null</c> or empty
	/// no hint is appended to the <see cref="PackageRequirementException"/> message.
	/// </summary>
	public string Hint { get; set; }

	/// <summary>
	/// Determines whether the specified type declares at least one <see cref="RequiresPackageAttribute"/>.
	/// </summary>
	/// <param name="type">The type to inspect.</param>
	/// <returns>
	/// <c>true</c> when <paramref name="type"/> carries a (possibly inherited)
	/// <see cref="RequiresPackageAttribute"/>; otherwise <c>false</c>.
	/// </returns>
	/// <remarks>
	/// This is a cheap, network-free pre-check used by callers (such as the MCP execution gate) to
	/// decide whether a package-requirement verification is needed before paying any resolution cost.
	/// Uses <c>inherit: true</c> to stay consistent with
	/// <see cref="IRequiredPackageChecker.EnsureRequirements(Type)"/>.
	/// </remarks>
	public static bool IsDefinedOn(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		return type.IsDefined(typeof(RequiresPackageAttribute), inherit: true);
	}
}
