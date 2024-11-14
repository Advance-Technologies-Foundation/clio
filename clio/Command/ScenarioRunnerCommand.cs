using System.IO.Abstractions;
using Autofac;

namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using Clio.YAML;
	using CommandLine;
	using OneOf;
	using OneOf.Types;
	using YamlDotNet.Serialization;

	[Verb("run", Aliases = new[] { "run-scenario" }, HelpText = "Run scenario")]
	public class ScenarioRunnerOptions : EnvironmentOptions {
		[Option("file-name", Required = true, HelpText = "Scenario file name")]
		public string FileName {
			get; set;
		}
	}
	
	
	public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions> {
		
		internal IFileSystem FileSystem {get;set;}
		
		private readonly IScenario _scenario;
		private readonly Common.ILogger _logger;

		public ScenarioRunnerCommand(IScenario scenario, Common.ILogger logger) {
			_scenario = scenario;
			_logger = logger;
		}
		public override int Execute(ScenarioRunnerOptions options) {
			int result = 0;
			_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Scenario started");
			_scenario
				.InitScript(options.FileName)
				.GetSteps( GetType().Assembly.GetTypes())
				.ToList().ForEach(step=> {
					_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Starting step: {step.Item2}");
					if(step.CommandOption is EnvironmentOptions stepOptions && step.CommandOption is not RegAppOptions) {
						if(!string.IsNullOrWhiteSpace(stepOptions.Environment)){
							SettingsRepository settingsRepository = new (FileSystem);
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
			return result >=1 ? 1: 0;
		}
	}
}