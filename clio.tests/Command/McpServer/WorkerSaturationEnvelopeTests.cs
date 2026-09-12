using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.McpServer.Relay;
using Clio.Common.McpWorker;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262: a call refused because every worker slot is in use must say SO, and must not be dressed
/// up as clio failing to start a process.
/// </summary>
/// <remarks>
/// R-10 promises a named saturation refusal "carrying cap and queue depth … never an error that reads
/// as a backend timeout". The generic relay-failure envelope said "the worker process could not be
/// started", which reads as a clio defect and sends an agent hunting a bug in clio when the host is
/// merely busy — and it discarded the two numbers that tell an operator whether to wait or to raise
/// the cap.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class WorkerSaturationEnvelopeTests {

	[Test]
	[Description("A queue-wait expiry is reported as saturation, carrying the cap and the queue depth, and never as a failure to start a process.")]
	public void WorkerSaturationResult_ShouldCarryTheCapAndDepth_WhenTheQueueWaitExpired() {
		// Arrange — the refusal the supervisor actually throws, with numbers a caller can act on.
		WorkerQueueWaitExpiredException refusal = new(
			waitEndured: TimeSpan.FromSeconds(60), configuredBound: TimeSpan.FromSeconds(60),
			concurrencyCap: 4, queueDepth: 9);

		// Act
		CallToolResult result = McpWorkerCallDispatcher.WorkerSaturationResult("get-page", refusal);

		// Assert
		result.IsError.Should().BeTrue(
			because: "the call did not run, so a caller must not read the answer as a result");
		JsonNode payload = JsonNode.Parse(result.StructuredContent.Value.GetRawText());
		payload["error-class"]?.GetValue<string>().Should().Be("clio-worker-saturated",
			because: "saturation and a relay failure need different classes: one means wait, the other means somebody should look for a bug in clio");
		payload["worker-concurrency"]?.GetValue<int>().Should().Be(4,
			because: "the cap is half of what tells an operator whether to wait or to raise CLIO_MCP_WORKER_CONCURRENCY");
		payload["queue-depth"]?.GetValue<int>().Should().Be(9,
			because: "the depth is the other half — a burst of nine behind a cap of four is a different situation from one caller unlucky at the bound");
		payload["retry-guidance"]?.GetValue<string>().Should().Contain("Retry",
			because: "nothing was spawned and no request reached Creatio, so unlike an indeterminate deploy this call is genuinely safe to repeat");
	}

	[Test]
	[Description("The saturation envelope must not claim the process could not be started: that wording is the generic relay failure and it points the reader at the wrong cause.")]
	public void WorkerSaturationResult_ShouldNotReadAsAFailureToStartTheProcess_WhenTheHostIsMerelyBusy() {
		// Arrange
		WorkerQueueWaitExpiredException refusal = new(
			waitEndured: TimeSpan.FromSeconds(60), configuredBound: TimeSpan.FromSeconds(60),
			concurrencyCap: 2, queueDepth: 3);

		// Act
		CallToolResult result = McpWorkerCallDispatcher.WorkerSaturationResult("list-pages", refusal);
		string text = ((TextContentBlock)result.Content[0]).Text;

		// Assert
		text.Should().NotContain("could not be started",
			because: "that is the generic relay-failure wording, and a host at its cap has not failed to start anything — it has declined to start one more");
		text.Should().Contain("list-pages",
			because: "the refusal must name the call it refused, or a caller with several in flight cannot tell which one was declined");
	}
}
