using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.Package;
using CommandLine;
using CreatioModel;

namespace Clio.Command;

[Verb("apply-manifest", Aliases = new[] {"applym", "apply-environment-manifest"},
	HelpText = "Apply manifest to environment")]
public class ApplyEnvironmentManifestOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "ManifestFilePath", Required = true, HelpText = "Path to manifest")]
	public string ManifestFilePath { get; set; }

	#endregion

}

public class ApplyEnvironmentManifestCommand : Command<ApplyEnvironmentManifestOptions>
{

	#region Fields: Private

	private readonly IEnvironmentManager _environmentManager;
	private readonly IApplicationInstaller _applicationInstaller;
	private readonly FeatureCommand _featureCommand;
	private readonly SysSettingsCommand _sysSettingCommand;
	private readonly SetWebServiceUrlCommand _setWebServiceUrlCommand;
	private readonly IDataProvider _dataProvider;
	private readonly EnvironmentSettings _environmentSettings;

	#endregion

	#region Properties: Public

	/// <summary>Sink the report of unapplied manifest entries is written to.</summary>
	public ILogger Logger { get; set; } = ConsoleLogger.Instance;

	#endregion

	#region Constructors: Internal

	internal ApplyEnvironmentManifestCommand(){ }

	#endregion

	#region Constructors: Public

	public ApplyEnvironmentManifestCommand(IEnvironmentManager environmentManager,
		IApplicationInstaller applicationInstaller, FeatureCommand featureCommand, SysSettingsCommand sysSettingCommand,
		SetWebServiceUrlCommand setWebServiceUrlCommand, IDataProvider dataProvider,
		EnvironmentSettings environmentSettings){
		_environmentManager = environmentManager;
		_applicationInstaller = applicationInstaller;
		_featureCommand = featureCommand;
		_sysSettingCommand = sysSettingCommand;
		_setWebServiceUrlCommand = setWebServiceUrlCommand;
		_dataProvider = dataProvider;
		_environmentSettings = environmentSettings;
	}

	#endregion

	#region Methods: Private

	private void ApplyApplicationFromManifest(ApplyEnvironmentManifestOptions options,
		List<SysInstalledApp> remoteApplications,
		List<SysInstalledApp> manifestApplications, EnvironmentSettings environmentInstance,
		List<string> failures){
		if (manifestApplications is null || manifestApplications.Count == 0) {
			return;
		}

		foreach (SysInstalledApp remoteApp in remoteApplications) {
			bool inManifest
				= manifestApplications.Any(app => app.Name == remoteApp.Code
					|| app.Name == remoteApp.Name || app.Aliases.Contains(remoteApp.Name) ||
					app.Aliases.Contains(remoteApp.Code));
			if (!inManifest) {
				ApplyEntryOrRecordFailure(failures, $"uninstalling application '{remoteApp.Name}'",
					"the environment refused the uninstall",
					() => _applicationInstaller.UnInstall(remoteApp, environmentInstance));
			}
		}

		List<SysInstalledApp> apps = _environmentManager.FindApplicationsInAppHub(options.ManifestFilePath);
		foreach (string zipFileName in apps.Select(app => app.ZipFileName)) {
			ApplyEntryOrRecordFailure(failures, $"installing application '{zipFileName}'",
				"the environment refused the installation",
				() => _applicationInstaller.Install(zipFileName, environmentInstance));
		}
	}

	private void ApplyFeaturesFromManifest(ApplyEnvironmentManifestOptions options, IEnumerable<Feature> features,
		List<string> failures){
		if (features is null || features.Count() == 0) {
			return;
		}

		foreach (Feature feature in features) {
			FeatureOptions featureCommandOptions = new FeatureOptions {
				Code = feature.Code,
				State = feature.Value ? 1 : 0
			};
			featureCommandOptions.CopyFromEnvironmentSettings(options);
			ApplyEntryOrRecordFailure(failures, $"feature '{feature.Code}'",
				"the environment rejected the state write",
				() => _featureCommand.SetFeatureStateDefValue(featureCommandOptions));
			foreach (KeyValuePair<string, bool> userValue in feature.UserValues) {
				featureCommandOptions.SysAdminUnitName = userValue.Key;
				featureCommandOptions.State = userValue.Value ? 1 : 0;
				ApplyEntryOrRecordFailure(failures, $"feature '{feature.Code}' for '{userValue.Key}'",
					$"no role named '{userValue.Key}' exists in the environment",
					() => _featureCommand.SetFeatureStateForUser(featureCommandOptions));
			}
		}
	}

