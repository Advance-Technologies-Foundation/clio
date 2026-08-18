using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using Clio.Common.McpWorker;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class McpServerCommandTests {

	[Test]
	[Category("Unit")]
	[Description("Curated knowledge installation completes before the MCP protocol handshake can expose mandatory guidance.")]
	public void BootstrapCuratedKnowledge_ShouldCompleteBeforeReturning() {
		// Arrange
		ICuratedKnowledgeBootstrapService bootstrap = Substitute.For<ICuratedKnowledgeBootstrapService>();
		ILogger logger = Substitute.For<ILogger>();
		bootstrap.Bootstrap(Arg.Any<CancellationToken>())
			.Returns(new CuratedKnowledgeBootstrapResult(true, true, true, "ready"));

		// Act
		CuratedKnowledgeBootstrapResult result = McpServerCommand.BootstrapCuratedKnowledge(bootstrap, logger);

		// Assert
		result.Success.Should().BeTrue(
			because: "the host may accept requests only after the local curated source is ready or a bounded failure is known");
		bootstrap.Received(1).Bootstrap(Arg.Is<CancellationToken>(token => token.CanBeCanceled));
	}

	[Test]
	[Category("Unit")]
	[Description("Curated knowledge bootstrap failures are logged as warnings while the MCP host remains free to start.")]
	public void BootstrapCuratedKnowledge_ShouldWarnAndReturn_WhenBootstrapFails() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		CuratedKnowledgeBootstrapResult failure = new(
			false,
			true,
			false,
			"repository unavailable");

		// Act
		CuratedKnowledgeBootstrapResult result = McpServerCommand.ReportCuratedKnowledgeBootstrap(failure, logger);

		// Assert
		result.Success.Should().BeFalse(
			because: "the host must retain the bootstrap diagnostic while continuing its startup path");
		string[] warnings = logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => call.GetArguments()[0]?.ToString() ?? string.Empty)
			.ToArray();
		warnings.Should().ContainSingle(message =>
			message.Contains("repository unavailable", StringComparison.Ordinal)
			&& message.Contains("install-knowledge --source creatio-curated", StringComparison.Ordinal),
			because: "operators need both the safe failure and the exact retry command without MCP startup failing");
	}

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

	[Test]
	[Description("A host reaps workers a PREVIOUS parent left behind; the registry has had an identity-checked reaper since stage 2 and nothing called it, which is invisible to every test except one that asserts the call.")]
	public void ReapStaleWorkersForHost_ShouldReap_WhenTheProcessIsAnOrdinaryHost() {
		// Arrange
		IWorkerProcessSupervisor supervisor = Substitute.For<IWorkerProcessSupervisor>();
		supervisor.ReapStaleWorkers().Returns(new StaleWorkerReapReport(0, 0, 0, 0, []));
		ILogger logger = Substitute.For<ILogger>();
		McpServerCommandOptions options = new() { Worker = false };

		// Act
		McpServerCommand.ReapStaleWorkersForHost(options, supervisor, logger);

		// Assert
		supervisor.Received(1).ReapStaleWorkers();
	}

	[Test]
	[Description("A WORKER must not reap: it spawns no workers of its own, and reaping the shared registry from inside one would kill its siblings.")]
	public void ReapStaleWorkersForHost_ShouldNotReap_WhenTheProcessIsAWorker() {
		// Arrange
		IWorkerProcessSupervisor supervisor = Substitute.For<IWorkerProcessSupervisor>();
		ILogger logger = Substitute.For<ILogger>();
		McpServerCommandOptions options = new() { Worker = true };

		// Act
		McpServerCommand.ReapStaleWorkersForHost(options, supervisor, logger);

		// Assert
		supervisor.DidNotReceiveWithAnyArgs().ReapStaleWorkers();
	}

	[Test]
	[Description("A startup that cannot clean up must still serve: a throwing reaper is reported and swallowed rather than taking the host down before it answers anything.")]
	public void ReapStaleWorkersForHost_ShouldNotThrow_WhenTheReaperFails() {
		// Arrange
		IWorkerProcessSupervisor supervisor = Substitute.For<IWorkerProcessSupervisor>();
		supervisor.ReapStaleWorkers().Throws(new IOException("registry unreadable"));
		ILogger logger = Substitute.For<ILogger>();
		McpServerCommandOptions options = new() { Worker = false };

		// Act
		Action reap = () => McpServerCommand.ReapStaleWorkersForHost(options, supervisor, logger);

		// Assert
		reap.Should().NotThrow(
			because: "cleanup is best-effort housekeeping; a host that refuses to start because it could not tidy up is strictly worse than one that starts with an orphan still running");
	}
}
