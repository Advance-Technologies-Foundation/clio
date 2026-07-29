using System;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Unit coverage for the TeamCity run guard used by destructive developer-local e2e fixtures.
/// </summary>
/// <remarks>
/// Lives in <c>clio.tests</c> so it runs in the standard pre-merge Unit lane. The e2e fixtures that
/// use the guard are <c>[Explicit]</c> and never execute in CI, so this test is the only thing that
/// exercises the guard's actual short-circuit behavior — catching an inverted check, a whitespace
/// regression, or a typo in the <c>TEAMCITY_VERSION</c> variable name before it could let a
/// destructive uninstall run on a shared stand.
/// </remarks>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
public sealed class TeamCityRunGuardTests {

	[TestCase(null, false)]
	[TestCase("", false)]
	[TestCase("   ", false)]
	[TestCase("2024.1", true)]
	[TestCase(" 2024.1 ", true)]
	[Description("The TeamCity run predicate treats a null or blank TEAMCITY_VERSION as not-under-TeamCity and any non-blank value as under-TeamCity.")]
	public void IsRunningUnderTeamCity_ShouldReflectTeamCityVersionValue_WhenValueProvided(
		string? teamCityVersion, bool expected) {
		// Arrange

		// Act
		bool result = TeamCityRunGuard.IsRunningUnderTeamCity(teamCityVersion);

		// Assert
		result.Should().Be(expected,
			because: "the destructive-uninstall guard must skip only when TEAMCITY_VERSION is present and non-blank");
	}

	[Test]
	[Description("The parameterless predicate reads the real TEAMCITY_VERSION environment variable, so a typo in the variable name or an inverted read would be caught.")]
	public void IsRunningUnderTeamCity_ShouldReadTeamCityVersionEnvironmentVariable_WhenSetAndCleared() {
		// Arrange
		string? originalValue = Environment.GetEnvironmentVariable("TEAMCITY_VERSION");
		try {
			// Act + Assert: a set variable is detected as a TeamCity run.
			Environment.SetEnvironmentVariable("TEAMCITY_VERSION", "2024.1");
			TeamCityRunGuard.IsRunningUnderTeamCity().Should().BeTrue(
				because: "a non-blank TEAMCITY_VERSION in the environment must be detected as a TeamCity run");

			// Act + Assert: a cleared variable is detected as a non-TeamCity run.
			Environment.SetEnvironmentVariable("TEAMCITY_VERSION", null);
			TeamCityRunGuard.IsRunningUnderTeamCity().Should().BeFalse(
				because: "an absent TEAMCITY_VERSION must be detected as a non-TeamCity run so local runs are not skipped");
		}
		finally {
			Environment.SetEnvironmentVariable("TEAMCITY_VERSION", originalValue);
		}
	}
}
