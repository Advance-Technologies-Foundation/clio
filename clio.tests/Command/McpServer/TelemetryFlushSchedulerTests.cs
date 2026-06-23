using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.Telemetry;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class TelemetryFlushSchedulerTests
{
	private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

	[Test]
	[Category("Unit")]
	[Description("Runs the flush service once in the background when the scheduler is idle.")]
	public async Task TryScheduleFlush_Should_Run_Flush_When_Idle()
	{
		// Arrange
		(ITelemetryFlushService service, TaskCompletionSource started, TaskCompletionSource release) = CreateControllableService();
		TelemetryFlushScheduler scheduler = new(service);

		// Act
		scheduler.TryScheduleFlush();
		await started.Task.WaitAsync(WaitTimeout);
		release.SetResult();
		await scheduler.DrainAsync(WaitTimeout);

		// Assert
		await service.Received(1).FlushAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("Skips a new flush while one is already running and accepts the next one after it completes.")]
	public async Task TryScheduleFlush_Should_Skip_When_Flush_Already_Running()
	{
		// Arrange
		(ITelemetryFlushService service, TaskCompletionSource started, TaskCompletionSource release) = CreateControllableService();
		TelemetryFlushScheduler scheduler = new(service);

		// Act
		scheduler.TryScheduleFlush();
		await started.Task.WaitAsync(WaitTimeout);
		scheduler.TryScheduleFlush();
		scheduler.TryScheduleFlush();
		release.SetResult();
		await scheduler.DrainAsync(WaitTimeout);

		// Assert
		await service.Received(1).FlushAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("DrainAsync waits for the in-flight flush to finish within the timeout.")]
	public async Task DrainAsync_Should_Wait_For_InFlight_Flush_Within_Timeout()
	{
		// Arrange
		(ITelemetryFlushService service, TaskCompletionSource started, TaskCompletionSource release) = CreateControllableService();
		TelemetryFlushScheduler scheduler = new(service);
		scheduler.TryScheduleFlush();
		await started.Task.WaitAsync(WaitTimeout);

		// Act
		Task drain = scheduler.DrainAsync(WaitTimeout);
		drain.IsCompleted.Should().BeFalse(
			because: "drain must wait while a flush is still uploading");
		release.SetResult();
		await drain.WaitAsync(WaitTimeout);

		// Assert
		drain.IsCompletedSuccessfully.Should().BeTrue(
			because: "drain should complete once the in-flight flush finishes");
	}

	[Test]
	[Category("Unit")]
	[Description("DrainAsync returns after the timeout when the in-flight flush hangs, keeping shutdown bounded.")]
	public async Task DrainAsync_Should_Return_When_Flush_Hangs_Beyond_Timeout()
	{
		// Arrange
		(ITelemetryFlushService service, TaskCompletionSource started, TaskCompletionSource release) = CreateControllableService();
		TelemetryFlushScheduler scheduler = new(service);
		scheduler.TryScheduleFlush();
		await started.Task.WaitAsync(WaitTimeout);

		// Act
		Func<Task> drain = () => scheduler.DrainAsync(TimeSpan.FromMilliseconds(100));

		// Assert
		await drain.Should().NotThrowAsync(
			because: "a hanging flush must not block or fail process shutdown beyond the drain timeout");
		release.SetResult();
	}

	[Test]
	[Category("Unit")]
	[Description("Releases the single-flight gate when the flush service faults so later flushes still run.")]
	public async Task TryScheduleFlush_Should_Not_Throw_And_Recover_When_Flush_Service_Faults()
	{
		// Arrange
		ITelemetryFlushService service = Substitute.For<ITelemetryFlushService>();
		service.FlushAsync(Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException(new InvalidOperationException("boom")));
		TelemetryFlushScheduler scheduler = new(service);

		// Act
		Action schedule = scheduler.TryScheduleFlush;
		schedule.Should().NotThrow(
			because: "background telemetry work must never surface failures to the caller");
		await scheduler.DrainAsync(WaitTimeout);
		scheduler.TryScheduleFlush();
		await scheduler.DrainAsync(WaitTimeout);

		// Assert
		await service.Received(2).FlushAsync(Arg.Any<CancellationToken>());
	}

	private static (ITelemetryFlushService Service, TaskCompletionSource Started, TaskCompletionSource Release)
		CreateControllableService()
	{
		TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ITelemetryFlushService service = Substitute.For<ITelemetryFlushService>();
		service.FlushAsync(Arg.Any<CancellationToken>()).Returns(_ => {
			started.TrySetResult();
			return release.Task;
		});
		return (service, started, release);
	}
}
