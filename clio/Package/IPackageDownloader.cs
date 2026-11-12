using System.Collections.Generic;

namespace Clio.Package
{

	#region Interface: IPackageDownloader

	public interface IPackageDownloader
	{

		#region Methods: Public

		/// <summary>Downloads each package's archive into the destination.</summary>
		/// <exception cref="System.Exception">
		/// The first download that fails, rethrown after its "not completed" line, so the caller never carries on
		/// with a missing archive.
		/// </exception>
		void DownloadZipPackages(IEnumerable<string> packagesNames, EnvironmentSettings environmentSettings = null,
			string destinationPath = null);
		void DownloadZipPackage(string packageName, EnvironmentSettings environmentSettings = null,
			string destinationPath = null);
		/// <summary>
		/// Downloads and overwrites the requested package directories without clearing unrelated content from
		/// the shared destination root.
		/// </summary>
		void DownloadPackages(IEnumerable<string> packagesNames, EnvironmentSettings environmentSettings = null,
			string destinationPath = null);
		void DownloadPackage(string packageName, EnvironmentSettings environmentSettings  = null,
			string destinationPath = null);

		#endregion

	}

	#endregion

}
