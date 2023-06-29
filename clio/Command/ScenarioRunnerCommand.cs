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
					result += Program.ExecuteCommandWithOption(step.Item1);
					Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] Finished step: {step.Item2}");
					Console.WriteLine();
				});
			Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] Scenario finished");
			return result >=1 ? 1: 0;
		}
	}
}