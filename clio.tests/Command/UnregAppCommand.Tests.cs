using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class UnregAppCommandTests : BaseCommandTests<UnregAppOptions>
{
	private ISettingsRepository _settingsRepository;
	private UnregAppCommand _command;
	private TextReader _originalConsoleIn;
	private TextWriter _originalConsoleOut;
	private StringWriter _consoleOutput;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		containerBuilder.AddSingleton<ISettingsRepository>(_settingsRepository);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UnregAppCommand>();
		_originalConsoleIn = Console.In;
		_originalConsoleOut = Console.Out;
		_consoleOutput = new StringWriter();
		Console.SetOut(_consoleOutput);
	}

	[TearDown]
	public override void TearDown() {
		Console.SetIn(_originalConsoleIn);
		Console.SetOut(_originalConsoleOut);
		_settingsRepository.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Removes the named environment when the positional argument is provided.")]
	public void Execute_RemovesEnvironment_WhenNameProvided() {
		UnregAppOptions options = new() {
			Name = "Test"
		};

		int result = _command.Execute(options);

		result.Should().Be(0, because: "a valid positional environment name should be removed successfully");
		_consoleOutput.ToString().Should().Contain("Environment Test was deleted...",
			because: "successful removal should be reported to the user");
		_settingsRepository.Received(1).RemoveEnvironment("Test");
		_settingsRepository.DidNotReceive().RemoveAllEnvironment();
	}

	[Test]
	[Description("Removes the named environment when the shared -e/--Environment option is provided.")]
	public void Execute_RemovesEnvironment_WhenEnvironmentAliasProvided() {
		UnregAppOptions options = new() {
			Environment = "AliasEnv"
		};

		int result = _command.Execute(options);

		result.Should().Be(0, because: "the shared environment option should act as a target alias for unreg-web-app");
		_settingsRepository.Received(1).RemoveEnvironment("AliasEnv");
		_settingsRepository.DidNotReceive().RemoveAllEnvironment();
	}

	[Test]
	[Description("Shows sorted registered environments and removes the selected one when no target was provided.")]
	public void Execute_RemovesSelectedEnvironment_WhenInteractiveSelectionIsValid() {
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["beta"] = new() { Uri = "https://beta" },
			["alpha"] = new() { Uri = "https://alpha" }
		});
		_settingsRepository.GetDefaultEnvironmentName().Returns("beta");
		Console.SetIn(new StringReader("2" + Environment.NewLine));

		int result = _command.Execute(new UnregAppOptions());

		result.Should().Be(0, because: "a valid menu selection should resolve the environment to remove");
		_consoleOutput.ToString().Should().Contain("1. alpha - https://alpha",
			because: "the interactive list should be sorted alphabetically");
		_consoleOutput.ToString().Should().Contain("2. beta - https://beta (active)",
			because: "the active environment should be marked in the interactive list");
		_settingsRepository.Received(1).RemoveEnvironment("beta");
	}

	[Test]
	[Description("Returns success without deletion when no registered environments exist for interactive selection.")]
	public void Execute_ReturnsZero_WhenNoRegisteredEnvironmentsExist() {
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings>());

		int result = _command.Execute(new UnregAppOptions());

		result.Should().Be(0, because: "an empty environment catalog is not an execution failure");
		_consoleOutput.ToString().Should().Contain("No environments configured",
			because: "the command should explain why no selection was shown");
		_settingsRepository.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
		_settingsRepository.DidNotReceive().RemoveAllEnvironment();
	}

	[Test]
	[Description("Cancels interactive selection when the user presses Enter without choosing an environment.")]
	public void Execute_ReturnsZero_WhenInteractiveSelectionIsCancelled() {
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["alpha"] = new() { Uri = "https://alpha" }
		});
		Console.SetIn(new StringReader(Environment.NewLine));

		int result = _command.Execute(new UnregAppOptions());

		result.Should().Be(0, because: "empty input should cancel the interactive flow instead of failing");
		_consoleOutput.ToString().Should().Contain("Operation cancelled",
			because: "the user should be told that no deletion happened");
		_settingsRepository.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
	}

	[Test]
	[Description("Returns an error when the interactive selection does not match any numbered item.")]
	public void Execute_ReturnsOne_WhenInteractiveSelectionIsInvalid() {
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["alpha"] = new() { Uri = "https://alpha" }
		});
		Console.SetIn(new StringReader("3" + Environment.NewLine));

		int result = _command.Execute(new UnregAppOptions());

		result.Should().Be(1, because: "an out-of-range menu choice should be rejected");
		_consoleOutput.ToString().Should().Contain("Invalid selection. Enter a number from the list.",
			because: "the command should explain how to fix the interactive input");
		_settingsRepository.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
	}

	[Test]
	[Description("Requires an explicit target or --all when the command runs in silent mode.")]
	public void Execute_ReturnsOne_WhenSilentModeHasNoTarget() {
		UnregAppOptions options = new() {
			IsSilent = true
		};

		int result = _command.Execute(options);

		result.Should().Be(1, because: "silent mode cannot prompt for an interactive environment choice");
		_consoleOutput.ToString().Should()
			.Contain("Environment name is required in --silent mode. Pass <Name>, -e/--Environment, or --all.",
				because: "the failure should tell the user how to run the command non-interactively");
		_settingsRepository.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
	}

	[Test]
	[Description("Removes all environments without resolving a specific target when --all is provided.")]
	public void Execute_RemovesAllEnvironments_WhenAllFlagProvided() {
		UnregAppOptions options = new() {
			UnregAll = true,
			Name = "Ignored"
		};

		int result = _command.Execute(options);

		result.Should().Be(0, because: "the all flag should take precedence over any specific target name");
		_settingsRepository.Received(1).RemoveAllEnvironment();
		_settingsRepository.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
	}
}
