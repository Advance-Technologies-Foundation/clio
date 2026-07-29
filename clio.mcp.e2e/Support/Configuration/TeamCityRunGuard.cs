using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Detects whether the current process runs under a TeamCity build agent, so destructive
/// developer-local fixtures (for example <c>UninstallCreatioWarningE2ETests</c> and
/// <c>DbHubLifecycleWarningE2ETests</c>) can short-circuit with <c>Assert.Ignore</c> and never
/// tear down a shared stand in CI. Exposed as a testable predicate (rather than an inline
/// <c>Environment.GetEnvironmentVariable</c> check duplicated per fixture) so the guard's
/// behavior is verified by a focused unit test.
/// </summary>
public static class TeamCityRunGuard {
	private const string TeamCityVersionVariable = "TEAMCITY_VERSION";

	/// <summary>
	/// Returns <see langword="true"/> when the <c>TEAMCITY_VERSION</c> environment variable is
	/// present and non-blank, which TeamCity sets on every build agent.
	/// </summary>
	public static bool IsRunningUnderTeamCity() =>
		IsRunningUnderTeamCity(Environment.GetEnvironmentVariable(TeamCityVersionVariable));

	/// <summary>
	/// Pure overload evaluating a supplied <c>TEAMCITY_VERSION</c> value, so the null/blank
	/// contract can be asserted deterministically without mutating process environment state.
	/// </summary>
	/// <param name="teamCityVersion">The <c>TEAMCITY_VERSION</c> value to evaluate.</param>
	public static bool IsRunningUnderTeamCity(string? teamCityVersion) =>
		!string.IsNullOrWhiteSpace(teamCityVersion);

	/// <summary>
	/// Skips the calling test with <c>Assert.Ignore</c> when running under TeamCity. Single shared
	/// check-and-ignore entry point so destructive developer-local fixtures cannot independently
	/// diverge (a trailing <c>return</c>, a forgotten <c>Assert.Ignore</c>) or drift from one another.
	/// </summary>
	/// <param name="reason">Human-readable skip reason surfaced in the test report.</param>
	public static void IgnoreIfRunningUnderTeamCity(string reason) {
		if (IsRunningUnderTeamCity()) {
			Assert.Ignore(reason);
		}
	}
}
