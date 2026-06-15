using System;
using System.Threading;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class McpServerCommandTests {

	[Test]
	[Category("Unit")]
	[Description("RequestShutdown swallows ObjectDisposedException so a process-exit / Ctrl+C handler that fires after the CancellationTokenSource was disposed during EOF teardown does not crash the mcp-server host.")]
	public void RequestShutdown_ShouldNotThrow_WhenSourceAlreadyDisposed() {
		// Arrange
		CancellationTokenSource cancellationTokenSource = new();
		cancellationTokenSource.Dispose();

		// Act
		Action act = () => McpServerCommand.RequestShutdown(cancellationTokenSource);

		// Assert
		act.Should().NotThrow(
			"because a late shutdown signal after EOF teardown must exit cleanly, not raise an unhandled ObjectDisposedException");
	}

	[Test]
	[Category("Unit")]
	[Description("RequestShutdown cancels a live CancellationTokenSource so an interactive Ctrl+C / process-exit signal still triggers graceful shutdown of the host loop.")]
	public void RequestShutdown_ShouldCancelToken_WhenSourceIsLive() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new();

		// Act
		McpServerCommand.RequestShutdown(cancellationTokenSource);

		// Assert
		cancellationTokenSource.IsCancellationRequested.Should().BeTrue(
			"because an active shutdown signal must request cancellation of the running MCP host loop");
	}

	[Test]
	[Category("Unit")]
	[Description("RequestShutdown is safe to invoke repeatedly after disposal, mirroring both a Ctrl+C and a ProcessExit signal arriving during the same EOF shutdown.")]
	public void RequestShutdown_ShouldNotThrow_WhenInvokedRepeatedlyAfterDisposal() {
		// Arrange
		CancellationTokenSource cancellationTokenSource = new();
		cancellationTokenSource.Dispose();

		// Act
		Action act = () => {
			McpServerCommand.RequestShutdown(cancellationTokenSource);
			McpServerCommand.RequestShutdown(cancellationTokenSource);
		};

		// Assert
		act.Should().NotThrow(
			"because multiple overlapping OS shutdown signals after teardown must all be tolerated");
	}
}
