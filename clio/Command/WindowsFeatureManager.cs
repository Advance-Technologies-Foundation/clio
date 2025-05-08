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

    #region Methods: Public

    IEnumerable<WindowsFeature> GerRequiredComponent();

    int GetActionMaxLength(IEnumerable<string> items);

    List<WindowsFeature> GetMissedComponents();

    void InstallFeature(string featureName);

    void InstallMissingFeatures();

    void UninstallFeature(string featureName);

    void UnInstallMissingFeatures();

    #endregion

}

public class WindowsFeatureManager : IWindowsFeatureManager
{

    #region Fields: Private

    private IEnumerable<string> _requirmentNETFrameworkFeatures;
    private IEnumerable<string> _windowsActiveFeatures;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly ConsoleProgressbar _consoleProgressBar;
    private readonly IWindowsFeatureProvider _windowsFeatureProvider;
    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public WindowsFeatureManager(IWorkingDirectoriesProvider workingDirectoriesProvider,
        ConsoleProgressbar consoleProgressBar, IWindowsFeatureProvider windowsFeatureProvider, ILogger logger)
    {
        _workingDirectoriesProvider = workingDirectoriesProvider;
        _consoleProgressBar = consoleProgressBar;
        _windowsFeatureProvider = windowsFeatureProvider;
        _logger = logger;
    }

    #endregion

    #region Properties: Private

    private string RequirmentNETFrameworkFeaturesFilePaths
    {
        get
        {
            return Path.Join(_workingDirectoriesProvider.TemplateDirectory, "windows_features",
                "RequirmentNetFramework.txt");
        }
    }

    private IEnumerable<string> windowsActiveFeatures
    {
        get
        {
            if (_windowsActiveFeatures == null)
            {
                _windowsActiveFeatures = _windowsFeatureProvider.GetActiveWindowsFeatures();
            }
            return _windowsActiveFeatures;
        }
    }

    #endregion

    #region Properties: Public

    public IEnumerable<string> RequirmentNETFrameworkFeatures
    {
        get
        {
            if (_requirmentNETFrameworkFeatures == null)
            {
                _requirmentNETFrameworkFeatures = File.ReadAllLines(RequirmentNETFrameworkFeaturesFilePaths);
            }
            return _requirmentNETFrameworkFeatures;
        }
        set { _requirmentNETFrameworkFeatures = value; }
    }

    #endregion

    #region Methods: Private

    private string GetInactiveFeaturesCode(string featureName)
    {
        List<WindowsFeature> windowsFeatures = _windowsFeatureProvider.GetWindowsFeatures();
        WindowsFeature feature = windowsFeatures.FirstOrDefault(i => i.Name?.ToLower() == featureName.ToLower() ||
            i.Caption?.ToLower() == featureName.ToLower());
        if (feature is null)
        {
            throw new ItemNotFoundException($"Windows feature [{featureName}] not found in the System");
        }
        return feature.Name;
    }

    private void SetFeatureState(string featureName, bool state)
    {
        try
        {
            string featureCode = GetInactiveFeaturesCode(featureName);
            DismApi.Initialize(DismLogLevel.LogErrorsWarningsInfo);
            using DismSession session = DismApi.OpenOnlineSession();
            (int left, int top) = Console.GetCursorPosition();
            if (state)
            {
                DismApi.EnableFeature(session, featureCode, false, true, null, progress =>
                {
                    Console.SetCursorPosition(left, top);
                    Console.Write(
                        _consoleProgressBar.GetBuatifyProgress("+ " + featureCode, progress.Current, progress.Total) +
                        " ");
                });
            }
            else
            {
                DismApi.DisableFeature(session, featureCode, null, true, progress =>
                {
                    Console.SetCursorPosition(left, top);
                    Console.Write(
                        _consoleProgressBar.GetBuatifyProgress("- " + featureCode, progress.Current, progress.Total) +
                        " ");
                });
            }
            _logger.WriteLine();
        }
        finally
        {
            DismApi.Shutdown();
        }
    }

    #endregion

    #region Methods: Public

    public IEnumerable<WindowsFeature> GerRequiredComponent()
    {
        List<WindowsFeature> requirmentFeatures = new();
        foreach (string item in RequirmentNETFrameworkFeatures)
        {
            requirmentFeatures.Add(new WindowsFeature
            {
                Name = item, Installed = windowsActiveFeatures.Select(i => i.ToLower()).Contains(item.ToLower())
            });
        }
        return requirmentFeatures;
    }

    public int GetActionMaxLength(IEnumerable<string> action) => action.Max(s => s.Length);

    public List<WindowsFeature> GetMissedComponents()
    {
        List<WindowsFeature> missedComponents = new();
        foreach (string item in RequirmentNETFrameworkFeatures)
        {
            if (!windowsActiveFeatures.Select(i => i.ToLower()).Contains(item.ToLower()))
            {
                missedComponents.Add(new WindowsFeature
                {
                    Name = item, Installed = false
                });
            }
        }
        return missedComponents;
    }

    public void InstallFeature(string featureName)
    {
        SetFeatureState(featureName, true);
    }

    public void InstallMissingFeatures()
    {
        List<WindowsFeature> missedComponents = GetMissedComponents();
        if (missedComponents.Count > 0)
        {
            int maxLengthComponentName = GetActionMaxLength(missedComponents.Select(s => s.Name));
            _consoleProgressBar.MaxActionNameLength = maxLengthComponentName;
            _logger.WriteInfo($"Found {missedComponents.Count} missed components");
            foreach (WindowsFeature item in missedComponents)
            {
                InstallFeature(item.Name);
            }
        }
        else
        {
            _logger.WriteInfo("All requirment components installed");
        }
    }

    public void UninstallFeature(string featureName)
    {
        SetFeatureState(featureName, false);
    }

    public void UnInstallMissingFeatures()
    {
        IEnumerable<WindowsFeature> requirmentsFeature = GerRequiredComponent();
        int maxLengthComponentName = GetActionMaxLength(requirmentsFeature.Select(s => s.Name));
        _consoleProgressBar.MaxActionNameLength = maxLengthComponentName;
        foreach (WindowsFeature feature in requirmentsFeature)
        {
            UninstallFeature(feature.Name);
        }
    }

    #endregion

}
