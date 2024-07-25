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
		public ScenarioRunnerCommand(IScenario scenario) {
			_scenario = scenario;
		}
		public override int Execute(ScenarioRunnerOptions options) {
			int result = 0;
			Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] Scenario started");
			_scenario
				.InitScript(options.FileName)
				.GetSteps( GetType().Assembly.GetTypes())
				.ToList().ForEach(step=> {
					Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] Starting step: {step.Item2}");
					
					// Create new DI container for each step
					// because we don't know that the scenario steps are all executed
					// on the same env. Further more if the environment is set in the yaml file
					// then we would execute all command on default environment
					if(step.CommandOption is EnvironmentOptions stepOptions) {
						if(!string.IsNullOrWhiteSpace(stepOptions.Environment)){
							SettingsRepository settingsRepository = new (FileSystem); //FileSystem is for tests
							EnvironmentSettings settings = settingsRepository.FindEnvironment(stepOptions.Environment);
							IContainer container = new BindingsModule().Register(settings);
							Program.Container = container;
						}
					}
					
					result += Program.ExecuteCommandWithOption(step.CommandOption);
					Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] Finished step: {step.StepDescription}");
					Console.WriteLine();
				});
			Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] Scenario finished");
			return result >=1 ? 1: 0;
		}
	}
}