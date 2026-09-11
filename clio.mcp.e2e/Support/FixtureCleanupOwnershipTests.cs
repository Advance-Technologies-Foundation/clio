using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support;

/// <summary>
/// Pins the two teardown guarantees the DB-first data-binding fixtures rely on: a cleanup command can never
/// hang or throw its way out of teardown, and a failure after the first side effect still releases what the
/// arrange step already created.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
public sealed class FixtureCleanupOwnershipTests {
	[Test]
	[Description("A cleanup command that never returns is cancelled by its own bounded token instead of hanging the E2E worker.")]
	public async Task BoundedCleanup_Should_Cancel_A_Hung_Cleanup_Command() {
		// Arrange
		TimeSpan timeout = TimeSpan.FromMilliseconds(200);

		// Act
		string? diagnostics = await BoundedCleanup.RunAsync(
			async cancellationToken => {
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return 0;
			},
			timeout,
			"Deleting the fixture package");

		// Assert
		diagnostics.Should().NotBeNull(
			because: "a cleanup that had to be cancelled must leave a trace saying what may need removing by hand");
		diagnostics.Should().Contain("cancelled",
			because: "the diagnostics have to distinguish a timed-out cleanup from a plain non-zero exit");
	}

	[Test]
	[Description("A cleanup command that throws is reported, not propagated, so teardown cannot mask the real test result.")]
	public async Task BoundedCleanup_Should_Report_A_Throwing_Cleanup_Command() {
		// Arrange
		const string failureMessage = "clio executable is missing";

		// Act
		string? diagnostics = await BoundedCleanup.RunAsync(
			_ => throw new IOException(failureMessage),
			TimeSpan.FromSeconds(5),
			"Deleting the fixture package");

		// Assert
		diagnostics.Should().NotBeNull(
			because: "a failed teardown is reported through the test output rather than thrown");
		diagnostics.Should().Contain(failureMessage,
			because: "the underlying reason is what makes the leftover actionable");
	}

	[Test]
	[Description("A non-zero exit code is reported, and a successful cleanup produces no diagnostics at all.")]
	public async Task BoundedCleanup_Should_Report_Only_A_Failing_Exit_Code() {
		// Act
		string? failed = await BoundedCleanup.RunAsync(
			_ => Task.FromResult(1),
			TimeSpan.FromSeconds(5),
			"Deleting the fixture package");
		string? succeeded = await BoundedCleanup.RunAsync(
			_ => Task.FromResult(0),
			TimeSpan.FromSeconds(5),
			"Deleting the fixture package");

		// Assert
		failed.Should().Contain("exit 1",
			because: "a package the stand refused to delete still has to be named in the output");
		succeeded.Should().BeNull(
			because: "a clean teardown must stay silent, otherwise the real failures drown in noise");
	}

	[Test]
	[Description("An arrange step that fails after the first side effect still disposes the context, and the original failure propagates.")]
	public async Task ArrangeOwnership_Should_Dispose_When_A_Later_Arrange_Step_Fails() {
		// Arrange
		TrackedResource resource = new();

		// Act
		Func<Task> act = () => ArrangeOwnership.CompleteOrDisposeAsync(
			resource,
			() => throw new InvalidOperationException("push-workspace succeeded, pkg-hotfix did not"));

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*pkg-hotfix*",
				because: "compensation must not replace the arrange failure with a teardown one");
		resource.DisposeCount.Should().Be(1,
			because: "the remote package and the temporary workspace created before the failure still have to go");
	}

	[Test]
	[Description("A successful arrange hands the context to the caller undisposed, so the test can use it.")]
	public async Task ArrangeOwnership_Should_Return_The_Live_Resource_When_Arrange_Succeeds() {
		// Arrange
		TrackedResource resource = new();

		// Act
		TrackedResource returned = await ArrangeOwnership.CompleteOrDisposeAsync(resource, () => Task.CompletedTask);

		// Assert
		returned.Should().BeSameAs(resource,
			because: "the caller owns the context from here on");
		returned.DisposeCount.Should().Be(0,
			because: "disposing a successfully arranged context would delete the package the test is about to use");
	}

	private sealed class TrackedResource : IAsyncDisposable {
		public int DisposeCount { get; private set; }

		public ValueTask DisposeAsync() {
			DisposeCount++;
			return ValueTask.CompletedTask;
		}
	}
}
