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

		public CheckWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager) {
			_windowsFeatureManager = windowsFeatureManager;
		}

		public override int Execute(CheckWindowsFeaturesOptions options) {
			var missedComponents = _windowsFeatureManager.GetMissedComponents();
			var requirmentComponentStates = _windowsFeatureManager.GerRequiredComponent();
			Console.WriteLine("For detailed information visit: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components");
			Console.WriteLine($"{Environment.NewLine}Check started:");
			foreach (var item in requirmentComponentStates) {
				Console.WriteLine($"{item}");
			}
			Console.WriteLine();
			if (missedComponents.Count > 0) {
				Console.WriteLine("Windows has missed components:");
				foreach (var item in missedComponents) {
					Console.WriteLine($"{item}");
				}
				return 1;
			} else {
				Console.WriteLine("All requirment components installed");
				return 0;
			}
		}



	}
}
