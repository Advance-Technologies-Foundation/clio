using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the read-response deadline gate + helper (ENG-93373): the retry-safe
/// classification predicate, deadline parsing, and the race that returns either the work result or a
/// structured <c>creatio-timeout</c> envelope.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpReadResponseDeadlineTests {

	private static readonly TimeSpan TinyDeadline = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan StopGuard = TimeSpan.FromSeconds(5);

	private static CallToolResult OkResult(string marker) => new() {
		IsError = false,
		Content = [new TextContentBlock { Text = marker }]
	};

	// ---- McpReadDeadlineGate.IsRetrySafe ------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A read-only, non-destructive tool is retry-safe.")]
	public void IsRetrySafe_ShouldBeTrue_WhenReadOnlyAndNonDestructive() {
		// Arrange
		// Act
		bool result = McpReadDeadlineGate.IsRetrySafe("list-pages", readOnly: true, destructive: false);

		// Assert
		result.Should().BeTrue(
			because: "a pure read is the canonical retry-safe operation");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page is admitted by name even though it is ReadOnly=false (it writes local .clio-pages files): it reads from Creatio and a retry re-reads + overwrites, so it is retry-safe.")]
	public void IsRetrySafe_ShouldBeTrue_WhenToolIsGetPageEvenIfNotReadOnly() {
		// Arrange
		// Act
		bool result = McpReadDeadlineGate.IsRetrySafe("get-page", readOnly: false, destructive: false);

		// Assert
		result.Should().BeTrue(
			because: "get-page reads from Creatio (ReadOnly=false only reflects its local file write) and must not be excluded");
	}

	[Test]
	[Category("Unit")]
	[Description("A destructive tool is never retry-safe.")]
	public void IsRetrySafe_ShouldBeFalse_WhenDestructive() {
		// Arrange
		// Act
		bool result = McpReadDeadlineGate.IsRetrySafe("delete-schema", readOnly: false, destructive: true);

		// Assert
		result.Should().BeFalse(
			because: "a destructive tool owns its own timeout contract; a 'safe to retry' timeout would be wrong");
	}

	[Test]
	[Category("Unit")]
	[Description("A non-read, non-destructive tool (e.g. an idempotent server write like install-gate) is NOT retry-safe — the read deadline must never bound a server write with 'safe to retry' guidance.")]
	public void IsRetrySafe_ShouldBeFalse_WhenIdempotentNonReadServerWrite() {
		// Arrange
		// Act — install-gate is ReadOnly=false, Destructive=false, Idempotent=true: an idempotent SERVER write.
		bool result = McpReadDeadlineGate.IsRetrySafe("install-gate", readOnly: false, destructive: false);

		// Assert
		result.Should().BeFalse(
			because: "an idempotent server write is safe for sequential re-runs but NOT for a retry issued while the abandoned first call is still mutating the server");
	}

	[Test]
	[Category("Unit")]
	[Description("Null annotations fail closed (not retry-safe) so an unannotated tool never gets the 'safe to retry' deadline.")]
	public void IsRetrySafe_ShouldFailClosed_WhenAnnotationsAreNull() {
		// Arrange
		ToolAnnotations annotations = null;

		// Act
		bool result = McpReadDeadlineGate.IsRetrySafe("list-pages", annotations);

		// Assert
		result.Should().BeFalse(
			because: "an unannotated tool is treated as destructive/unknown and must not be bounded");
	}

	[Test]
	[Category("Unit")]
	[Description("The annotations overload derives read-only retry-safety from the hints (a read-only tool is retry-safe).")]
	public void IsRetrySafe_ShouldReadFromAnnotations_WhenReadOnlyNonDestructive() {
		// Arrange
		ToolAnnotations annotations = new() {
			ReadOnlyHint = true,
			DestructiveHint = false,
			IdempotentHint = true
		};

		// Act
		bool result = McpReadDeadlineGate.IsRetrySafe("list-apps", annotations);

		// Assert
		result.Should().BeTrue(
			because: "the overload must derive retry-safety from the annotation hints");
	}

	[Test]
	[Category("Unit")]
	[Description("The annotations overload excludes an idempotent non-read server write, even though it is non-destructive and idempotent.")]
	public void IsRetrySafe_ShouldExcludeIdempotentServerWrite_FromAnnotations() {
		// Arrange — the exact shape of install-gate / add-package-dependency.
		ToolAnnotations annotations = new() {
			ReadOnlyHint = false,
			DestructiveHint = false,
			IdempotentHint = true
		};

		// Act
		bool result = McpReadDeadlineGate.IsRetrySafe("add-package-dependency", annotations);

		// Assert
		result.Should().BeFalse(
			because: "the idempotent hint must NOT admit a server write to the read deadline");
	}

	// ---- McpReadResponseDeadline.ResolveDeadline ---------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("Parses a valid in-range override into the requested deadline.")]
	public void ResolveDeadline_ShouldUseOverride_WhenValueIsValid() {
		// Arrange
		// Act
		TimeSpan result = McpReadResponseDeadline.ResolveDeadline("90");

		// Assert
		result.Should().Be(TimeSpan.FromSeconds(90),
			because: "a valid override must be honoured so operators can tune the read budget");
	}

	[Test]
	[Category("Unit")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("not-a-number")]
	[TestCase("0")]
	[TestCase("601")]
	[TestCase("-5")]
	[Description("Falls back to the 120 s default for null, empty, non-numeric, or out-of-range (0 < n <= 600) overrides.")]
	public void ResolveDeadline_ShouldFallBackToDefault_WhenValueIsInvalidOrOutOfRange(string rawValue) {
		// Arrange
		// Act
		TimeSpan result = McpReadResponseDeadline.ResolveDeadline(rawValue);

		// Assert
		result.Should().Be(TimeSpan.FromSeconds(120),
			because: "invalid or out-of-range overrides must fall back to the safe 120 s default");
	}

	// ---- McpReadResponseDeadline.RunAsync ----------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("Returns the work's CallToolResult unchanged when the work completes within the deadline.")]
	public async Task RunAsync_ShouldReturnWorkResult_WhenWorkCompletesWithinDeadline() {
		// Arrange
		CallToolResult expected = OkResult("ok");

		// Act
		CallToolResult result = await McpReadResponseDeadline.RunAsync(
			"list-pages",
			_ => new ValueTask<CallToolResult>(expected),
			CancellationToken.None,
			TimeSpan.FromSeconds(5));

		// Assert
		result.Should().BeSameAs(expected,
			because: "a fast read must be transparent — the deadline wrapper returns the real result unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured creatio-timeout envelope when the work outlives the deadline, without throwing.")]
	public async Task RunAsync_ShouldReturnStructuredTimeout_WhenWorkExceedsDeadline() {
		// Arrange
		// Work blocks well past the tiny deadline.
		Func<CancellationToken, ValueTask<CallToolResult>> work = async token => {
			await Task.Delay(StopGuard, token);
			return OkResult("should-not-be-returned");
		};

		// Act
		CallToolResult result = await McpReadResponseDeadline.RunAsync(
			"list-pages", work, CancellationToken.None, TinyDeadline);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a timed-out read must be reported as an error result");
		JsonElement structured = result.StructuredContent!.Value;
		structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
			because: "the machine-readable error-class must match the shared timeout token");
		structured.GetProperty("read-response-timed-out").GetBoolean().Should().BeTrue(
			because: "the read envelope must be distinguishable from the write in-progress envelope");
		structured.GetProperty("retry-guidance").GetString().Should().NotBeNullOrWhiteSpace(
			because: "the agent needs machine-readable retry guidance, not just prose");
		structured.GetProperty("tool").GetString().Should().Be("list-pages",
			because: "the envelope must name the tool that timed out");
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates cancellation (not a timeout) when the request token is already cancelled.")]
	public async Task RunAsync_ShouldThrowCancellation_WhenRequestCancelled() {
		// Arrange
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();
		Func<CancellationToken, ValueTask<CallToolResult>> work = async token => {
			await Task.Delay(StopGuard, token);
			return OkResult("unreached");
		};

		// Act
		Func<Task> act = async () => await McpReadResponseDeadline.RunAsync(
			"list-pages", work, cts.Token, TimeSpan.FromSeconds(5));

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "genuine request cancellation must propagate, not be masked as a read timeout");
	}

	[Test]
	[Category("Unit")]
	[Description("The structured timeout result reports the elapsed deadline in whole seconds.")]
	public void CreateTimeoutResult_ShouldReportDeadlineSeconds() {
		// Arrange
		// Act
		CallToolResult result = McpReadResponseDeadline.CreateTimeoutResult("get-page", TimeSpan.FromSeconds(120));

		// Assert
		result.StructuredContent!.Value.GetProperty("deadline-seconds").GetInt32().Should().Be(120,
			because: "the agent should see the exact budget that elapsed");
	}
}
