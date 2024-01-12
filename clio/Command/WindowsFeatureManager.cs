using Castle.Components.DictionaryAdapter;
using Clio.Common;
using Microsoft.Dism;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

public interface IWindowsFeatureManager
{
	void InstallFeature(string featureName);
	List<WindowsFeature> GetMissedComponents();

	IEnumerable<WindowsFeature> GerRequiredComponent();

	void UninstallFeature(string featureName);

}

public class WindowsFeatureManager : IWindowsFeatureManager
{

	private string RequirmentNETFrameworkFeaturesFilePaths {
		get {
			return Path.Join(_workingDirectoriesProvider.TemplateDirectory, "windows_features", "RequirmentNetFramework.txt");
		}
	}

	

	private string GetInactiveFeaturesCode(string featureName) {
		var windowsFeatures = GetWindowsFeatures();
		var feature = windowsFeatures.FirstOrDefault(i => i.Name.ToLower() == featureName.ToLower() ||
				i.Caption.ToLower() == featureName.ToLower());
		return feature.Name;
	}

	private List<string> RequirmentNETFrameworkFeatures {
		get { return File.ReadAllLines(RequirmentNETFrameworkFeaturesFilePaths).ToList(); }
	}

	public List<WindowsFeature> GetMissedComponents() {
		var missedComponents = new List<WindowsFeature>();
		foreach (var item in RequirmentNETFrameworkFeatures) {
			if (!windowsActiveFeatures.Select(i => i.ToLower()).Contains(item.ToLower())) {
				missedComponents.Add(new WindowsFeature() {
					Name = item,
					Installed = false
				});
			}
		}
		return missedComponents;
	}

	public IEnumerable<WindowsFeature> GerRequiredComponent() {
		var requirmentFeatures = new List<WindowsFeature>();
		foreach (var item in RequirmentNETFrameworkFeatures) {
			requirmentFeatures.Add(new WindowsFeature() {
				Name = item,
				Installed = windowsActiveFeatures.Select(i => i.ToLower()).Contains(item.ToLower())
			});
		}
		return requirmentFeatures;
	}

	public void InstallFeature(string featureName) {
		SetFeatureState(featureName, true);
	}

	public void UninstallFeature(string featureName) {
		SetFeatureState(featureName, false);
	}

	private void SetFeatureState(string featureName, bool state) {
		DismApi.Initialize(DismLogLevel.LogErrorsWarningsInfo);
		try {
			var featureCode = GetInactiveFeaturesCode(featureName);
			using var session = DismApi.OpenOnlineSession();
			var (left, top) = Console.GetCursorPosition();
			if (state) {
				DismApi.EnableFeature(session, featureCode, false, true, null, progress => {
					Console.SetCursorPosition(left, top);
					Console.Write($"{progress.Total} / {progress.Current} ");
				});
			} else {
				DismApi.DisableFeature(session, featureCode, null, true, progress => {
					Console.SetCursorPosition(left, top);
					Console.Write($"{progress.Total} / {progress.Current} ");
				});
			}
			Console.WriteLine();
		} catch (Exception e) {
			Console.WriteLine(e.Message);
		} finally {
			DismApi.Shutdown();
		}
	}

	private List<string> _windowsActiveFeatures;
	private List<string> windowsActiveFeatures {
		get {
			if (_windowsActiveFeatures == null) {
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
				_windowsActiveFeatures = features;
			}
			return _windowsActiveFeatures;
		}
	}

	private List<WindowsFeature> GetWindowsFeatures() {
		var features = new List<WindowsFeature>();
		ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OptionalFeature");
		ManagementObjectCollection featureCollection = searcher.Get();
		foreach (ManagementObject featureObject in featureCollection) {
			features.Add(new WindowsFeature() {
				Name = featureObject["Name"].ToString(),
				Caption = featureObject["Caption"].ToString()
			});
		}
		return features;
	}

	IWorkingDirectoriesProvider _workingDirectoriesProvider;

	public WindowsFeatureManager(IWorkingDirectoriesProvider workingDirectoriesProvider) {
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}
}