namespace Clio.Project.NuGet
{

	#region Class: PackageForUpdate
	
	public class PackageForUpdate
	{

		#region Constructors: Public

		public PackageForUpdate(LastVersionNugetPackages lastVersionNugetPackages, PackageInfo applicationPackage) {
			LastVersionNugetPackages = lastVersionNugetPackages;
			ApplicationPackage = applicationPackage;
		}

		#endregion

		#region Properties: Public

		public LastVersionNugetPackages LastVersionNugetPackages { get; }
		public PackageInfo ApplicationPackage { get; }

		#endregion


	}

	#endregion

}