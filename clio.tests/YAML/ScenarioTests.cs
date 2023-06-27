namespace Clio.Tests.YAML;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.YAML;
using FluentAssertions;
using NUnit.Framework;
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
		Scenario scenario = Scenario.CreateScenarioFromFile(sampleFile, _deserializer);
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
		Scenario scenario = Scenario.CreateScenarioFromFile(sampleFile, _deserializer);
		IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
		
		// Assert
		steps.ToList().ForEach(step=> {
			var stepType = Step.FindOptionTypeByName(_allAvailableTypes, step.Action);
			var activeOption = Step.ActivateOptions(stepType.Value as Type, step.Options);
			
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
		Scenario scenario = Scenario.CreateScenarioFromFile(sampleFile, _deserializer);
		IReadOnlyList<Step> steps = Scenario.ParseSteps(scenario.Sections);
		
		//Assert
		scenario.Sections.Should().HaveCount(1);
		steps.Should().HaveCount(4);
		
		steps.ToList().ForEach(step=> {
			var stepType = Step.FindOptionTypeByName(_allAvailableTypes, step.Action);
			var activeOption = Step.ActivateOptions(stepType, step.Options);
			
			object _ = step.Action switch {
				"aaa" => activeOption.Value.Should().BeOfType<NotOption>(),
				"flushdb" => activeOption.Value.Should().BeOfType<ClearRedisOptions>(),
				"ver" => activeOption.Value.Should().BeOfType<GetVersionOptions>(),
				"download" => activeOption.Value.Should().BeOfType<PullPkgOptions>(),
				_ => throw new ArgumentOutOfRangeException(nameof(step.Action), step.Action, "Unknown action")
			};
		});
	}
	
}