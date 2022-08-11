namespace Clio.Package
{
	using System.Collections.Generic;

	#region Interface: IApplicationPackageListProvider
	
	public interface IApplicationPackageListProvider
	{

		#region Methods: Public

		IEnumerable<PackageInfo> GetPackages();
		IEnumerable<PackageInfo> GetPackages(string scriptData);

		#endregion

	}

	#endregion

}