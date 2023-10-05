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

		public CheckWindowsFeaturesCommand(IWorkingDirectoriesProvider workingDirectoriesProvider) {
			WorkingDirectoriesProvider = workingDirectoriesProvider;
		}

		private string RequirmentNETFrameworkFeaturesFilePaths {
			get {
				return Path.Join(WorkingDirectoriesProvider.TemplateDirectory, "windows_features", "RequirmentNetFramework.txt");
			}
		}

		public override int Execute(CheckWindowsFeaturesOptions options) {
			var missedComponents = new List<string>();
			Console.WriteLine("For detailed information go to: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components");
			Console.WriteLine("Check started:");
			foreach (var item in RequirmentNETFrameworkFeatures) {
				if (!windowsActiveFeatures.Select(i => i.ToLower()).Contains(item.ToLower())) { 
					missedComponents.Add(item);
					Console.WriteLine($"NOT INSTALLED: {item}");
				} else {
					Console.WriteLine($"OK: {item}");
				}
			}
			if (missedComponents.Count > 0) {
				Console.WriteLine("Windows has missed components:");
				foreach (var item in missedComponents) {
					Console.WriteLine($"NOT INSTALLED: {item}");
				}
				return 1;
			} else {
				Console.WriteLine("All requirment components installed");
				return 0;
			}
		}


		private List<string> RequirmentNETFrameworkFeatures {
			get { return File.ReadAllLines(RequirmentNETFrameworkFeaturesFilePaths).ToList(); }
		}


		private List<string> windowsActiveFeatures {
			get {
				var features = new List<string>();
				try {
					ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OptionalFeature WHERE InstallState = 1");
					ManagementObjectCollection featureCollection = searcher.Get();
					foreach (ManagementObject featureObject in featureCollection) {
						string featureName = featureObject["Name"].ToString();
						features.Add(featureName);
						string featureCaption = featureObject["Caption"].ToString();
						features.Add(featureCaption);
					}
				} catch (Exception e) {
				}
				return features;
			}
		}

		public IWorkingDirectoriesProvider WorkingDirectoriesProvider { get; }
	}
}
