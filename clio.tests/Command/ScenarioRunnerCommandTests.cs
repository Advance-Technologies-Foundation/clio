namespace Clio.Tests.Command;

using System.Collections.Generic;
using Clio.Command;
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
		Program.MyMap = (instance) => {
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

}