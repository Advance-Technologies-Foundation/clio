namespace Clio.Tests.YAML;

using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.YAML;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using OneOf;
using OneOf.Types;

[TestFixture(Author = "Kirill Krylov")]
[Category("YAML")]
internal class StepTests
{

	#region Fields: Private

	private readonly RestartOptions _marker = new();

	#endregion

	[Test]
	public void Activate_Returns_Initialized_CommandOptionType() {
		//Arrange
		const string expectedEnvName = "digitalads";
		Step step = new() {
			Action = "restart",
			Description = "restart application",
			Options = new Dictionary<object, object> {
				{"Environment", expectedEnvName}
			}
		};

		Type[] types = _marker.GetType().Assembly.GetTypes();
		Func<string, OneOf<object, None>> settingsLookupMock = Substitute.For<Func<string, OneOf<object, None>>>();
		settingsLookupMock.Invoke(Arg.Any<string>()).Returns(string.Empty);

		Func<string, OneOf<object, None>> secretsLookupMock = Substitute.For<Func<string, OneOf<object, None>>>();
		secretsLookupMock.Invoke(Arg.Any<string>()).Returns(string.Empty);

		//Act
		Tuple<OneOf<None, object>, string> commandOption = step.Activate(types, settingsLookupMock, secretsLookupMock);

		//Assert
		settingsLookupMock.DidNotReceive().Invoke(Arg.Any<string>());
		secretsLookupMock.DidNotReceive().Invoke(Arg.Any<string>());
		commandOption.Item1.Value.Should().BeOfType<RestartOptions>();
		RestartOptions restartOption = commandOption.Item1.Value as RestartOptions;
		restartOption.Environment.Should().Be(expectedEnvName);
		commandOption.Item2.Should().Be("restart application");
	}

	[Test]
	public void Activate_Returns_Initialized_CommandOptionType_WithWrongMacro() {
		//Arrange
		Step step = new() {
			Action = "restart",
			Description = "restart application",
			Options = new Dictionary<object, object> {
				{"Environment", "{{settings.Environmentttttttttt}}"}
			}
		};

		Type[] types = _marker.GetType().Assembly.GetTypes();
		Func<string, OneOf<object, None>> settingsLookupMock = Substitute.For<Func<string, OneOf<object, None>>>();
		settingsLookupMock.Invoke(Arg.Is("Environmentttttttttt")).Returns(new None());

		Func<string, OneOf<object, None>> secretsLookupMock = Substitute.For<Func<string, OneOf<object, None>>>();
		secretsLookupMock.Invoke(Arg.Any<string>()).Returns(string.Empty);

		//Act
		Tuple<OneOf<None, object>, string> commandOption = step.Activate(types, settingsLookupMock, secretsLookupMock);

		//Assert
		settingsLookupMock.Received(1).Invoke(Arg.Any<string>());
		secretsLookupMock.DidNotReceive().Invoke(Arg.Any<string>());
		commandOption.Item1.Value.Should().BeOfType<RestartOptions>();
		RestartOptions restartOption = commandOption.Item1.Value as RestartOptions;
		restartOption.Environment.Should().Be("{{settings.Environmentttttttttt}}");
	}

	[Test]
	public void Activate_Returns_Initialized_CommandOptionType_WithWrongMacroFormat() {
		//Arrange
		Step step = new() {
			Action = "restart",
			Description = "restart application",
			Options = new Dictionary<object, object> {
				{"Environment", "{{settings:Environmentttttttttt}}"}
			}
		};

		Type[] types = _marker.GetType().Assembly.GetTypes();
		Func<string, OneOf<object, None>> settingsLookupMock = Substitute.For<Func<string, OneOf<object, None>>>();
		settingsLookupMock.Invoke(Arg.Is("Environmentttttttttt")).Returns(new None());

		Func<string, OneOf<object, None>> secretsLookupMock = Substitute.For<Func<string, OneOf<object, None>>>();
		secretsLookupMock.Invoke(Arg.Any<string>()).Returns(string.Empty);

		//Act
		Tuple<OneOf<None, object>, string> commandOption = step.Activate(types, settingsLookupMock, secretsLookupMock);

		//Assert
		settingsLookupMock.DidNotReceive().Invoke(Arg.Any<string>());
		secretsLookupMock.DidNotReceive().Invoke(Arg.Any<string>());
		commandOption.Item1.Value.Should().BeOfType<RestartOptions>();
		RestartOptions restartOption = commandOption.Item1.Value as RestartOptions;
		restartOption.Environment.Should().Be("{{settings:Environmentttttttttt}}");
	}

}