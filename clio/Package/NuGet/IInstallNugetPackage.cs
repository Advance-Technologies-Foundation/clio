namespace Clio.Project.NuGet
{
	using System.Collections.Generic;

	#region Interface: IInstallNugetPackage

	public interface IInstallNugetPackage
	{

		#region Methods: Public

		void Install(IEnumerable<NugetPackageFullName> nugetPackageFullNames, string nugetSourceUrl);
		void Install(string packageName, string version, string nugetSourceUrl);

		#endregion

	}

	#endregion

}