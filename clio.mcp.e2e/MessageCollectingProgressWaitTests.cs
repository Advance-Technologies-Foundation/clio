using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using FluentAssertions;
using ModelContextProtocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Deterministic coverage for the bounded progress wait that <see cref="MessageCollectingProgress"/>
/// exposes (issue #1103). The sandbox tests that consume it can only run against a seeded stand, so the
/// wait mechanism itself — the part that replaced the racy "assert whatever arrived" pattern — is proved
/// here with a hand-driven sink and no environment at all.
/// </summary>
[TestFixture]
[AllureNUnit]
public sealed class MessageCollectingProgressWaitTests {
	private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(30);

	private static ProgressNotificationValue Beat(string message) => new() { Progress = 0, Message = message };

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Verifies the bounded progress wait returns a notification that is reported AFTER the wait began, which is the dispatch race that made the application progress tests flaky.")]
	[AllureFeature("mcp-progress-heartbeat")]
	[AllureTag("mcp-progress-heartbeat")]
	[AllureName("Progress wait observes a notification delivered after the wait started")]
	[AllureDescription("Starts the bounded wait against an empty sink, then reports the awaited marker, and verifies the wait completes with the marker instead of failing on the empty snapshot it saw first.")]
	public async Task WaitForMessages_Should_Observe_Notification_Reported_After_Wait_Started() {
		// Arrange
		MessageCollectingProgress progress = new();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(1));
		Task<IReadOnlyList<string>> wait = progress.WaitForMessagesAsync(
			messages => messages.Any(message => message.Contains("loading application metadata", StringComparison.Ordinal)),
			GenerousTimeout,
			cancellationTokenSource.Token);

		// Act — deliver the marker only after the wait is already pending, reproducing the SDK's
		// independent-continuation delivery of a notification the server had already sent.
		progress.Report(Beat("3/3: loading application metadata"));
		IReadOnlyList<string> observed = await wait;

		// Assert
		observed.Should().ContainSingle(message => message.Contains("loading application metadata", StringComparison.Ordinal),
			because: "the bounded wait must observe a notification delivered after it started, which is exactly the race that made the immediate assertion flaky");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Verifies the bounded progress wait returns immediately when the awaited condition is already satisfied, so a wait never costs wall-clock time on the healthy path.")]
	[AllureFeature("mcp-progress-heartbeat")]
	[AllureTag("mcp-progress-heartbeat")]
	[AllureName("Progress wait returns immediately when already satisfied")]
	[AllureDescription("Reports the markers before waiting and verifies the bounded wait returns the existing snapshot without waiting for a further notification.")]
	public async Task WaitForMessages_Should_Return_Immediately_When_Condition_Already_Satisfied() {
		// Arrange
		MessageCollectingProgress progress = new();
		progress.Report(Beat("1/3: enriching application model"));
		progress.Report(Beat("2/3: creating application package"));
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(1));

		// Act — no further notification is ever reported, so this can only complete from the existing snapshot.
		IReadOnlyList<string> observed = await progress.WaitForMessagesAsync(
			messages => messages.Count >= 2, GenerousTimeout, cancellationTokenSource.Token);

		// Assert
		observed.Should().HaveCount(2,
			because: "an already-satisfied condition must resolve from the current snapshot rather than blocking for another notification");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Verifies the bounded progress wait fails with a diagnostic that lists the notifications that DID arrive, so a genuinely missing stage marker is still reported precisely instead of as a bare timeout.")]
	[AllureFeature("mcp-progress-heartbeat")]
	[AllureTag("mcp-progress-heartbeat")]
	[AllureName("Progress wait timeout names the notifications that arrived")]
	[AllureDescription("Reports two of three markers, waits for the third with a short timeout, and verifies the TimeoutException enumerates the observed markers.")]
	public async Task WaitForMessages_Should_Time_Out_With_Diagnostic_Listing_Observed_Messages() {
		// Arrange
		MessageCollectingProgress progress = new();
		progress.Report(Beat("1/3: enriching application model"));
		progress.Report(Beat("2/3: creating application package"));
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(1));

		// Act
		Func<Task> act = async () => await progress.WaitForMessagesAsync(
			messages => messages.Any(message => message.Contains("loading application metadata", StringComparison.Ordinal)),
			TimeSpan.FromMilliseconds(200),
			cancellationTokenSource.Token);

		// Assert
		(await act.Should().ThrowAsync<TimeoutException>(
				because: "a marker that never arrives must fail the wait rather than hang until the fixture token fires"))
			.And.Message.Should()
			.Contain("enriching application model",
				because: "the timeout diagnostic must name the markers that DID arrive so a genuinely missing marker is actionable")
			.And.Contain("creating application package",
				because: "every observed marker belongs in the diagnostic, not just the first");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Verifies the count-based bounded wait resolves once the requested number of notifications has been observed, covering the keep-alive assertion path.")]
	[AllureFeature("mcp-progress-heartbeat")]
	[AllureTag("mcp-progress-heartbeat")]
	[AllureName("Progress wait resolves on notification count for keep-alive assertions")]
	[AllureDescription("Starts a count-based wait against an empty sink, reports one heartbeat, and verifies the wait resolves — the keep-alive path used by the long-running-call test.")]
	public async Task WaitForCount_Should_Resolve_When_Minimum_Notification_Count_Observed() {
		// Arrange
		MessageCollectingProgress progress = new();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(1));
		Task<IReadOnlyList<string>> wait = progress.WaitForCountAsync(
			minimumCount: 1, GenerousTimeout, cancellationTokenSource.Token);

		// Act
		progress.Report(Beat("list-app-sections is still running… (~1s elapsed)"));
		IReadOnlyList<string> observed = await wait;

		// Assert
		observed.Should().HaveCount(1,
			because: "the keep-alive assertion must resolve as soon as the first heartbeat is observed, whenever it is dispatched");
	}
}
