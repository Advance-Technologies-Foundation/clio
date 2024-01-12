using Clio.Command;
using CommandLine;
using System;
using System.Linq;

namespace Clio
{
	[Verb("manage-windows-features", Aliases = new string[] { "mwf", "mng-win-features" }, HelpText = "Install windows features required for Creatio")]

	internal class InstallWindowsFeaturesOptions
	{
		[Option('c', "Check", Required = false, HelpText = "Check required feature states")]
		public bool CheckMode {
			get; set;
		}

		[Option('i', "Install", Required = false, HelpText = "Install required features")]
		public bool InstallMode {
			get; set;
		}

		[Option('u', "Uninstall", Required = false, HelpText = "Uninstall required features")]
		public bool UnistallMode {
			get; set;
		}

	}

	internal class ManageWindowsFeaturesCommand : Command<InstallWindowsFeaturesOptions> {

		private IWindowsFeatureManager _windowsFeatureManager;

		public ManageWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager) {
			_windowsFeatureManager = windowsFeatureManager;
		}

		public override int Execute(InstallWindowsFeaturesOptions options) {
			if (options.CheckMode) {
				CheckRequirmentFeatureOptions();
			} else if (options.InstallMode) {
				InstallRequirmentComponents();
			} else if (options.UnistallMode) {
				UnistallRequirmentComponents();
			} else {
				Console.WriteLine("Please select mode");
			}					
			return 0;
		}

		public void CheckRequirmentFeatureOptions() {
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
			} else {
				Console.WriteLine("All requirment components installed");
			}
		}

		public void InstallRequirmentComponents() {
			var missedComponents = _windowsFeatureManager.GetMissedComponents();
			if (missedComponents.Count > 0) {
				Console.WriteLine($"Found {missedComponents.Count} missed components");
				foreach (var item in missedComponents) {
					Console.WriteLine($"Installed component {item.Name}");
					_windowsFeatureManager.InstallFeature(item.Name);
				}
				Console.WriteLine("Done");
			} else {
				Console.WriteLine("All requirment components installed");
			}
		}

		public void UnistallRequirmentComponents() {
			var requirmentsFeature = _windowsFeatureManager.GerRequiredComponent();
			foreach (var feature in requirmentsFeature) {
				Console.WriteLine($"Unistall component: {feature.Name}");
				_windowsFeatureManager.UninstallFeature(feature.Name);
			}
			Console.WriteLine("Done");
		}
	}
}