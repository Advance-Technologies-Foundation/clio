using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Package;
using Clio.Project.NuGet;
using Version = System.Version;

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

public class ClioGateway : IClioGateway
{

	#region Fields: Private

	private readonly IApplicationPackageListProvider _applicationPackageListProvider;

	#endregion

	#region Constructors: Public

	public ClioGateway(IApplicationPackageListProvider applicationPackageListProvider){
		_applicationPackageListProvider = applicationPackageListProvider;
	}

	#endregion

	#region Methods: Public

	public PackageVersion GetInstalledVersion(){
		const string clioPkgNameNetFramework = "cliogate";
		const string clioPkgNameNetCore = "cliogate_netcore";

		IEnumerable<PackageInfo> allPackages = _applicationPackageListProvider
			.GetPackages();

		return allPackages.Where(p =>
				p.Descriptor.Name is clioPkgNameNetFramework or clioPkgNameNetCore)
			.MinBy(p => p.Version)
			?.Version;
	}

	public bool IsCompatibleWith(string version){
		PackageVersion installedVersion = GetInstalledVersion();
		if (installedVersion is null) {
			return false;
		}
		return installedVersion >= new PackageVersion(new Version(version), string.Empty);
	}

	public void CheckCompatibleVersion(string version){
		if (!IsCompatibleWith(version)) {
			throw new NotSupportedException($"To use this command, you need to install the cliogate package version {version} or higher.");
		}
	}

	#endregion

}