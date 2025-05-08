using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.YAML;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests.YAML;

[TestFixture(Author = "Kirill Krylov")]
[Category("YAML")]
public class ScenarioTests
{
    private readonly Scenario _sut;

    public ScenarioTests()
    {
        IDeserializer deserializer = new DeserializerBuilder().Build();
        _sut = new Scenario(deserializer);
    }

    [TestCase("YAML/Script/three_sections_with_step_vlaues.yaml")]
    [TestCase("YAML/Script/three_sections.yaml")]
    [TestCase("YAML/Script/steps_only_with_values.yaml")]
    [TestCase("YAML/Script/additional_steps_sections.yaml")]
    [TestCase("YAML/Script/three_sections_settings_macro.yaml")]
    public void GetSteps_Returns_ActivatedSteps_When_FileContainsSteps(string fileName)
    {
        // Arrange
        Type[] types = _sut.GetType().Assembly.GetTypes();

        // Act
        IEnumerable<(object CommandOption, string StepDescription)> steps = _sut.InitScript(fileName).GetSteps(types);

        // Assert
        steps.Should().HaveCount(4);
        List<(object CommandOption, string StepDescription)> listOfSteps = steps.ToList();

        listOfSteps[0].CommandOption.Should().BeOfType<RestartOptions>();
        RestartOptions restartOption = listOfSteps[0].CommandOption as RestartOptions;
        restartOption.Environment.Should().Be("digitalads");
        restartOption.Login.Should().Be("Supervisor");
        restartOption.Password.Should().Be("Supervisor");

        listOfSteps[1].CommandOption.Should().BeOfType<ClearRedisOptions>();
        ClearRedisOptions clearRedisOption = listOfSteps[1].CommandOption as ClearRedisOptions;
        clearRedisOption.Environment.Should().Be("digitalads");

        listOfSteps[2].CommandOption.Should().BeOfType<InfoCommandOptions>();
        InfoCommandOptions verOptions = listOfSteps[2].CommandOption as InfoCommandOptions;
        verOptions.All.Should().BeTrue();

        listOfSteps[3].CommandOption.Should().BeOfType<PullPkgOptions>();
        PullPkgOptions pullPkgOptions = listOfSteps[3].CommandOption as PullPkgOptions;
        pullPkgOptions.Environment.Should().Be("digitalads");
        pullPkgOptions.Name.Should().Be("CrtDigitalAdsApp");
        pullPkgOptions.DestPath.Should().Be("D:\\CrtDigitalAdsApp");
        pullPkgOptions.Unzip.Should().BeTrue();
    }
}
