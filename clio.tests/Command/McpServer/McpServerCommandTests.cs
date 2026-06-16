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
	[Description("RequestShutdown is a tolerated no-op on a live source that was already cancelled, mirroring a second OS shutdown signal (Ctrl+C then ProcessExit) arriving while graceful cancellation is already in flight but before EOF teardown disposes the source.")]
	public void RequestShutdown_ShouldNotThrow_WhenSourceAlreadyCancelled() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new();
		cancellationTokenSource.Cancel();

		// Act
		Action act = () => McpServerCommand.RequestShutdown(cancellationTokenSource);

		// Assert
		act.Should().NotThrow(
			"because a redundant shutdown signal on an already-cancelling host loop must be tolerated without error");
		cancellationTokenSource.IsCancellationRequested.Should().BeTrue(
			"because the source must stay cancelled after a repeated shutdown request");
	}

	[Test]
	[Category("Unit")]
	[Description("RequestShutdown swallows the AggregateException that Cancel() surfaces when a synchronous cancellation callback throws during teardown, so a Ctrl+C / process-exit signal whose callback faults still exits cleanly instead of crashing the mcp-server host.")]
	public void RequestShutdown_ShouldNotThrow_WhenCancellationCallbackThrows() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new();
		cancellationTokenSource.Token.Register(static () => throw new InvalidOperationException("cancellation callback fault"));

		// Act
		Action act = () => McpServerCommand.RequestShutdown(cancellationTokenSource);

		// Assert
		act.Should().NotThrow(
			"because a faulting cancellation callback during shutdown must not crash the host with an unhandled AggregateException");
		cancellationTokenSource.IsCancellationRequested.Should().BeTrue(
			"because the shutdown request must still mark the source cancelled even when a callback throws");
	}
}
