namespace Clio.Tests.Command;

using System.Collections.Generic;
using Clio.Command;
using Clio.Command.SysSettingsCommand;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

[TestFixture(Author = "Kirill Krylov", Category = "YAML")]
public class ScenarioRunnerCommandTests
{
	private readonly ScenarioRunnerCommand _sut;
	private readonly IDeserializer _deserializer = new DeserializerBuilder().Build(); 
	private readonly List<object> _receivedOptions = new();
	public ScenarioRunnerCommandTests() {
		_sut  = new ScenarioRunnerCommand(_deserializer);
		Program.ExecuteCommandWithOption = (instance) => {
			_receivedOptions.Add(instance);
			return 0;
		};
	}
	
	[TestCase(@"YAML/Yaml-Samples/steps.yaml")]
	public void Clio_Executes_AllCommands_When_CommandsFoundInScenario(string fileName) {
		
		// Arrange
		ScenarioRunnerOptions options = new () {
			FileName = fileName
		};
		_receivedOptions.Clear();
		
		// Act
		_sut.Execute(options);
		
		// Assert
		_receivedOptions.Should().HaveCount(4);
	}
	
	[TestCase(@"YAML/Yaml-Samples/steps_incorrect_action.yaml")]
	public void Clio_Executes_ValidCommands_When_CommandsFoundInScenario(string fileName) {
		
		// Arrange
		ScenarioRunnerOptions options = new () {
			FileName = fileName
		};
		_receivedOptions.Clear();
		
		// Act
		_sut.Execute(options);
		
		// Assert
		_receivedOptions.Should().HaveCount(3);
	}

	[TestCase(@"YAML/Yaml-Samples/steps_with_macro.yaml")]
	public void Clio_Executes_Commands_WithMacro(string fileName) {
		
		// Arrange
		ScenarioRunnerOptions options = new () {
			FileName = fileName
		};
		_receivedOptions.Clear();
		
		// Act
		_sut.Execute(options);
		
		// Assert
		_receivedOptions.Should().HaveCount(1);
		_receivedOptions[0].Should().BeOfType<RestartOptions>();
		(_receivedOptions[0] as RestartOptions)?.Environment.Should().Be("digitalads");
	}
	
	[Test]
	public void Clio_Executes_MultipleCommands_WithMacro() {
		
		// Arrange
		const string fileName = @"YAML/Yaml-Samples/mutiple_steps_with_macro.yaml";
		ScenarioRunnerOptions options = new () {
			FileName = fileName
		};
		_receivedOptions.Clear();
		
		// Act
		_sut.Execute(options);
		
		// Assert
		_receivedOptions.Should().HaveCount(3);
		_receivedOptions[0].Should().BeOfType<RestartOptions>();
		var restartOptions = _receivedOptions[0] as RestartOptions;
		restartOptions.Environment.Should().Be("digitalads");
		
		_receivedOptions[1].Should().BeOfType<ClearRedisOptions>();
		var clearRedisOptions = _receivedOptions[1] as ClearRedisOptions; 
		clearRedisOptions.Environment.Should().Be("digitalads");
		
		_receivedOptions[2].Should().BeOfType<SysSettingsOptions>();
		var sysSettingsOptions = _receivedOptions[2] as SysSettingsOptions;
		sysSettingsOptions.Environment.Should().Be("digitalads");
		sysSettingsOptions.Code.Should().Be("Publisher");
		sysSettingsOptions.Value.Should().Be("Terrasoft");
		sysSettingsOptions.Type.Should().Be("Text");
	}
	
	[Test]
	public void Executes_Handles_EmptyFile() {
		
		// Arrange
		const string fileName = @"YAML/Yaml-Samples/emptyFile.yaml";
		ScenarioRunnerOptions options = new () {
			FileName = fileName
		};
		_receivedOptions.Clear();
		
		// Act
		_sut.Execute(options);
		
		//Assert
		_receivedOptions.Should().BeEmpty();
	}
	
	[Test]
	public void Executes_Handles_NonExistantFile() {
		
		// Arrange
		const string fileName = @"YAML/Yaml-Samples/non_existant.yaml";
		ScenarioRunnerOptions options = new () {
			FileName = fileName
		};
		_receivedOptions.Clear();
		
		// Act
		_sut.Execute(options);
		
		//Assert
		_receivedOptions.Should().BeEmpty();
	}
	
}