	private void ApplySettingsFromManifest(ApplyEnvironmentManifestOptions options,
		IEnumerable<CreatioManifestSetting> settings, List<string> failures){
		if (settings is null || settings.Count() == 0) {
			return;
		}

		foreach (CreatioManifestSetting setting in settings) {
			SysSettingsOptions sysSettingOption = new SysSettingsOptions {
				Code = setting.Code,
				Value = setting.Value
			};
			sysSettingOption.CopyFromEnvironmentSettings(options);
			ApplyEntryOrRecordFailure(failures, $"system setting '{setting.Code}'",
				"the environment did not update it",
				() => _sysSettingCommand.UpdateSysSetting(sysSettingOption));
		}
	}

	private void ApplyWebservicesFromManifest(ApplyEnvironmentManifestOptions options,
		IEnumerable<CreatioManifestWebService> webservices, List<string> failures){
		if (webservices is null || webservices.Count() == 0) {
			return;
		}
		foreach (CreatioManifestWebService webservice in webservices) {
			SetWebServiceUrlOptions webserviceUrlOption = new SetWebServiceUrlOptions {
				WebServiceName = webservice.Name,
				WebServiceUrl = webservice.Url
			};
			webserviceUrlOption.CopyFromEnvironmentSettings(options);
			ApplyEntryOrRecordFailure(failures, $"web service '{webservice.Name}'",
				"the environment refused the url write",
				() => _setWebServiceUrlCommand.Execute(webserviceUrlOption) == 0);
		}
	}

	private static void ApplyEntryOrRecordFailure(
		List<string> failures, string subject, string refusalReason, Func<bool> applyEntry){
		try {
			if (!applyEntry()) {
				failures.Add($"{subject}: {refusalReason}");
			}
		}
		catch (Exception exception) {
			failures.Add($"{subject}: {exception.Message}");
		}
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Applies every stage of the manifest — applications, features, system settings, web services — and reports
	/// the entries that could not be applied once all of them have run.
	/// </summary>
	/// <remarks>
	/// A refused entry neither abandons the entries after it nor skips the stages that follow it, so a refused
	/// feature still leaves the system settings and web services the manifest names applied. A manifest that
	/// cannot be read throws instead, because there is nothing to apply partially.
	/// </remarks>
	/// <param name="options">The manifest path and the target environment.</param>
	/// <returns>0 when every entry applied; 1 when at least one was reported as not applied.</returns>
	public override int Execute(ApplyEnvironmentManifestOptions options){
		List<SysInstalledApp> manifestApplications
			= _environmentManager.GetApplicationsFromManifest(options.ManifestFilePath);
		List<SysInstalledApp> remoteApplications = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysInstalledApp>()
			.ToList();
		List<string> failures = [];

		ApplyApplicationFromManifest(
			options, remoteApplications, manifestApplications, _environmentSettings, failures);

		IEnumerable<Feature> features = _environmentManager.GetFeaturesFromManifest(options.ManifestFilePath);
		IEnumerable<CreatioManifestSetting> settings
			= _environmentManager.GetSettingsFromManifest(options.ManifestFilePath);
		IEnumerable<CreatioManifestWebService> webservices
			= _environmentManager.GetWebServicesFromManifest(options.ManifestFilePath);
		ApplyFeaturesFromManifest(options, features, failures);
		ApplySettingsFromManifest(options, settings, failures);
		ApplyWebservicesFromManifest(options, webservices, failures);
		return Report(failures);
	}

	private int Report(List<string> failures){
		if (failures.Count == 0) {
			return 0;
		}
		Logger.WriteError(failures.Count == 1
			? "The manifest was applied except for 1 entry, so this run exits with code 1:"
			: $"The manifest was applied except for {failures.Count} entries, so this run exits with code 1:");
		foreach (string failure in failures) {
			Logger.WriteError($"  - {failure}");
		}
		return 1;
	}

	#endregion

}