using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.YAML;
using CommandLine;


namespace Clio.Command;

[Verb("run", Aliases = ["scenario","run-scenario"], HelpText = "Run scenario")]
public class ScenarioRunnerOptions : EnvironmentOptions{
    [Option("file-name", Required = true, HelpText = "Scenario file name")]
    public string FileName { get; set; }
}

public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions>{
    private readonly ILogger _logger;
    private readonly IScenario _scenario;
    private readonly ISettingsRepository _settingsRepository;

    public ScenarioRunnerCommand(IScenario scenario, ILogger logger, ISettingsRepository settingsRepository) {
        _scenario = scenario;
        _logger = logger;
        _settingsRepository = settingsRepository;
    }

    public override int Execute(ScenarioRunnerOptions options) {
        int result = 0;
        _logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Scenario started");

        _scenario
            .InitScript(options.FileName)
            .GetSteps(GetType().Assembly.GetTypes())
            .ToList()
            .ForEach(step => {
                _logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Starting step: {step.StepDescription}");
                if (step.CommandOption is EnvironmentOptions stepOptions and not RegAppOptions) {
                    if (!string.IsNullOrWhiteSpace(stepOptions.Environment)) {
                        EnvironmentSettings settings = _settingsRepository.FindEnvironment(stepOptions.Environment);
                        IServiceProvider container = new BindingsModule().Register(settings);
                        Program.Container = container;
                    }
                    else if (!string.IsNullOrWhiteSpace(options.Environment)) {
                        stepOptions.Environment = options.Environment;
                        EnvironmentSettings settings = _settingsRepository.FindEnvironment(options.Environment);
                        IServiceProvider container = new BindingsModule().Register(settings);
                        Program.Container = container;
                    }
                    else {
                        EnvironmentSettings settings = _settingsRepository.FindEnvironment(options.Environment);
                        IServiceProvider container = new BindingsModule().Register(settings);
                        Program.Container = container;
                    }
                }

                result += Program.ExecuteCommandWithOption(step.CommandOption);
                _logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Finished step: {step.StepDescription}");
                _logger.WriteLine();
            });
        _logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Scenario finished");
        return result >= 1 ? 1 : 0;
    }
}


