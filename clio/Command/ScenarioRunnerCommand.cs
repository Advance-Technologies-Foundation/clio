using System;
using System.Linq;
using Autofac;
using Clio.Common;
using Clio.YAML;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;


namespace Clio.Command;

[Verb("run", Aliases = ["scenario","run-scenario"], HelpText = "Run scenario")]
public class ScenarioRunnerOptions : EnvironmentOptions{
    [Option("file-name", Required = true, HelpText = "Scenario file name")]
    public string FileName { get; set; }
}

public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions>{
    private readonly ILogger _logger;

    private readonly IScenario _scenario;

    public ScenarioRunnerCommand(IScenario scenario, ILogger logger) {
        _scenario = scenario;
        _logger = logger;
    }

    internal IFileSystem FileSystem { get; set; }

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
                        SettingsRepository settingsRepository = new(FileSystem);
                        EnvironmentSettings settings = settingsRepository.FindEnvironment(stepOptions.Environment);
                        IContainer container = new BindingsModule().Register(settings);
                        Program.Container = container;
                    }
                    else if (!string.IsNullOrWhiteSpace(options.Environment)) {
                        SettingsRepository settingsRepository = new(FileSystem);
                        stepOptions.Environment = options.Environment;
                        EnvironmentSettings settings = settingsRepository.FindEnvironment(options.Environment);
                        IContainer container = new BindingsModule().Register(settings);
                        Program.Container = container;
                    }
                    else {
                        SettingsRepository settingsRepository = new(FileSystem);
                        EnvironmentSettings settings = settingsRepository.FindEnvironment(options.Environment);
                        IContainer container = new BindingsModule().Register(settings);
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
