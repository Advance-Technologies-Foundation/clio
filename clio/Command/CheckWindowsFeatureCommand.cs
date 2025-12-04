#region

using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;

#endregion

namespace Clio.Command;

[Verb("check-windows-features", Aliases = ["checkw", "checks", "checkf", "cf", "check-windows-features"]
	, HelpText = "Check windows for the required components")]
public class CheckWindowsFeaturesOptions{ }

public class CheckWindowsFeaturesCommand : Command<CheckWindowsFeaturesOptions>{
	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IWindowsFeatureManager _windowsFeatureManager;

	#endregion

	#region Constructors: Public

	public CheckWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager, ILogger logger) {
		_windowsFeatureManager = windowsFeatureManager;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(CheckWindowsFeaturesOptions options) {
		if (!OperationSystem.Current.IsWindows) {
			_logger.WriteError("This command is only available on Windows operating system.");
			return 1;
		}

		List<WindowsFeature> missedComponents = _windowsFeatureManager.GetMissedComponents();
		IEnumerable<WindowsFeature> requiredComponentStates = _windowsFeatureManager.GetRequiredComponent();
		_logger.WriteLine(
			"For detailed information visit: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components");
		_logger.WriteInfo($"{Environment.NewLine}Check started:");
		foreach (WindowsFeature item in requiredComponentStates) {
			_logger.WriteInfo($"{item}");
		}

		_logger.WriteLine("");
		if (missedComponents.Count > 0) {
			_logger.WriteError("Windows has missed components:");
			foreach (WindowsFeature item in missedComponents) {
				_logger.WriteInfo($"{item}");
			}

			return 1;
		}

		_logger.WriteInfo("All requirement components installed");
		return 0;
	}

	#endregion
}
