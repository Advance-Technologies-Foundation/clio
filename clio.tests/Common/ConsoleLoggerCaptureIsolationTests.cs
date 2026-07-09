using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// FR-06 (ENG-93208): the log-capture buffer and the db-operation context are per async-flow, so two
/// concurrent MCP tool invocations never leak each other's captured lines / db-context. A
/// <see cref="Barrier"/> forces the two flows to interleave deterministically (no arbitrary sleeps).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class ConsoleLoggerCaptureIsolationTests {

	[Test]
	[Description("Two concurrent async-flows each capture only their own log lines; the per-flow buffer prevents cross-flow bleed.")]
	public void FlushAndSnapshotMessages_ShouldReturnOnlyOwnFlowLines_WhenTwoFlowsCaptureConcurrently() {
		// Arrange
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(TextWriter.Null);
		Console.SetError(TextWriter.Null);
		using Barrier barrier = new(2);
		IReadOnlyList<LogMessage> flowASnapshot = null;
		IReadOnlyList<LogMessage> flowBSnapshot = null;

		try {
			// Act
			Task flowA = Task.Run(() => {
				logger.PreserveMessages = true;
				logger.WriteInfo("flow-A-line-1");
				barrier.SignalAndWait();
				logger.WriteInfo("flow-A-line-2");
				flowASnapshot = logger.FlushAndSnapshotMessages(clearMessages: true);
			});
			Task flowB = Task.Run(() => {
				logger.PreserveMessages = true;
				logger.WriteInfo("flow-B-line-1");
				barrier.SignalAndWait();
				logger.WriteInfo("flow-B-line-2");
				flowBSnapshot = logger.FlushAndSnapshotMessages(clearMessages: true);
			});
			Task.WaitAll(flowA, flowB);

			// Assert
			string[] flowAValues = flowASnapshot.Select(m => m.Value?.ToString()).ToArray();
			string[] flowBValues = flowBSnapshot.Select(m => m.Value?.ToString()).ToArray();
			flowAValues.Should().BeEquivalentTo(
				new[] { "flow-A-line-1", "flow-A-line-2" },
				because: "flow A's capture buffer must hold only the lines A wrote even though B wrote concurrently");
			flowBValues.Should().BeEquivalentTo(
				new[] { "flow-B-line-1", "flow-B-line-2" },
				because: "flow B's capture buffer must hold only the lines B wrote even though A wrote concurrently");
		}
		finally {
			logger.ClearMessages();
			logger.PreserveMessages = false;
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}
	}

	[Test]
	[Description("Two concurrent async-flows each see only their own db-operation session and last-completed path.")]
	public void CurrentSession_ShouldBeIsolatedPerFlow_WhenTwoFlowsSetSessionConcurrently() {
		// Arrange
		DbOperationLogContextAccessor accessor = new();
		using Barrier barrier = new(2);
		IDbOperationLogSession sessionA = new StubSession("path-A");
		IDbOperationLogSession sessionB = new StubSession("path-B");
		string flowASeenPath = null;
		string flowBSeenPath = null;
		string flowACompletedPath = null;
		string flowBCompletedPath = null;

		// Act
		Task flowA = Task.Run(() => {
			accessor.SetCurrent(sessionA);
			barrier.SignalAndWait();
			flowASeenPath = accessor.CurrentSession?.LogFilePath;
			accessor.Complete(sessionA, "completed-A");
			flowACompletedPath = accessor.LastCompletedPath;
		});
		Task flowB = Task.Run(() => {
			accessor.SetCurrent(sessionB);
			barrier.SignalAndWait();
			flowBSeenPath = accessor.CurrentSession?.LogFilePath;
			accessor.Complete(sessionB, "completed-B");
			flowBCompletedPath = accessor.LastCompletedPath;
		});
		Task.WaitAll(flowA, flowB);

		// Assert
		flowASeenPath.Should().Be("path-A",
			because: "flow A must observe its own current session even though B set a different session concurrently");
		flowBSeenPath.Should().Be("path-B",
			because: "flow B must observe its own current session even though A set a different session concurrently");
		flowACompletedPath.Should().Be("completed-A",
			because: "flow A's last-completed path must be its own and never carry B's completion");
		flowBCompletedPath.Should().Be("completed-B",
			because: "flow B's last-completed path must be its own and never carry A's completion");
	}

	private sealed class StubSession(string logFilePath) : IDbOperationLogSession {
		public string LogFilePath { get; } = logFilePath;
		public void WriteNativeLine(string line) { }
		public void Dispose() { }
	}

	[Test]
	[Description("Two concurrent flows with distinct scoped file sinks: each artifact receives ONLY its own flow's log lines, never the other flow's.")]
	public void BeginScopedFileSink_ShouldReceiveOnlyOwnFlowLines_WhenTwoFlowsEmitConcurrently() {
		// Arrange
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(TextWriter.Null);
		Console.SetError(TextWriter.Null);
		string fileA = Path.Combine(Path.GetTempPath(), $"clio-sink-a-{Guid.NewGuid():N}.log");
		string fileB = Path.Combine(Path.GetTempPath(), $"clio-sink-b-{Guid.NewGuid():N}.log");
		// Phases: 1) both wrote line-1; 2) both wrote line-2 (all messages enqueued, all sinks alive);
		// 3) both drained (writes routed per-message) before either disposes its sink.
		using Barrier barrier = new(2);

		try {
			// Act
			Task flowA = Task.Run(() => {
				using IDisposable sink = logger.BeginScopedFileSink(fileA);
				logger.WriteInfo("flow-A-line-1");
				barrier.SignalAndWait();
				logger.WriteInfo("flow-A-line-2");
				barrier.SignalAndWait();
				logger.FlushAndSnapshotMessages(clearMessages: true);
				barrier.SignalAndWait();
			});
			Task flowB = Task.Run(() => {
				using IDisposable sink = logger.BeginScopedFileSink(fileB);
				logger.WriteInfo("flow-B-line-1");
				barrier.SignalAndWait();
				logger.WriteInfo("flow-B-line-2");
				barrier.SignalAndWait();
				logger.FlushAndSnapshotMessages(clearMessages: true);
				barrier.SignalAndWait();
			});
			Task.WaitAll(flowA, flowB);

			// Assert
			string contentA = File.ReadAllText(fileA);
			string contentB = File.ReadAllText(fileB);
			contentA.Should().Contain("flow-A-line-1").And.Contain("flow-A-line-2",
				because: "flow A's scoped artifact must receive the lines A produced");
			contentA.Should().NotContain("flow-B-line-1").And.NotContain("flow-B-line-2",
				because: "flow B's lines must never bleed into flow A's artifact — scoped sinks are per-flow");
			contentB.Should().Contain("flow-B-line-1").And.Contain("flow-B-line-2",
				because: "flow B's scoped artifact must receive the lines B produced");
			contentB.Should().NotContain("flow-A-line-1").And.NotContain("flow-A-line-2",
				because: "flow A's lines must never bleed into flow B's artifact — scoped sinks are per-flow");
		}
		finally {
			logger.ClearMessages();
			logger.PreserveMessages = false;
			Console.SetOut(originalOut);
			Console.SetError(originalError);
			File.Delete(fileA);
			File.Delete(fileB);
		}
	}
}
