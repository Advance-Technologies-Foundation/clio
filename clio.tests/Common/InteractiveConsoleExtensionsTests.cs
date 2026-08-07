using System;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Direct regression coverage for the shared warn-and-proceed seam (ENG-93157): the fail-open
/// confirmation and the full silent/confirm/decline/postpone orchestration both compile commands rely on.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class InteractiveConsoleExtensionsTests {

	[Test]
	[Description("ConfirmOrProceedWhenNonInteractive proceeds without prompting when the host is non-interactive.")]
	public void ConfirmOrProceedWhenNonInteractive_ShouldProceedWithoutPrompting_WhenNonInteractive() {
		// Arrange
		IInteractiveConsole console = Substitute.For<IInteractiveConsole>();
		console.IsInteractive.Returns(false);

		// Act
		bool result = console.ConfirmOrProceedWhenNonInteractive("warning");

		// Assert
		result.Should().BeTrue(because: "a non-interactive host cannot be asked and must fail open (proceed)");
		console.DidNotReceive().Prompt(Arg.Any<string>());
	}

	[Test]
	[Description("ConfirmOrProceedWhenNonInteractive delegates to Prompt and returns its result on an interactive host.")]
	[TestCase(true)]
	[TestCase(false)]
	public void ConfirmOrProceedWhenNonInteractive_ShouldDelegateToPrompt_WhenInteractive(bool promptResult) {
		// Arrange
		IInteractiveConsole console = Substitute.For<IInteractiveConsole>();
		console.IsInteractive.Returns(true);
		console.Prompt("warning").Returns(promptResult);

		// Act
		bool result = console.ConfirmOrProceedWhenNonInteractive("warning");

		// Assert
		result.Should().Be(promptResult, because: "on an interactive host the user's Prompt answer decides");
		console.Received(1).Prompt("warning");
	}

	[Test]
	[Description("ConfirmOrProceedWhenNonInteractive throws ArgumentNullException when the console is null.")]
	public void ConfirmOrProceedWhenNonInteractive_ShouldThrow_WhenConsoleIsNull() {
		// Arrange
		IInteractiveConsole console = null;

		// Act
		Action act = () => console.ConfirmOrProceedWhenNonInteractive("warning");

		// Assert
		act.Should().Throw<ArgumentNullException>(because: "the console argument is required");
	}

	[Test]
	[Description("ConfirmHeavyOperation proceeds without prompting or logging when --silent is set, even on an interactive host.")]
	public void ConfirmHeavyOperation_ShouldProceedWithoutPrompting_WhenSilent() {
		// Arrange
		IInteractiveConsole console = Substitute.For<IInteractiveConsole>();
		console.IsInteractive.Returns(true);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		bool result = console.ConfirmHeavyOperation(isSilent: true, "warning", logger, "hint");

		// Assert
		result.Should().BeTrue(because: "--silent requests default behavior without user interaction");
		console.DidNotReceive().Prompt(Arg.Any<string>());
		logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	[Test]
	[Description("ConfirmHeavyOperation proceeds without logging the postpone hint when an interactive user confirms.")]
	public void ConfirmHeavyOperation_ShouldProceedAndNotLogHint_WhenInteractiveUserConfirms() {
		// Arrange
		IInteractiveConsole console = Substitute.For<IInteractiveConsole>();
		console.IsInteractive.Returns(true);
		console.Prompt("warning").Returns(true);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		bool result = console.ConfirmHeavyOperation(isSilent: false, "warning", logger, "hint");

		// Assert
		result.Should().BeTrue(because: "the interactive user confirmed");
		logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	[Test]
	[Description("ConfirmHeavyOperation logs the postpone hint and declines when an interactive user declines.")]
	public void ConfirmHeavyOperation_ShouldLogHintAndDecline_WhenInteractiveUserDeclines() {
		// Arrange
		IInteractiveConsole console = Substitute.For<IInteractiveConsole>();
		console.IsInteractive.Returns(true);
		console.Prompt("warning").Returns(false);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		bool result = console.ConfirmHeavyOperation(isSilent: false, "warning", logger, "run it later hint");

		// Assert
		result.Should().BeFalse(because: "the interactive user declined the heavy operation");
		logger.Received(1).WriteInfo("run it later hint");
	}

	[Test]
	[Description("ConfirmHeavyOperation proceeds without prompting on a non-interactive host (fail open).")]
	public void ConfirmHeavyOperation_ShouldProceed_WhenNonInteractive() {
		// Arrange
		IInteractiveConsole console = Substitute.For<IInteractiveConsole>();
		console.IsInteractive.Returns(false);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		bool result = console.ConfirmHeavyOperation(isSilent: false, "warning", logger, "hint");

		// Assert
		result.Should().BeTrue(because: "a non-interactive host must proceed without blocking");
		console.DidNotReceive().Prompt(Arg.Any<string>());
		logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	[Test]
	[Description("ConfirmHeavyOperation throws ArgumentNullException when the console is null.")]
	public void ConfirmHeavyOperation_ShouldThrow_WhenConsoleIsNull() {
		// Arrange
		IInteractiveConsole console = null;
		ILogger logger = Substitute.For<ILogger>();

		// Act
		Action act = () => console.ConfirmHeavyOperation(isSilent: false, "warning", logger, "hint");

		// Assert
		act.Should().Throw<ArgumentNullException>(because: "the console argument is required");
	}
}
