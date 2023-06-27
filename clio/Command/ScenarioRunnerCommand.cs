namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using Clio.YAML;
	using CommandLine;
	using OneOf;
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
			Scenario scenario = Scenario.CreateScenarioFromFile(options.FileName, _deserializer);
			IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
			int result = 0;
			
			steps.ToList().ForEach(step=> {
				
				OneOf<Type, NotType> maybeType = 
					Step.FindOptionTypeByName(Assembly.GetExecutingAssembly().GetTypes(), step.Action);
				
				OneOf<NotOption, object> maybeActivatedOption = 
					Step.ActivateOptions(maybeType, step.Options);
				
				result += (maybeActivatedOption.Value is not NotOption) ?
					Program.MyMap(maybeActivatedOption.Value): 0;
				
			});
			
			return result >=1 ? 1: 0;
		}
	}
}