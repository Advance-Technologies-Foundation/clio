using System;
using Clio;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Story 1 (browser-session-handoff): the Safe-environment confirmation must fail closed in
/// non-interactive contexts instead of deadlocking on <see cref="Console.ReadKey()"/> or killing
/// the process via <see cref="System.Environment.Exit(int)"/>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class SafeEnvironmentFillTests {

	private static EnvironmentSettings SafeEnv() =>
		new() { Uri = "https://prod.creatio.com", Login = "u", Password = "p", Safe = true };

	[Test]
	[Description("NonInteractiveConsole.Prompt returns false at once without reading the console, so the MCP stdio server never blocks.")]
	public void Prompt_ShouldReturnFalseImmediately_WhenNonInteractiveConsole() {
		// Arrange
		var sut = new NonInteractiveConsole();

		// Act
		bool result = sut.Prompt("Continue?");

		// Assert
		result.Should().BeFalse("because a non-interactive context cannot confirm and must fail closed");
	}

	[Test]
	[Description("RealInteractiveConsole.Prompt fails closed and never reads a key when standard input is redirected (MCP stdio / CI pipe).")]
	public void Prompt_ShouldFailClosedAndNotReadKey_WhenInputIsRedirected() {
		// Arrange
		bool readKeyCalled = false;
		var sut = new RealInteractiveConsole(
			isInputRedirected: () => true,
			readKey: () => { readKeyCalled = true; return 'y'; });

		// Act
		bool result = sut.Prompt("Continue?");

		// Assert
		result.Should().BeFalse("because redirected stdin means no interactive confirmation is possible");
		readKeyCalled.Should().BeFalse("because Console.ReadKey must never be reached on redirected stdin — that is the deadlock this fix removes");
	}

	[Test]
	[Description("RealInteractiveConsole.Prompt returns true when stdin is a terminal and the user presses 'y'.")]
	public void Prompt_ShouldReturnTrue_WhenInteractiveAndUserConfirms() {
		// Arrange
		var sut = new RealInteractiveConsole(isInputRedirected: () => false, readKey: () => 'y');

		// Act
		bool result = sut.Prompt("Continue?");

		// Assert
		result.Should().BeTrue("because the user explicitly confirmed with 'y'");
	}

	[Test]
	[Description("RealInteractiveConsole.Prompt returns false when stdin is a terminal and the user presses a key other than 'y'.")]
	public void Prompt_ShouldReturnFalse_WhenInteractiveAndUserDeclines() {
		// Arrange
		var sut = new RealInteractiveConsole(isInputRedirected: () => false, readKey: () => 'n');

		// Act
		bool result = sut.Prompt("Continue?");

		// Assert
		result.Should().BeFalse("because any key other than 'y'/'Y' declines the confirmation");
	}

	[Test]
	[Description("Fill on a Safe environment with a declining console throws SafeEnvironmentConfirmationRequiredException instead of exiting the process.")]
	public void Fill_ShouldThrowSafeEnvironmentConfirmationRequiredException_WhenNonInteractiveAndSafeEnvironment() {
		// Arrange
		var console = Substitute.For<IInteractiveConsole>();
		console.Prompt(Arg.Any<string>()).Returns(false);
		EnvironmentSettings stored = SafeEnv();
		var options = new EnvironmentOptions();

		// Act
		Action act = () => stored.Fill(options, console);

		// Assert
		act.Should().Throw<SafeEnvironmentConfirmationRequiredException>(
			"because a Safe environment whose confirmation is declined must fail closed, not exit the process");
		console.Received(1).Prompt(Arg.Any<string>());
	}

	[Test]
	[Description("Fill on a Safe environment with a fail-closed RealInteractiveConsole (redirected stdin) throws without ever calling ReadKey — proving no deadlock on the MCP path.")]
	public void Fill_ShouldThrowAndNotReadKey_WhenSafeEnvironmentAndInputRedirected() {
		// Arrange
		bool readKeyCalled = false;
		var console = new RealInteractiveConsole(
			isInputRedirected: () => true,
			readKey: () => { readKeyCalled = true; return 'y'; });
		EnvironmentSettings stored = SafeEnv();

		// Act
		Action act = () => stored.Fill(new EnvironmentOptions(), console);

		// Assert
		act.Should().Throw<SafeEnvironmentConfirmationRequiredException>(
			"because a Safe environment on redirected stdin must fail closed");
		readKeyCalled.Should().BeFalse("because the keypress source must never be reached — the original Console.ReadKey deadlocked the stdio MCP server");
	}

	[Test]
	[Description("Fill on a Safe environment proceeds and prompts (does not throw) when the interactive console confirms — the production prompt still fires for ordinary CLI commands.")]
	public void Fill_ShouldCompleteAndPrompt_WhenSafeEnvironmentAndConsoleConfirms() {
		// Arrange
		var console = Substitute.For<IInteractiveConsole>();
		console.Prompt(Arg.Any<string>()).Returns(true);
		EnvironmentSettings stored = SafeEnv();

		// Act
		EnvironmentSettings result = stored.Fill(new EnvironmentOptions(), console);

		// Assert
		result.Should().NotBeNull("because a confirmed Safe environment is filled normally");
		console.Received(1).Prompt(Arg.Any<string>());
	}

	[Test]
	[Description("Fill on a non-Safe environment never prompts the console.")]
	public void Fill_ShouldNotPrompt_WhenEnvironmentIsNotSafe() {
		// Arrange
		var console = Substitute.For<IInteractiveConsole>();
		EnvironmentSettings stored = new() { Uri = "https://dev.creatio.com", Login = "u", Password = "p", Safe = false };

		// Act
		EnvironmentSettings result = stored.Fill(new EnvironmentOptions(), console);

		// Assert
		result.Should().NotBeNull("because a non-Safe environment is filled without confirmation");
		console.DidNotReceive().Prompt(Arg.Any<string>());
	}
}
