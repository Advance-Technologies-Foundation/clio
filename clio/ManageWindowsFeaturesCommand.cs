using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using CommandLine;

namespace Clio;

[Verb("manage-windows-features", Aliases = new[] {"mwf", "mng-win-features"},
	HelpText = "Install windows features required for Creatio")]
public class ManageWindowsFeaturesOptions
{

	#region Properties: Public

	[Option('c', "Check", Required = false, HelpText = "Check required feature states")]
	public bool CheckMode { get; set; }

	[Option('i', "Install", Required = false, HelpText = "Install required features")]
	public bool InstallMode { get; set; }

	[Option('u', "Uninstall", Required = false, HelpText = "Uninstall required features")]
	public bool UninstallMode { get; set; }

	#endregion

}

internal class ManageWindowsFeaturesCommand : Command<ManageWindowsFeaturesOptions>
{

	#region Fields: Private

	private readonly IWindowsFeatureManager _windowsFeatureManager;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public ManageWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager, ILogger logger){
		_windowsFeatureManager = windowsFeatureManager;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(ManageWindowsFeaturesOptions options){
		if (!OperationSystem.Current.IsWindows) {
			_logger.WriteError("This command is only available on Windows operating system.");
			return 1;
		}

		if (options.CheckMode) {
			return CheckRequiredFeatureOptions();
		} else if (options.InstallMode) {
			return InstallRequiredComponents();
		} else if (options.UninstallMode) {
			return UninstallRequiredComponents();
		} else {
			_logger.WriteInfo("Please select mode");
		}
		return 0;
	}

	public int CheckRequiredFeatureOptions(){
		List<WindowsFeature> missedComponents = _windowsFeatureManager.GetMissedComponents();
		IEnumerable<WindowsFeature> requiredComponentStates = _windowsFeatureManager.GetRequiredComponent();
		_logger.WriteInfo(
			"For detailed information visit: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components");
		_logger.WriteInfo($"{Environment.NewLine}Check started:");
		foreach (WindowsFeature item in requiredComponentStates) {
			_logger.WriteInfo($"{item}");
		}
		Console.WriteLine();
		if (missedComponents.Count > 0) {
			_logger.WriteError("Windows has missed components:");
			foreach (WindowsFeature item in missedComponents) {
				_logger.WriteInfo($"{item}");
			}
			return 1;
		} else {
			_logger.WriteInfo("All required components installed");
		}
		return 0;
	}

	public int InstallRequiredComponents(){
		try {
			_windowsFeatureManager.InstallMissingFeatures();
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception ex) {
			_logger.WriteError($"Installation failed: {ex.Message}");
			if (Program.IsDebugMode) {
				_logger.WriteDebug($"Exception details: {ex}");
			}
			return 1;
		}
	}

	public int UninstallRequiredComponents(){
		try {
			_windowsFeatureManager.UninstallMissingFeatures();
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception ex) {
			_logger.WriteError($"Uninstallation failed: {ex.Message}");
			if (Program.IsDebugMode) {
				_logger.WriteDebug($"Exception details: {ex}");
			}
			return 1;
		}
	}

	#endregion

}