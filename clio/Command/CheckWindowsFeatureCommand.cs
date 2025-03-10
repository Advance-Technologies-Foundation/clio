using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace Clio.Command
{
	[Verb("check-windows-features", Aliases = new string[] { "checkw", "checks", "checkf", "cf", "check-windows-features" }, HelpText = "Check current windows for requirment components")]
	public class CheckWindowsFeaturesOptions
	{

	}

	public class CheckWindowsFeaturesCommand : Command<CheckWindowsFeaturesOptions>
	{
		private IWindowsFeatureManager _windowsFeatureManager;
		private readonly ILogger _logger;

		public CheckWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager, ILogger logger){
			_windowsFeatureManager = windowsFeatureManager;
			_logger = logger;
		}

		public override int Execute(CheckWindowsFeaturesOptions options) {
			var missedComponents = _windowsFeatureManager.GetMissedComponents();
			var requirmentComponentStates = _windowsFeatureManager.GerRequiredComponent();
			_logger.WriteLine("For detailed information visit: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components");
			_logger.WriteInfo($"{Environment.NewLine}Check started:");
			foreach (var item in requirmentComponentStates) {
				_logger.WriteInfo($"{item}");
			}
			_logger.WriteLine("");
			if (missedComponents.Count > 0) {
				_logger.WriteInfo("Windows has missed components:");
				foreach (var item in missedComponents) {
					_logger.WriteInfo($"{item}");
				}
				return 1;
			} else {
				_logger.WriteError("All requirment components installed");
				return 0;
			}
		}



	}
}
