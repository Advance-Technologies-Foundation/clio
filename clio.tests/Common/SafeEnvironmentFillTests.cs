using System;
using System.IO;
using Clio;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
		// Arrange — use TextWriter.Null to avoid writing to the global Console.Out, which other
		// test fixtures running in parallel may have redirected to a StringWriter that gets disposed.
		var sut = new RealInteractiveConsole(isInputRedirected: () => false, readKey: () => 'y',
			output: TextWriter.Null);

		// Act
		bool result = sut.Prompt("Continue?");

		// Assert
		result.Should().BeTrue("because the user explicitly confirmed with 'y'");
	}

	[TestCase('n')]
	[TestCase('\u001b')]
	[Description("RealInteractiveConsole.Prompt returns false when stdin is a terminal and the user declines or presses Escape.")]
	public void Prompt_ShouldReturnFalse_WhenInteractiveAndUserDoesNotConfirm(char answer) {
		// Arrange — use TextWriter.Null to avoid writing to the global Console.Out, which other
		// test fixtures running in parallel may have redirected to a StringWriter that gets disposed.
		var sut = new RealInteractiveConsole(isInputRedirected: () => false, readKey: () => answer,
			output: TextWriter.Null);

		// Act
		bool result = sut.Prompt("Continue?");

		// Assert
		result.Should().BeFalse("because any key other than 'y'/'Y' declines the confirmation");
	}

	[Test]
	[Description("NonInteractiveConsole.IsInteractive is false so warn-and-proceed confirmations (ENG-93157) skip the prompt and proceed without blocking.")]
	public void IsInteractive_ShouldBeFalse_WhenNonInteractiveConsole() {
		// Arrange
		var sut = new NonInteractiveConsole();

		// Act
		bool isInteractive = sut.IsInteractive;

		// Assert
		isInteractive.Should().BeFalse(
			because: "an explicitly non-interactive host cannot ask the user, so warn-and-proceed confirmations must fail open and continue");
	}

	[Test]
	[Description("RealInteractiveConsole.IsInteractive reflects whether stdin is a terminal: false on redirected stdin (MCP stdio / CI), true on a real terminal.")]
	public void IsInteractive_ShouldReflectInputRedirection_WhenRealConsole() {
		// Arrange
		var redirected = new RealInteractiveConsole(isInputRedirected: () => true, readKey: () => 'y');
		var terminal = new RealInteractiveConsole(isInputRedirected: () => false, readKey: () => 'y');

		// Act
		bool redirectedIsInteractive = redirected.IsInteractive;
		bool terminalIsInteractive = terminal.IsInteractive;

		// Assert
		redirectedIsInteractive.Should().BeFalse(
			because: "redirected stdin (MCP stdio / CI pipe) means no interactive prompt is possible");
		terminalIsInteractive.Should().BeTrue(
			because: "a real terminal can prompt the user");
	}

	[Test]
	[Description("NonInteractiveConsole.ForceInContainer overrides the default RealInteractiveConsole so any automation host (MCP resolver, scenario runner) that builds a child container resolves the shared non-interactive console — the single mechanism that keeps compile confirmations from blocking on Console.ReadKey (ENG-93157, RC-14/RC-15).")]
	public void ForceInContainer_ShouldRegisterSharedNonInteractiveConsole() {
		// Arrange — start from the production default (RealInteractiveConsole), then apply the override.
		var services = new ServiceCollection();
		services.AddSingleton<IInteractiveConsole>(RealInteractiveConsole.Shared);
		NonInteractiveConsole.ForceInContainer(services);

		// Act
		IInteractiveConsole resolved = services.BuildServiceProvider().GetRequiredService<IInteractiveConsole>();

		// Assert
		resolved.Should().BeSameAs(NonInteractiveConsole.Shared,
			because: "ForceInContainer must make the container resolve the shared non-interactive console, overriding the default, so automation-resolved commands never prompt");
	}

	[Test]
	[Description("RC-22 boundary guard: ForceInContainer overrides the IInteractiveConsole SERVICE (constructor-injected consumers get the shared non-interactive console) but does NOT retroactively rebind a singleton already built with a different console — mirroring the pre-built ISettingsRepository whose RealInteractiveConsole is baked in at construction and is therefore unaffected.")]
	public void ForceInContainer_ShouldNotRebindAlreadyConstructedSingletons() {
		// Arrange — mimic BindingsModule.RegisterInto: a singleton is built (capturing the console) BEFORE
		// additionalRegistrations (ForceInContainer) run.
		var services = new ServiceCollection();
		services.AddSingleton<IInteractiveConsole>(RealInteractiveConsole.Shared);
		var prebuilt = new ConsoleCapturingService(RealInteractiveConsole.Shared);
		services.AddSingleton(prebuilt);
		NonInteractiveConsole.ForceInContainer(services);
		var provider = services.BuildServiceProvider();

		// Act
		IInteractiveConsole resolvedConsole = provider.GetRequiredService<IInteractiveConsole>();
		ConsoleCapturingService resolvedService = provider.GetRequiredService<ConsoleCapturingService>();

		// Assert
		resolvedConsole.Should().BeSameAs(NonInteractiveConsole.Shared,
			because: "constructor-injected consumers resolved after the override get the non-interactive console");
		resolvedService.CapturedConsole.Should().BeSameAs(RealInteractiveConsole.Shared,
			because: "a singleton already built with a different console (like the pre-built ISettingsRepository) is NOT retroactively rebound by ForceInContainer — this is the RC-22 boundary");
	}

	private sealed class ConsoleCapturingService {
		public IInteractiveConsole CapturedConsole { get; }
		public ConsoleCapturingService(IInteractiveConsole capturedConsole) => CapturedConsole = capturedConsole;
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
