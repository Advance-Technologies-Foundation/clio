using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ClearExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-93157: before a package compilation the interactive CLI warns that compilation is a heavy
/// operation and lets the user proceed now or postpone. Non-interactive hosts (the MCP server, CI,
/// redirected stdin) proceed without prompting so the confirmed-compile behavior is unchanged.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class CompilePackageCommandTestCase : BaseCommandTests<CompilePackageOptions>
{
	private readonly IPackageBuilder _packageBuilder = Substitute.For<IPackageBuilder>();
	private readonly IInteractiveConsole _interactiveConsole = Substitute.For<IInteractiveConsole>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_packageBuilder);
		// Override the composition-root RealInteractiveConsole so the warn-and-proceed confirmation is
		// deterministic (no dependency on the test host's real stdin).
		containerBuilder.AddSingleton(_interactiveConsole);
		// Capture the injected logger so the postpone "run it later" hint can be asserted.
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		// Reset the interaction stub to the production default (non-interactive) each test so a prior
		// test's IsInteractive=true/Prompt stub cannot leak into a later test via the reused fixture
		// instance (ClearReceivedCalls resets only call history, not stubs — review RC-13). Tests that
		// need an interactive terminal re-stub IsInteractive=true explicitly.
		_interactiveConsole.IsInteractive.Returns(false);
		// The builder substitute is shared by the fixture too, and ClearReceivedCalls keeps a When..Do that a
		// failure test installed, so a later test would see Rebuild throw and exit 1 depending on test order.
		_packageBuilder.ClearSubstitute(ClearOptions.CallActions);
	}

	[TearDown]
	public override void TearDown() {
		_packageBuilder.ClearReceivedCalls();
		_interactiveConsole.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("On an interactive terminal, when the user declines the heavy-operation warning the package compilation is postponed: the package builder is never invoked and the command returns the distinct DeclinedExitCode (2) rather than 0, so postpone cannot be mistaken for success (ENG-93157, RC-10).")]
	public void Execute_ShouldPostponeAndNotBuild_WhenInteractiveUserDeclines() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev" };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(InteractiveConsoleExtensions.DeclinedExitCode,
			because: "a declined/postponed build must return the distinct non-zero DeclinedExitCode so in-process callers and shell chains do not read it as a successful build (RC-10)");
		// The user must see the exact heavy-operation warning before deciding.
		_interactiveConsole.Received(1).Prompt(Arg.Is<string>(message =>
			message == CompilePackageCommand.PackageCompilationWarning));
		_packageBuilder.DidNotReceive().Rebuild(Arg.Any<string[]>());
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("postponed", StringComparison.Ordinal)
			&& message.Contains("clio compile-package UsrPackage", StringComparison.Ordinal)
			&& message.Contains("-e dev", StringComparison.Ordinal)));
	}

	[Test]
	[Description("On an interactive terminal, when the user confirms the warning the package compilation proceeds and the package builder is invoked (ENG-93157 regression guard).")]
	public void Execute_ShouldBuild_WhenInteractiveUserConfirms() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev" };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a confirmed package compilation runs to completion");
		_interactiveConsole.Received(1).Prompt(Arg.Any<string>());
		_packageBuilder.Received(1).Rebuild(Arg.Is<string[]>(names => names.Length == 1 && names[0] == "UsrPackage"));
	}

	[Test]
	[Description("--silent requests default behavior without user interaction, so the package build proceeds WITHOUT prompting even on an interactive terminal (review RC-2).")]
	public void Execute_ShouldBuildWithoutPrompting_WhenSilentEvenIfInteractive() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev", IsSilent = true };
		_interactiveConsole.IsInteractive.Returns(true);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "--silent must never block on a prompt and proceeds to build");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		_packageBuilder.Received(1).Rebuild(Arg.Any<string[]>());
	}

	[Test]
	[Description("On a non-interactive host (the MCP server that runs this same command, CI, redirected stdin) the package compilation proceeds WITHOUT prompting, so the confirmed-compile behavior is unchanged (ENG-93157 regression guard).")]
	public void Execute_ShouldBuildWithoutPrompting_WhenNonInteractive() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev" };
		_interactiveConsole.IsInteractive.Returns(false);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a non-interactive host must never be blocked by a prompt");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		_packageBuilder.Received(1).Rebuild(Arg.Any<string[]>());
	}

	[Test]
	[Description("When the confirmed package build throws, the command surfaces exit code 1 — the confirmation gate does not swallow or alter the underlying failure (ENG-93157 regression guard).")]
	public void Execute_ShouldReturnErrorExitCode_WhenConfirmedBuildThrows() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev" };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);
		_packageBuilder.When(builder => builder.Rebuild(Arg.Any<string[]>()))
			.Do(_ => throw new Exception("rebuild failed"));

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a build failure after confirmation must surface as exit code 1, unchanged by the confirmation gate");
		_packageBuilder.Received(1).Rebuild(Arg.Any<string[]>());
	}

	[Test]
	[Description("--wait asks the package builder to block until the build has finished, with the 600-second default --wait-timeout, instead of returning when the build was accepted (issue #1632).")]
	public void Execute_ShouldRebuildWithWaitOptions_WhenWaitIsSet() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev", Wait = true };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a waited build that finished cleanly is a success");
		_packageBuilder.Received(1).Rebuild(Arg.Is<string[]>(names => names.Length == 1 && names[0] == "UsrPackage"),
			Arg.Is<PackageCompilationWaitOptions>(wait => wait.Timeout == TimeSpan.FromSeconds(600)));
		_packageBuilder.DidNotReceive().Rebuild(Arg.Any<string[]>());
	}

	[Test]
	[Description("A --wait-timeout outside 1..3600 seconds is rejected before anything is compiled, so a typo cannot turn into an unbounded or zero-length wait (issue #1632).")]
	public void Execute_ShouldRejectOutOfRangeWaitTimeout_WithoutCompiling() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() {
			PackageName = "UsrPackage", Environment = "dev", Wait = true, WaitTimeout = 0
		};

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an out-of-range wait bound is a usage error");
		_packageBuilder.DidNotReceive().Rebuild(Arg.Any<string[]>(), Arg.Any<PackageCompilationWaitOptions>());
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("--wait-timeout", StringComparison.Ordinal)));
	}

	[Test]
	[Description("The --wait-timeout upper bound is inclusive: 3600 seconds is accepted and 3601 is rejected before anything is compiled (issue #1632).")]
	[TestCase(3600, 0)]
	[TestCase(3601, 1)]
	public void Execute_ShouldApplyInclusiveUpperWaitTimeoutBound(int waitTimeout, int expectedExitCode) {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() {
			PackageName = "UsrPackage", Environment = "dev", Wait = true, WaitTimeout = waitTimeout
		};

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(expectedExitCode, because: "the documented --wait-timeout range is 1..3600 seconds");
		_packageBuilder.Received(expectedExitCode == 0 ? 1 : 0).Rebuild(Arg.Any<string[]>(),
			Arg.Any<PackageCompilationWaitOptions>());
	}

	[Test]
	[Description("--wait-timeout is not validated when --wait is not set, so the unused option cannot fail a default compile (issue #1632).")]
	public void Execute_ShouldIgnoreWaitTimeout_WhenWaitIsNotSet() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() {
			PackageName = "UsrPackage", Environment = "dev", Wait = false, WaitTimeout = 0
		};

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "--wait-timeout only bounds a waited build");
		_packageBuilder.Received(1).Rebuild(Arg.Any<string[]>());
	}

	[Test]
	[Description("An out-of-range --wait-timeout is rejected before the heavy-operation prompt, so an interactive user is not asked to approve a compile that is then refused over an option.")]
	public void Execute_ShouldRejectOutOfRangeWaitTimeout_BeforePrompting() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() {
			PackageName = "UsrPackage", Environment = "dev", Wait = true, WaitTimeout = 3601
		};
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an out-of-range wait bound is a usage error");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
	}

	[Test]
	[Description("When Creatio reports a failed package build the command exits 1 and prints the failure summary instead of Done (issue #1633).")]
	public void Execute_ShouldExitNonZeroWithoutDone_WhenBuildReportsCompileError() {
		// Arrange
		CompilePackageCommand command = Container.GetRequiredService<CompilePackageCommand>();
		CompilePackageOptions options = new() { PackageName = "UsrPackage", Environment = "dev" };
		_packageBuilder.When(builder => builder.Rebuild(Arg.Any<string[]>()))
			.Do(_ => throw new PackageCompilationException("Package compilation failed for 'UsrPackage' (build result 1)."));

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a C# compile error must not end in exit code 0");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Package compilation failed", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteInfo("Done");
	}
}
