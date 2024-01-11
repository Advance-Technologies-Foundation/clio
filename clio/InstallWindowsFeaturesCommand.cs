using Clio.Command;
using CommandLine;
using System;
using System.Linq;

namespace Clio
{
	[Verb("install-windows-features", Aliases = new string[] { "iwf", "inst-win-features" }, HelpText = "Install windows features required for Creatio")]

	internal class InstallWindowsFeaturesOptions
	{
	}

	internal class InstallWindowsFeaturesCommand : Command<InstallWindowsFeaturesOptions>
	{

		private IWindowsFeatureManager _windowsFeatureManager;

		public InstallWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager) {
			_windowsFeatureManager = windowsFeatureManager;
		}

		public override int Execute(InstallWindowsFeaturesOptions options) {
			//var anyFeature = _windowsFeatureManager.GerRequiredComponent().First();
			//_windowsFeatureManager.UninstallFeature(anyFeature.Name);
			//return 0;
			var missedComponents = _windowsFeatureManager.GetMissedComponents();
			if (missedComponents.Count > 0) {
				Console.WriteLine($"Found {missedComponents.Count} missed components");
				foreach(var item in missedComponents) {
					Console.WriteLine($"Installed component {item.Name}");
					_windowsFeatureManager.InstallFeature(item.Name);
				}	
			} else {
				Console.WriteLine("All requirment components installed");
			}
			return 0;
		}
	}
}