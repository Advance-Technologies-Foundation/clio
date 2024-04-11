using System;
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
		private readonly FeatureCommand _featureCommand;
		private readonly SysSettingsCommand _sysSettingCommand;
		private readonly SetWebServiceUrlCommand _webserviceUrlCommand;

		#endregion

		#region Constructors: Public

		public ApplyEnvironmentManifestCommand(EnvironmentManager environmentManager,
			IApplicationInstaller applicationInstaller, FeatureCommand featureCommand, SysSettingsCommand sysSettingCommand){
			_environmentManager = environmentManager;
			_applicationInstaller = applicationInstaller;
			_featureCommand = featureCommand;
			_sysSettingCommand = sysSettingCommand;
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

			ApplyApplicationFromManifest(options, remoteApplications, manifestApplications, environmentInstance);
			
			var features = _environmentManager.GetFeaturesFromManifest(options.ManifestFilePath);
			var settings = _environmentManager.GetSettingsFromManifest(options.ManifestFilePath);
			var webservices = _environmentManager.GetWebServicesFromManifest(options.ManifestFilePath);
			ApplyFeaturesFromManifest(options, features, environmentInstance);
			ApplySettingsFromManifest(options, settings, environmentInstance);
			ApplyWebservicesFromManifest(options, webservices, environmentInstance);
			return 0;
		}

		private void ApplyWebservicesFromManifest(ApplyEnvironmentManifestOptions options, IEnumerable<CreatioManifestWebService> webservices, EnvironmentSettings environmentInstance) {
			if (webservices is null || webservices.Count() == 0) {
				return;
			}
			foreach (var webservice in webservices) {
				var webserviceUrlOption = new SetWebServiceUrlOptions() {
					WebServiceName = webservice.Name,
					WebServiceUrl = webservice.Url
				};
				webserviceUrlOption.CopyFromEnvironmentSettings(options);
				_webserviceUrlCommand.Execute(webserviceUrlOption);
			}
		}

		private void ApplyFeaturesFromManifest(ApplyEnvironmentManifestOptions options,IEnumerable<Feature> features, EnvironmentSettings environmentInstance){

			if(features is null || features.Count() == 0) {
				return;
			}
			
			foreach (Feature feature in features) {
				var featureCommandOptions = new FeatureOptions() {
					Code = feature.Code,
					State = feature.Value ? 1:0
				};
				featureCommandOptions.CopyFromEnvironmentSettings(options); ;
				_featureCommand.SetFeatureStateDefValue(featureCommandOptions);
				foreach (KeyValuePair<string, bool> userValue in feature?.UserValues) {
					featureCommandOptions.SysAdminUnitName = userValue.Key;
					featureCommandOptions.State = userValue.Value ? 1:0;
					_featureCommand.SetFeatureStateForUser(featureCommandOptions);
				}
			}
		}

		private void ApplySettingsFromManifest(ApplyEnvironmentManifestOptions options,IEnumerable<CreatioManifestSetting> settings, EnvironmentSettings environmentInstance){

			if(settings is null || settings.Count() == 0) {
            	return;
            }
			
			foreach (var setting in settings) {
				
				var sysSettingOption = new SysSettingsOptions() {
					Code = setting.Code,
					Value = setting.Value,
				};
				sysSettingOption.CopyFromEnvironmentSettings(options);
				_sysSettingCommand.UpdateSysSetting(sysSettingOption);
			}
			
		}
		private void ApplyApplicationFromManifest(ApplyEnvironmentManifestOptions options, List<SysInstalledApp> remoteApplications,
			List<SysInstalledApp> manifestApplications, EnvironmentSettings environmentInstance){
			
			if(manifestApplications is null || manifestApplications.Count == 0) {
				return;
			}
			
			foreach (SysInstalledApp remoteApp in remoteApplications) {
				bool inManifest
					= manifestApplications.Any(app => app.Name == remoteApp.Code || app.Name == remoteApp.Name);
				if (!inManifest) {
					_applicationInstaller.UnInstall(remoteApp, environmentInstance);
				}
			}

			List<SysInstalledApp> apps = _environmentManager.FindApplicationsInAppHub(options.ManifestFilePath);
			foreach (SysInstalledApp app in apps) {
				_applicationInstaller.Install(app.ZipFileName, environmentInstance);
			}
		}

		#endregion

	}
}