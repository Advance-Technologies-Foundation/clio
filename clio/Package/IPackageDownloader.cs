using System.Collections.Generic;

namespace Clio.Package
{

	#region Interface: IPackageDownloader

	public interface IPackageDownloader
	{

		#region Methods: Public

		void DownloadZipPackages(IEnumerable<string> packagesNames, EnvironmentSettings environmentSettings = null,
			string destinationPath = null);
		void DownloadZipPackage(string packageName, EnvironmentSettings environmentSettings = null,
			string destinationPath = null);
		/// <summary>
		/// Downloads and overwrites the requested package directories without clearing unrelated content from
		/// the shared destination root.
		/// </summary>
		/// <exception cref="System.InvalidOperationException">
		/// The environment did not answer the ping - a rejected login, a stopped site - so nothing was downloaded
		/// and the destination was not touched. The message carries the ping's own failure.
		/// </exception>
		/// <seealso cref="EnsureEnvironmentAnswers"/>
		void DownloadPackages(IEnumerable<string> packagesNames, EnvironmentSettings environmentSettings = null,
			string destinationPath = null);

		/// <summary>
		/// Pings the environment packages would be downloaded from, so a caller can fail BEFORE work that does not
		/// need the environment - for restore-workspace, the CreatioSDK lookup on api.nuget.org.
		/// </summary>
		/// <param name="environmentSettings">The environment; the startup environment when null.</param>
		/// <exception cref="System.InvalidOperationException">
		/// The environment did not answer - a rejected login, a stopped site. The message carries the reason.
		/// </exception>
		void EnsureEnvironmentAnswers(EnvironmentSettings environmentSettings = null);
		void DownloadPackage(string packageName, EnvironmentSettings environmentSettings  = null,
			string destinationPath = null);

		#endregion

	}

	#endregion

}
