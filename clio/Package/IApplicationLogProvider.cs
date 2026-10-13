using Clio.Common;
using System;

namespace Clio.Package
{
	/// <summary>Reads the package installation log of a Creatio environment.</summary>
	public interface IApplicationLogProvider {

		/// <summary>Reads the installation log through the session of the running installation.</summary>
		/// <param name="applicationClient">
		/// The client of the installation. The log is kept per node, so on a web farm only the session that
		/// installs the package reads the log of the node that runs the installation.
		/// </param>
		/// <param name="environmentSettings">The environment the package is installed into.</param>
		/// <returns>The log content, or an empty string when the log could not be read.</returns>
		string GetInstallationLog(IApplicationClient applicationClient, EnvironmentSettings environmentSettings);
	}

	public class ApplicationLogProvider: IApplicationLogProvider
	{
		private const string InstallLogUrl = @"/ServiceModel/PackageInstallerService.svc/GetLogFile";

		protected string GetCompleteUrl(string url, EnvironmentSettings environmentSettings) =>
	_serviceUrlBuilder.Build(url, environmentSettings);

		public ApplicationLogProvider(IServiceUrlBuilder serviceUrlBuilder)
        {
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		private IServiceUrlBuilder _serviceUrlBuilder;

		public string GetInstallationLog(IApplicationClient applicationClient,
			EnvironmentSettings environmentSettings) {
			try {
				return applicationClient.ExecuteGetRequest(GetCompleteUrl(InstallLogUrl, environmentSettings));
			} catch (Exception) { }
			return String.Empty;
		}
	}
}
