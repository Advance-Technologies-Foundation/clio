using System;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class PollRetryPolicyTests {

	[Test]
	[Description("Verifies the channel is not considered healthy before any poll has ever succeeded")]
	public void IsChannelHealthy_ReturnsFalse_BeforeAnySuccessRecorded() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

		// Act
		bool healthy = sut.IsChannelHealthy(now);

		// Assert
		healthy.Should().BeFalse(because: "a channel that has never succeeded once cannot be trusted, even before any failure was recorded");
	}

	[Test]
	[Description("Verifies the channel is healthy immediately after a successful poll")]
	public void IsChannelHealthy_ReturnsTrue_ImmediatelyAfterSuccess() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.RecordSuccess(now);

		// Act
		bool healthy = sut.IsChannelHealthy(now);

		// Assert
		healthy.Should().BeTrue(because: "a poll just succeeded at this exact instant");
	}

	[Test]
	[Description("Verifies the channel is reported unhealthy once too much time has passed since the last success, so stale data during an outage is never trusted")]
	public void IsChannelHealthy_ReturnsFalse_OnceStaleAfterWindowElapsesSinceLastSuccess() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime successAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.RecordSuccess(successAt);
		DateTime longAfter = successAt.Add(PollRetryPolicy.StaleAfter).AddSeconds(1);

		// Act
		bool healthy = sut.IsChannelHealthy(longAfter);

		// Assert
		healthy.Should().BeFalse(because: "the last successful poll is older than the stale-after window, so the channel can no longer be trusted");
	}

	[Test]
	[Description("Verifies RecordSuccess resets the consecutive-failure counter back to zero")]
	public void RecordSuccess_ResetsConsecutiveFailures() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.RecordFailure(now);
		sut.RecordFailure(now);

		// Act
		sut.RecordSuccess(now);

		// Assert
		sut.ConsecutiveFailures.Should().Be(0, because: "a successful poll clears prior failures for the purpose of the next backoff calculation");
	}

	[Test]
	[Description("Verifies RecordFailure increments the consecutive-failure counter")]
	public void RecordFailure_IncrementsConsecutiveFailures() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

		// Act
		sut.RecordFailure(now);
		sut.RecordFailure(now);
		sut.RecordFailure(now);

		// Assert
		sut.ConsecutiveFailures.Should().Be(3, because: "three consecutive failures were recorded");
	}

	[Test]
	[Description("Verifies NextDelay equals the base delay after exactly one failure")]
	public void NextDelay_EqualsBaseDelay_AfterOneFailure() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.RecordFailure(now);

		// Act
		TimeSpan delay = sut.NextDelay;

		// Assert
		delay.Should().Be(PollRetryPolicy.BaseDelay, because: "the first failure should back off by exactly the base delay");
	}

	[Test]
	[Description("Verifies NextDelay grows exponentially with consecutive failures")]
	public void NextDelay_GrowsExponentially_WithConsecutiveFailures() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.RecordFailure(now);
		sut.RecordFailure(now);
		sut.RecordFailure(now);

		// Act
		TimeSpan delay = sut.NextDelay;

		// Assert
		delay.Should().Be(TimeSpan.FromSeconds(4), because: "the third consecutive failure should back off by base delay * 2^(3-1) = 4x");
	}

	[Test]
	[Description("Verifies NextDelay never exceeds the configured maximum delay, no matter how many consecutive failures occurred")]
	public void NextDelay_IsCappedAtMaxDelay_WithManyConsecutiveFailures() {
		// Arrange
		PollRetryPolicy sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		for (int i = 0; i < 10; i++) {
			sut.RecordFailure(now);
		}

		// Act
		TimeSpan delay = sut.NextDelay;

		// Assert
		delay.Should().Be(PollRetryPolicy.MaxDelay, because: "exponential growth must be capped so a flapping channel never waits an unbounded amount of time between attempts");
	}

}
