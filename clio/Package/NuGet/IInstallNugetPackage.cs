namespace Clio.Project.NuGet
{
	using System.Collections.Generic;

	#region Interface: IInstallNugetPackage

	public interface IInstallNugetPackage
	{

		#region Methods: Public

		/// <summary>Restores the NuGet packages and installs them into the environment as one archive.</summary>
		/// <param name="nugetPackageFullNames">The packages, each a name and a version.</param>
		/// <param name="nugetSourceUrl">The NuGet feed to restore them from.</param>
		/// <exception cref="System.InvalidOperationException">
		/// The installer reported the installation as failed; it has already logged the reason, and the exception
		/// names the packages that were not installed.
		/// </exception>
		void Install(IEnumerable<NugetPackageFullName> nugetPackageFullNames, string nugetSourceUrl);
		void Install(string packageName, string version, string nugetSourceUrl);

		#endregion

	}

	#endregion

}