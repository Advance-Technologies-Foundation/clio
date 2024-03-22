using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Package;
using CommandLine;
using CreatioModel;

namespace Clio.Command
{
	[Verb("apply-manifest", Aliases = new[] {"applym", "apply-environment-manifest"},
		HelpText = "Apply manifest to environment")]
	public class ApplyEnvironmentManifestOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "ManifestFilePath", Required = true, HelpText = "Path to manifest")]
		public string ManifestFilePath { get; set; }
	}

	public class ApplyEnvironmentManifestCommand : Command<ApplyEnvironmentManifestOptions>
	{

		#region Fields: Private

		private readonly EnvironmentManager _environmentManager;
		private readonly IApplicationInstaller _applicationInstaller;

		#endregion

		#region Constructors: Public

		public ApplyEnvironmentManifestCommand(EnvironmentManager environmentManager,
			IApplicationInstaller applicationInstaller){
			_environmentManager = environmentManager;
			_applicationInstaller = applicationInstaller;
		}

		#endregion

		#region Methods: Public

		public override int Execute(ApplyEnvironmentManifestOptions options){
			List<SysInstalledApp> manifestApplications
				= _environmentManager.GetApplicationsFromManifest(options.ManifestFilePath);
			EnvironmentSettings manifestEnvironment
				= _environmentManager.GetEnvironmentFromManifest(options.ManifestFilePath);
			EnvironmentSettings environmentInstance = manifestEnvironment.Fill(options);
			IDataProvider provider = string.IsNullOrEmpty(environmentInstance.Login) switch {
				true => new RemoteDataProvider(environmentInstance.Uri, environmentInstance.AuthAppUri,
					environmentInstance.ClientId, environmentInstance.ClientSecret, environmentInstance.IsNetCore),
				false => new RemoteDataProvider(environmentInstance.Uri, environmentInstance.Login,
					environmentInstance.Password, environmentInstance.IsNetCore)
			};

			List<SysInstalledApp> remoteApplications = AppDataContextFactory.GetAppDataContext(provider)
				.Models<SysInstalledApp>()
				.ToList();

			foreach (SysInstalledApp remoteApp in remoteApplications) {
				bool inManifest
					= manifestApplications.Any(app => app.Name == remoteApp.Code || app.Name == remoteApp.Name);
				if (!inManifest) {
					_applicationInstaller.UnInstall(remoteApp, environmentInstance);
				}
			}

			List<SysInstalledApp> apps = _environmentManager.FindApllicationsInAppHub(options.ManifestFilePath);
			foreach (SysInstalledApp app in apps) {
				_applicationInstaller.Install(app.ZipFileName, environmentInstance);
			}
			return 0;
		}

		#endregion

	}
}