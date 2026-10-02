using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class ResetNavigationCacheCommandTests : BaseCommandTests<ResetNavigationCacheOptions> {

	private const string BrowserSessionNote = "run GetData(true) in the open tab, then reload it";
	private const string ResetWarning = "navigation cache reset failed: Access denied";

	private ResetNavigationCacheCommand _command = null!;
	private IApplicationClient _applicationClient = null!;
	private INavigationCacheResetter _navigationCacheResetter = null!;
	private ILogger _logger = null!;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ResetNavigationCacheCommand>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_navigationCacheResetter.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_navigationCacheResetter = Substitute.For<INavigationCacheResetter>();
		_navigationCacheResetter.BuildBrowserSessionNote(Arg.Any<EnvironmentSettings>()).Returns(BrowserSessionNote);
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _navigationCacheResetter);
	}

	[Test]
	[Description("Resets the cache through the environment's client and returns success with the browser-session note.")]
	public void Reset_ShouldReturnSuccessWithNote_WhenResetSucceeds() {
		// Arrange
		_navigationCacheResetter.TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns((string?)null);

		// Act
		ResetNavigationCacheResponse response = _command.Reset(new ResetNavigationCacheOptions());

		// Assert
		_navigationCacheResetter.Received(1).TryReset(_applicationClient, Arg.Any<EnvironmentSettings>());
		response.Success.Should().BeTrue(because: "the server confirmed the reset");
		response.Error.Should().BeNull(because: "a confirmed reset has no failure to report");
		response.NextStep.Should().Be(BrowserSessionNote,
			because: "clio's reset never reaches a browser tab, so the caller needs the in-tab call");
	}

	[Test]
	[Description("A failed reset becomes success=false with the resetter's message, and still carries the browser-session note.")]
	public void Reset_ShouldReturnFailureWithMessageAndNote_WhenResetFails() {
		// Arrange
		_navigationCacheResetter.TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns(ResetWarning);

		// Act
		ResetNavigationCacheResponse response = _command.Reset(new ResetNavigationCacheOptions());

		// Assert
		response.Success.Should().BeFalse(because: "the server did not confirm the reset");
		response.Error.Should().Be(ResetWarning, because: "the caller must see why the reset failed");
		response.NextStep.Should().Be(BrowserSessionNote,
			because: "a browser tab can still be refreshed from inside the tab when clio's own reset failed");
	}

	[Test]
	[Description("Execute exits with 0 and prints the browser-session note when the reset succeeds.")]
	public void Execute_ShouldReturnZeroAndPrintNote_WhenResetSucceeds() {
		// Arrange
		_navigationCacheResetter.TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns((string?)null);

		// Act
		int exitCode = _command.Execute(new ResetNavigationCacheOptions());

		// Assert
		exitCode.Should().Be(0, because: "the server confirmed the reset");
		_logger.Received(1).WriteInfo(BrowserSessionNote);
		_logger.DidNotReceiveWithAnyArgs().WriteError(default!);
	}

	[Test]
	[Description("Execute exits with 1, prints the failure as an error and still prints the browser-session note when the reset fails.")]
	public void Execute_ShouldReturnOneAndPrintErrorAndNote_WhenResetFails() {
		// Arrange
		_navigationCacheResetter.TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns(ResetWarning);

		// Act
		int exitCode = _command.Execute(new ResetNavigationCacheOptions());

		// Assert
		exitCode.Should().Be(1, because: "the server did not confirm the reset");
		_logger.Received(1).WriteError(ResetWarning);
		_logger.Received(1).WriteInfo(BrowserSessionNote);
	}
}
