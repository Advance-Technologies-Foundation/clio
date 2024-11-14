using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using Clio.Common;
using Microsoft.Dism;

namespace Clio.Command;

public interface IWindowsFeatureManager
{
	void InstallFeature(string featureName);
	List<WindowsFeature> GetMissedComponents();

	IEnumerable<WindowsFeature> GerRequiredComponent();

	void UninstallFeature(string featureName);

	void InstallMissingFeatures();

	void UnInstallMissingFeatures();

	int GetActionMaxLength(IEnumerable<string> items);

}

public class WindowsFeatureManager : IWindowsFeatureManager
{

	public WindowsFeatureManager(IWorkingDirectoriesProvider workingDirectoriesProvider,
		ConsoleProgressbar consoleProgressBar, IWindowsFeatureProvider windowsFeatureProvider, ILogger logger) {
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_consoleProgressBar = consoleProgressBar;
		_windowsFeatureProvider = windowsFeatureProvider;
		_logger = logger;
	}


	private string RequirmentNETFrameworkFeaturesFilePaths {
		get {
			return Path.Join(_workingDirectoriesProvider.TemplateDirectory, "windows_features", "RequirmentNetFramework.txt");
		}
	}

	

	private string GetInactiveFeaturesCode(string featureName) {
		var windowsFeatures = _windowsFeatureProvider.GetWindowsFeatures();
		var feature = windowsFeatures.FirstOrDefault(i => i.Name?.ToLower() == featureName.ToLower() ||
			i.Caption?.ToLower() == featureName.ToLower());
		if (feature is null) {
			throw new ItemNotFoundException($"Windows feature [{featureName}] not found in the System");
		}
		return feature.Name;
	}

	private IEnumerable<string> _requirmentNETFrameworkFeatures;

	public IEnumerable<string> RequirmentNETFrameworkFeatures
	{
		get
		{
			if (_requirmentNETFrameworkFeatures == null) {
				_requirmentNETFrameworkFeatures = File.ReadAllLines(RequirmentNETFrameworkFeaturesFilePaths);
			}
			return _requirmentNETFrameworkFeatures;
		}
		set
		{
			_requirmentNETFrameworkFeatures = value;
		}
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

	public void InstallMissingFeatures(){
		List<WindowsFeature> missedComponents = GetMissedComponents();
		if (missedComponents.Count > 0) {
			int maxLengthComponentName = GetActionMaxLength(missedComponents.Select(s => s.Name));
			_consoleProgressBar.MaxActionNameLength = maxLengthComponentName;
			_logger.WriteInfo($"Found {missedComponents.Count} missed components");
			foreach (WindowsFeature item in missedComponents) {
				InstallFeature(item.Name);
			}
		} else {
			_logger.WriteInfo("All requirment components installed");
		}
	}

	public void UnInstallMissingFeatures(){
		IEnumerable<WindowsFeature> requirmentsFeature = GerRequiredComponent();
		int maxLengthComponentName = GetActionMaxLength(requirmentsFeature.Select(s => s.Name));
		_consoleProgressBar.MaxActionNameLength = maxLengthComponentName;
		foreach (WindowsFeature feature in requirmentsFeature) {
			UninstallFeature(feature.Name);
		}
	}

	public int GetActionMaxLength(IEnumerable<string> action) => action.Max(s=> s.Length);
	
	
	private void SetFeatureState(string featureName, bool state) {
		try {
			var featureCode = GetInactiveFeaturesCode(featureName);
			DismApi.Initialize(DismLogLevel.LogErrorsWarningsInfo);
			using var session = DismApi.OpenOnlineSession();
			var (left, top) = Console.GetCursorPosition();
			if (state) {
				DismApi.EnableFeature(session, featureCode, false, true, null, progress => {
					Console.SetCursorPosition(left, top);
					Console.Write(_consoleProgressBar.GetBuatifyProgress("+ " + featureCode, progress.Current, progress.Total) + " ");
				});
			} else {
				DismApi.DisableFeature(session, featureCode, null, true, progress => {
					Console.SetCursorPosition(left, top);
				Console.Write(_consoleProgressBar.GetBuatifyProgress("- " + featureCode, progress.Current, progress.Total) + " ");
				});
			}
			_logger.WriteLine();
		}  finally {
			DismApi.Shutdown();
		}
	}

	private IEnumerable<string> _windowsActiveFeatures;
	private IEnumerable<string> windowsActiveFeatures {
		get {
			if (_windowsActiveFeatures == null) {
				_windowsActiveFeatures = _windowsFeatureProvider.GetActiveWindowsFeatures();
			}
			return _windowsActiveFeatures;
		}
	}

	IWorkingDirectoriesProvider _workingDirectoriesProvider;
	ConsoleProgressbar _consoleProgressBar;
	private IWindowsFeatureProvider _windowsFeatureProvider;
	private readonly ILogger _logger;
}