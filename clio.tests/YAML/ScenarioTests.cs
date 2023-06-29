namespace Clio.Tests.YAML;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.YAML;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

[TestFixture(Author = "Kirill Krylov")]
[Category("YAML")]
public class ScenarioTests
{

	#region Fields: Private

	private readonly Scenario _sut;

	#endregion

	#region Constructors: Public

	public ScenarioTests() {
		IDeserializer deserializer = new DeserializerBuilder().Build();
		_sut = new Scenario(deserializer);
	}

	#endregion

	#region Methods: Public

	[TestCase("YAML/Script/three_sections_with_step_vlaues.yaml")]
	[TestCase("YAML/Script/three_sections.yaml")]
	[TestCase("YAML/Script/steps_only_with_values.yaml")]
	[TestCase("YAML/Script/additional_steps_sections.yaml")]
	[TestCase("YAML/Script/three_sections_settings_macro.yaml")]
	public void GetSteps_Returns_ActivatedSteps_When_FileContainsSteps(string fileName) {
		//Arrange 
		Type[] types = _sut.GetType().Assembly.GetTypes();

		//Act
		IEnumerable<Tuple<object, string>> steps = _sut.InitScript(fileName).GetSteps(types);

		//Assert
		steps.Should().HaveCount(4);
		List<Tuple<object, string>> listOfSteps = steps.ToList();

		listOfSteps[0].Item1.Should().BeOfType<RestartOptions>();
		RestartOptions restartOption = listOfSteps[0].Item1 as RestartOptions;
		restartOption.Environment.Should().Be("digitalads");
		restartOption.Login.Should().Be("Supervisor");
		restartOption.Password.Should().Be("Supervisor");

		listOfSteps[1].Item1.Should().BeOfType<ClearRedisOptions>();
		ClearRedisOptions clearRedisOption = listOfSteps[1].Item1 as ClearRedisOptions;
		clearRedisOption.Environment.Should().Be("digitalads");

		listOfSteps[2].Item1.Should().BeOfType<GetVersionOptions>();
		GetVersionOptions verOptions = listOfSteps[2].Item1 as GetVersionOptions;
		verOptions.All.Should().BeTrue();

		listOfSteps[3].Item1.Should().BeOfType<PullPkgOptions>();
		PullPkgOptions pullPkgOptions = listOfSteps[3].Item1 as PullPkgOptions;
		pullPkgOptions.Environment.Should().Be("digitalads");
		pullPkgOptions.Name.Should().Be("CrtDigitalAdsApp");
		pullPkgOptions.DestPath.Should().Be("D:\\CrtDigitalAdsApp");
		pullPkgOptions.Unzip.Should().BeTrue();
	}
	
	#endregion

}