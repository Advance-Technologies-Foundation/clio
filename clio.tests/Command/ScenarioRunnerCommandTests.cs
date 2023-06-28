namespace Clio.Tests.Command;

using System.Collections.Generic;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.YAML;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

[TestFixture(Author = "Kirill Krylov", Category = "YAML")]
public class ScenarioRunnerCommandTests
{

	#region Fields: Private

	private readonly ScenarioRunnerCommand _sut;
	private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
	private readonly List<object> _receivedOptions = new();

	#endregion

	#region Constructors: Public

	public ScenarioRunnerCommandTests() {
		IScenario script = new Scenario(_deserializer);
		_sut = new ScenarioRunnerCommand(script);
		Program.ExecuteCommandWithOption = instance => {
			_receivedOptions.Add(instance);
			return 0;
		};
	}

	#endregion

	[Test]
	public void Executes_Handles_CorrectFile() {
		// Arrange
		const string fileName = @"YAML/Script/three_sections.yaml";
		ScenarioRunnerOptions options = new() {
			FileName = fileName
		};
		_receivedOptions.Clear();

		// Act
		_sut.Execute(options);

		//Assert
		_receivedOptions.Should().HaveCount(4);
		_receivedOptions[0].Should().BeOfType<RestartOptions>();
		RestartOptions restartOption = _receivedOptions[0] as RestartOptions;
		restartOption.Environment.Should().Be("digitalads");
		restartOption.Login.Should().Be("Supervisor");
		restartOption.Password.Should().Be("Supervisor");

		_receivedOptions[1].Should().BeOfType<ClearRedisOptions>();
		ClearRedisOptions clearRedisOption = _receivedOptions[1] as ClearRedisOptions;
		clearRedisOption.Environment.Should().Be("digitalads");

		_receivedOptions[2].Should().BeOfType<GetVersionOptions>();
		GetVersionOptions verOptions = _receivedOptions[2] as GetVersionOptions;
		verOptions.All.Should().BeTrue();

		_receivedOptions[3].Should().BeOfType<PullPkgOptions>();
		PullPkgOptions pullPkgOptions = _receivedOptions[3] as PullPkgOptions;
		pullPkgOptions.Environment.Should().Be("digitalads");
		pullPkgOptions.Name.Should().Be("CrtDigitalAdsApp");
		pullPkgOptions.DestPath.Should().Be("D:\\CrtDigitalAdsApp");
		pullPkgOptions.Unzip.Should().BeTrue();
	}

	[Test]
	public void Executes_Handles_EmptyFile() {
		// Arrange
		const string fileName = @"YAML/Script/emptyFile.yaml";
		ScenarioRunnerOptions options = new() {
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
		const string fileName = @"YAML/Script/non_existant.yaml";
		ScenarioRunnerOptions options = new() {
			FileName = fileName
		};
		_receivedOptions.Clear();

		// Act
		_sut.Execute(options);

		//Assert
		_receivedOptions.Should().BeEmpty();
	}

}