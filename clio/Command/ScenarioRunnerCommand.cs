using System;
using System.Linq;
using Autofac;
using Clio.Common;
using Clio.YAML;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

[Verb("run", Aliases = new[]
{
    "run-scenario"
}, HelpText = "Run scenario")]
public class ScenarioRunnerOptions : EnvironmentOptions
{

    #region Properties: Public

    [Option("file-name", Required = true, HelpText = "Scenario file name")]
    public string FileName { get; set; }

    #endregion

}

public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions>
{

    #region Fields: Private

    private readonly IScenario _scenario;
    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public ScenarioRunnerCommand(IScenario scenario, ILogger logger)
    {
        _scenario = scenario;
        _logger = logger;
    }

    #endregion

    #region Properties: Internal

    internal IFileSystem FileSystem { get; set; }

    #endregion

    #region Methods: Public

    public override int Execute(ScenarioRunnerOptions options)
    {
        int result = 0;
        _logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Scenario started");
        _scenario
            .InitScript(options.FileName)
            .GetSteps(GetType().Assembly.GetTypes())
            .ToList().ForEach(step =>
            {
                _logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Starting step: {step.Item2}");
                if (step.CommandOption is EnvironmentOptions stepOptions && step.CommandOption is not RegAppOptions)
                {
                    if (!string.IsNullOrWhiteSpace(stepOptions.Environment))
                    {
                        SettingsRepository settingsRepository = new(FileSystem);
                        EnvironmentSettings settings = settingsRepository.FindEnvironment(stepOptions.Environment);
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

    #endregion

}
