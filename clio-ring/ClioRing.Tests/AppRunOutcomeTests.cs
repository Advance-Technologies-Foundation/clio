using ClioRing.Ipc;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
public sealed class AppRunOutcomeTests {
	[TestCase(ClioStageEventContract.RunOutcomes.Success)]
	[TestCase(ClioStageEventContract.RunOutcomes.SuccessWithWarnings)]
	[Description("Successful terminal outcomes trigger the environment refresh path.")]
	public void IsSuccessfulRunOutcome_ShouldReturnTrue_WhenOutcomeCompletesSuccessfully(string outcome) {
		// Arrange

		// Act
		bool result = App.IsSuccessfulRunOutcome(outcome);

		// Assert
		result.Should().BeTrue(because: "environment registration changed after every successful uninstall outcome");
	}

	[Test]
	[Description("A failed terminal outcome does not trigger the environment refresh path.")]
	public void IsSuccessfulRunOutcome_ShouldReturnFalse_WhenOutcomeFailed() {
		// Arrange

		// Act
		bool result = App.IsSuccessfulRunOutcome(ClioStageEventContract.RunOutcomes.Failure);

		// Assert
		result.Should().BeFalse(because: "failed uninstall must retain the existing environment catalog state");
	}
}
