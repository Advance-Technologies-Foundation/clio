using System.Collections.Generic;

namespace Clio.Package
{

	#region Interface: IPackageDownloader

	public interface IPackageDownloader
	{

		#region Methods: Public

		void DownloadZipPackages(IEnumerable<string> packagesNames, string destinationPath = null);
		void DownloadZipPackage(string packageName, string destinationPath = null);
		void DownloadPackages(IEnumerable<string> packagesNames, string destinationPath = null); 
		void DownloadPackage(string packageName, string destinationPath = null);

		#endregion

	}

	#endregion

}