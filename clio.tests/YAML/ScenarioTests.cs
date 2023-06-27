namespace Clio.Tests.YAML;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.YAML;
using FluentAssertions;
using NUnit.Framework;
using OneOf.Types;
using YamlDotNet.Serialization;

[TestFixture, Author("Kirill Krylov")]
[Category("YAML")]
public class ScenarioTests
{
	private readonly IDeserializer _deserializer;
	private readonly Type[] _allAvailableTypes;
	public ScenarioTests() {
		_deserializer = new DeserializerBuilder().Build();
		
		Program marker = new Program();
		_allAvailableTypes = marker.GetType().Assembly.GetTypes();
	}
	
	[Test]
	public void CreateScenarioFromFile_Returns_SectionsAndSteps_When_FileExists() {
		//Arrange
		const string sampleFile = @"YAML/Yaml-Samples/steps.yaml";

		// Act
		Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
		IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
		
		// Assert
		scenario.Sections.Should().HaveCount(1);
		steps.Should().HaveCount(4);
	}
	
	[Test]
	public void CreateScenarioFromFile_Returns_InitializedSteps_When_FileExists() {
		//Arrange
		const string sampleFile = @"YAML/Yaml-Samples/steps.yaml";

		// Act
		Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
		IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
		IReadOnlyDictionary<string, object> secrets = Scenario.ParseSecrets(scenario.Sections);
		IReadOnlyDictionary<string, object> config = Scenario.ParseSettings(scenario.Sections);
		
		// Assert
		steps.ToList().ForEach(step=> {
			var stepType = Step.FindOptionTypeByName(_allAvailableTypes, step.Action);
			var activeOption = Step.ActivateOptions(stepType.Value as Type, step.Options, config, secrets);
			
			object _ = step.Action switch {
				"restart" => activeOption.Value.Should().BeOfType<RestartOptions>(),
				"flushdb" => activeOption.Value.Should().BeOfType<ClearRedisOptions>(),
				"ver" => activeOption.Value.Should().BeOfType<GetVersionOptions>(),
				"download" => activeOption.Value.Should().BeOfType<PullPkgOptions>(),
				_ => throw new ArgumentOutOfRangeException(nameof(step.Action), step.Action, "Unknown action")
			};
		});
	}
	
	[Test]
	public void CreateScenarioFromFile_Returns_InitializedSteps_When_ActionIsIncorrect() {
		//Arrange
		const string sampleFile = @"YAML/Yaml-Samples/steps_incorrect_action.yaml";

		// Act
		Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
		IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
		IReadOnlyDictionary<string, object> secrets = Scenario.ParseSecrets(scenario.Sections);
		IReadOnlyDictionary<string, object> config = Scenario.ParseSettings(scenario.Sections);
		//Assert
		scenario.Sections.Should().HaveCount(1);
		steps.Should().HaveCount(4);
		
		steps.ToList().ForEach(step=> {
			var stepType = Step.FindOptionTypeByName(_allAvailableTypes, step.Action);
			var activeOption = Step.ActivateOptions(stepType, step.Options,config, secrets);
			
			object _ = step.Action switch {
				"aaa" => activeOption.Value.Should().BeOfType<None>(),
				"flushdb" => activeOption.Value.Should().BeOfType<ClearRedisOptions>(),
				"ver" => activeOption.Value.Should().BeOfType<GetVersionOptions>(),
				"download" => activeOption.Value.Should().BeOfType<PullPkgOptions>(),
				_ => throw new ArgumentOutOfRangeException(nameof(step.Action), step.Action, "Unknown action")
			};
		});
	}
	
	
	[Test]
	public void CreateScenarioFromFile_Returns_ThreeSections() {
		//Arrange
		const string sampleFile = @"YAML/Yaml-Samples/3sections.yaml";

		// Act
		Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
		IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
		
		// Assert
		scenario.Sections.Should().HaveCount(3);
		scenario.Sections.ContainsKey("steps").Should().BeTrue();
		scenario.Sections.ContainsKey("secrets").Should().BeTrue();
		scenario.Sections.ContainsKey("config").Should().BeTrue();
	}
	
	[Test]
	public void CreateScenarioFromFile_Returns_Secrets_When_FileContainsSecrets() {
		//Arrange
		const string sampleFile = @"YAML/Yaml-Samples/secrets.yaml";

		// Act
		Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
		var secrets = Scenario.ParseSecrets(scenario.Sections);
		
		// Assert
		scenario.Sections.Should().HaveCount(1);
		scenario.Sections.ContainsKey("secrets").Should().BeTrue();
	}
	
	[Test]
	public void ConvertDictionary_Returns_Converted() {
		
		//Arrange
		var input = new Dictionary<object, object>() {
			{"key1","obj1"},
			{"key2","obj2"},
		};
		
		//Act
		var result = Scenario.ConvertDictionary(input);
		
		//Assert
		result.Should().HaveCount(2);
		result["key1"].Should().Be("obj1");
		result["key1"].Should().BeOfType<string>();
		
		result["key2"].Should().Be("obj2");
		result["key2"].Should().BeOfType<string>();
	}
	
	
	[Test]
	public void Steps_WithRefToOtherFile() {
		//Arrange
		const string sampleFile = @"YAML/Yaml-Samples/steps_with_values.yaml";

		// Act
		Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
		var steps = Scenario.ParseSteps(scenario.Sections);
		
		// Assert
		steps.Should().HaveCount(4);
		
	}
	
}