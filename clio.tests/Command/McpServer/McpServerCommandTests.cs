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
	[Description("A successful warm start whose cache is stale is reported as a warning, not hidden in debug output.")]
	public void ReportCuratedKnowledgeBootstrap_ShouldWarn_WhenTheServedCacheIsStale() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		CuratedKnowledgeBootstrapResult stale = new(
			true,
			true,
			true,
			"ready from its local cache",
			"serving library version 1.12.0 (sequence 7); refresh with update-knowledge");

		// Act
		CuratedKnowledgeBootstrapResult result = McpServerCommand.ReportCuratedKnowledgeBootstrap(stale, logger);

		// Assert
		result.Success.Should().BeTrue(
			because: "a stale cache is a usable cache and must not turn a working start into a failure");
		string[] warnings = logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => call.GetArguments()[0]?.ToString() ?? string.Empty)
			.ToArray();
		warnings.Should().ContainSingle(message => message.Contains("1.12.0", StringComparison.Ordinal),
			because: "reporting the stale version at debug level would keep exactly the silence issue #1100 reports");
	}

	[Test]
	[Category("Unit")]
	[NonParallelizable]
	[Description("A stale-cache warning is written to stderr in stdio MCP mode without touching the protocol stream.")]
	public void ReportCuratedKnowledgeBootstrap_ShouldWriteStandardError_WhenMcpUsesStdio() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		CuratedKnowledgeBootstrapResult stale = new(
			true,
			true,
			true,
			"ready from its local cache",
			"serving library version 1.12.0; check with update-knowledge");
		TextWriter originalError = Console.Error;
		bool originalMcpMode = global::Clio.Program.IsMcpServerMode;
		using StringWriter standardError = new();

		try {
			global::Clio.Program.IsMcpServerMode = true;
			Console.SetError(standardError);

			// Act
			McpServerCommand.ReportCuratedKnowledgeBootstrap(stale, logger);

			// Assert
			standardError.ToString().Should().Contain("[WAR]", because: "stderr is the operator-visible channel in stdio mode");
			standardError.ToString().Should().Contain("1.12.0",
				because: "the stderr warning must identify the generation actually being served");
		} finally {
			Console.SetError(originalError);
			global::Clio.Program.IsMcpServerMode = originalMcpMode;
		}
	}

	[Test]
	[Category("Unit")]
	[NonParallelizable]
	[Description("A closed stderr sink cannot turn an advisory stale-cache warning into MCP startup failure.")]
	public void ReportCuratedKnowledgeBootstrap_ShouldContinue_WhenStandardErrorIsUnavailable() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		CuratedKnowledgeBootstrapResult stale = new(
			true,
			true,
			true,
			"ready from its local cache",
			"cached library version 1.12.0; check with update-knowledge");
		TextWriter originalError = Console.Error;
		bool originalMcpMode = global::Clio.Program.IsMcpServerMode;

		try {
			global::Clio.Program.IsMcpServerMode = true;
			Console.SetError(new ThrowingTextWriter());

			// Act
			Func<CuratedKnowledgeBootstrapResult> act = () =>
				McpServerCommand.ReportCuratedKnowledgeBootstrap(stale, logger);

			// Assert
			act.Should().NotThrow(
				because: "stderr is an optional diagnostic sink and cannot be allowed to abort the MCP handshake");
			logger.ReceivedCalls().Should().ContainSingle(
				call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning),
				because: "the configured logger must still receive the warning when stderr is unavailable");
		} finally {
			Console.SetError(originalError);
			global::Clio.Program.IsMcpServerMode = originalMcpMode;
		}
	}

	[Test]
	[Category("Unit")]
	[Description("A successful warm start with a fresh cache emits no warning at all.")]
	public void ReportCuratedKnowledgeBootstrap_ShouldNotWarn_WhenTheServedCacheIsFresh() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		CuratedKnowledgeBootstrapResult fresh = new(true, true, true, "ready from its local cache");

		// Act
		CuratedKnowledgeBootstrapResult result = McpServerCommand.ReportCuratedKnowledgeBootstrap(fresh, logger);

		// Assert
		result.Success.Should().BeTrue(
			because: "a fresh cache is a healthy warm start and must be reported as a success");
		string[] warnings = logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => call.GetArguments()[0]?.ToString() ?? string.Empty)
			.ToArray();
		warnings.Should().BeEmpty(
			because: "warning about a cache that is already up to date would train operators to ignore staleness warnings");
	}

	private sealed class ThrowingTextWriter : StringWriter {
		public override void WriteLine(string? value) => throw new IOException("stderr is closed");
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
