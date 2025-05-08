using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("check-windows-features", Aliases = new[] { "checkw", "checks", "checkf", "cf", "check-windows-features" },
    HelpText = "Check current windows for requirment components")]
public class CheckWindowsFeaturesOptions
{
}

public class CheckWindowsFeaturesCommand(IWindowsFeatureManager windowsFeatureManager, ILogger logger)
    : Command<CheckWindowsFeaturesOptions>
{
    private readonly ILogger _logger = logger;
    private readonly IWindowsFeatureManager _windowsFeatureManager = windowsFeatureManager;

    public override int Execute(CheckWindowsFeaturesOptions options)
    {
        List<WindowsFeature> missedComponents = _windowsFeatureManager.GetMissedComponents();
        IEnumerable<WindowsFeature> requirmentComponentStates = _windowsFeatureManager.GerRequiredComponent();
        _logger.WriteLine(
            "For detailed information visit: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components");
        _logger.WriteInfo($"{Environment.NewLine}Check started:");
        foreach (WindowsFeature item in requirmentComponentStates)
        {
            _logger.WriteInfo($"{item}");
        }

        _logger.WriteLine(string.Empty);
        if (missedComponents.Count > 0)
        {
            _logger.WriteInfo("Windows has missed components:");
            foreach (WindowsFeature item in missedComponents)
            {
                _logger.WriteInfo($"{item}");
            }

            return 1;
        }

        _logger.WriteError("All requirment components installed");
        return 0;
    }
}
