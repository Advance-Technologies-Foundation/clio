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
		private readonly IDeserializer _deserializer;
		public ScenarioRunnerCommand(IDeserializer deserializer) {
			_deserializer = deserializer;
		}
		public override int Execute(ScenarioRunnerOptions options) {
			return Scenario.CreateScenarioFromFile(options.FileName, _deserializer).Value switch {
				None => ReportMissingFile(options),
				Scenario{Sections: null} => ReportEmptyFile(options),
				Scenario{Sections: not null} v => ExecuteScenario(v),
				_ => ReportUnknownError()
			};
		}
		
		private static readonly Func<ScenarioRunnerOptions ,int> ReportMissingFile = (options)=> {
			Console.WriteLine($"Scenario file {options.FileName} not found");
			return 1;
		};
		private static readonly Func<ScenarioRunnerOptions ,int> ReportEmptyFile = (options)=> {
			Console.WriteLine($"Scenario file {options.FileName} is empty");
			return 1;
		};
		private static readonly Func<int> ReportUnknownError = ()=> {
			Console.WriteLine("Unknown error");
			return 1;
		};
		private static readonly Func<Scenario, int> ExecuteScenario = (scenario) => {
			IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
			IReadOnlyDictionary<string, object> secrets = Scenario.ParseSecrets(scenario.Sections);
			IReadOnlyDictionary<string, object> settings = Scenario.ParseSettings(scenario.Sections);
			
			int result = 0;
			steps.ToList().ForEach(step=> {
				
				OneOf<Type, None> maybeType = 
					Step.FindOptionTypeByName(Assembly.GetExecutingAssembly().GetTypes(), step.Action);
				
				OneOf<None, object> maybeActivatedOption = 
					Step.ActivateOptions(maybeType, step.Options, settings, secrets);
				
				result += (maybeActivatedOption.Value is not None) ?
					Program.ExecuteCommandWithOption(maybeActivatedOption.Value): 0;
			});
			return result >=1 ? 1: 0;
		};
	}
}