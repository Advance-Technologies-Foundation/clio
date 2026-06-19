using System;
using Clio.Project.NuGet;

namespace Clio.Common;

public interface IClioGateway
{

	#region Methods: Public

	/// <summary>
	/// Retrieves the installed version of the package.
	/// </summary>
	/// <returns>The <see cref="PackageVersion"/> of the installed package, or null if no version is installed.</returns>
	public PackageVersion GetInstalledVersion();

	/// <summary>
	/// Determines if the specified version string is compatible with the installed package version.
	/// </summary>
	/// <param name="version">The version string to compare against the installed package version.</param>
	/// <returns>true if the specified version is compatible; otherwise, false.</returns>
	bool IsCompatibleWith(string version);

	#endregion
	
	/// <summary>
	/// Checks if the specified version is compatible with the installed package version.
	/// Throws a NotSupportedException if the version is not compatible.
	/// </summary>
	/// <param name="version">The version string to check for compatibility.</param>
	void CheckCompatibleVersion(string version);
}

/// <summary>
/// cliogate-specific facade over <see cref="IRequiredPackageChecker"/>. Kept as the queryable API for
/// callers that conditionally branch on cliogate availability (for example <c>get-info</c> and
/// <c>ApplicationDownloader</c>). All package lookups are delegated to the generic checker, whose alias
/// map already resolves both <c>cliogate</c> and <c>cliogate_netcore</c>.
/// </summary>
public class ClioGateway : IClioGateway
{

	#region Constants: Private

	private const string CliogatePackageName = "cliogate";

	#endregion

	#region Fields: Private

	private readonly IRequiredPackageChecker _requiredPackageChecker;

	#endregion

	#region Constructors: Public

	public ClioGateway(IRequiredPackageChecker requiredPackageChecker){
		_requiredPackageChecker = requiredPackageChecker;
	}

	#endregion

	#region Methods: Public

	public PackageVersion GetInstalledVersion()
		=> _requiredPackageChecker.GetInstalledVersion(CliogatePackageName);

	public bool IsCompatibleWith(string version)
		=> _requiredPackageChecker.IsCompatible(CliogatePackageName, version);

	public void CheckCompatibleVersion(string version){
		if (!IsCompatibleWith(version)) {
			throw new NotSupportedException(
				$"To use this command, you need to install the cliogate package version {version} or higher. " +
				"Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) and retry.");
		}
	}

	#endregion

}