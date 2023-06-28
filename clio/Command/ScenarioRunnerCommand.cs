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
			_scenario
				.InitScript(options.FileName)
				.GetSteps( GetType().Assembly.GetTypes())
				.ToList().ForEach(step=> {
					result += Program.ExecuteCommandWithOption(step);
			});
			return result >=1 ? 1: 0;
		}
	}
}