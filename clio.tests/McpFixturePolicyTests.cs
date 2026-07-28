using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Mcp.E2E;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Guard tests for MCP e2e fixture scheduling policy.
/// </summary>
/// <remarks>
/// This guard lives in <c>clio.tests</c> (not <c>clio.mcp.e2e</c>) so it runs in the standard
/// pre-merge Unit lane (<c>dotnet test clio.tests.csproj --filter "Category!=Integration"</c>).
/// It reflects over the <c>clio.mcp.e2e</c> assembly via a project reference; the e2e tests
/// themselves are not executed here.
/// </remarks>
[TestFixture]
[Category("Unit")]
public sealed class McpFixturePolicyTests {

	[Test]
	[Description("Verifies that every fixture containing Sandbox tests is class-level NonParallelizable.")]
	public void SandboxFixtures_ShouldBeNonParallelizable_WhenTheyContainSandboxTests() {
		// Arrange
		IReadOnlyList<Type> sandboxFixtures = GetFixturesWithCategory("McpE2E.Sandbox");

		// Act
		Type[] missingGuard = sandboxFixtures
			.Where(fixture => fixture.GetCustomAttribute<NonParallelizableAttribute>(inherit: true) is null)
			.OrderBy(fixture => fixture.FullName, StringComparer.Ordinal)
			.ToArray();

		// Assert
		missingGuard.Should().BeEmpty(
			because: "Sandbox tests touch the shared destructive stand and must never run in parallel");
	}

	[Test]
	[Description("Verifies that NoEnvironment-only fixtures are not forced to be NonParallelizable by the Sandbox guard.")]
	public void SandboxFixtureGuard_ShouldIgnoreNoEnvironmentOnlyFixtures() {
		// Arrange
		IReadOnlyList<Type> noEnvironmentOnlyFixtures = GetFixturesWithCategory("McpE2E.NoEnvironment")
			.Where(fixture => !FixtureHasCategory(fixture, "McpE2E.Sandbox"))
			.ToArray();

		// Act
		bool hasNoEnvironmentOnlyFixtures = noEnvironmentOnlyFixtures.Count > 0;

		// Assert
		hasNoEnvironmentOnlyFixtures.Should().BeTrue(
			because: "the policy guard should remain scoped to Sandbox fixtures only");
		noEnvironmentOnlyFixtures.Should().Contain(typeof(ExperimentalToolE2ETests),
			because: "ExperimentalToolE2ETests is a known NoEnvironment-only fixture and should not be treated as Sandbox");
	}

	private static IReadOnlyList<Type> GetFixturesWithCategory(string category) =>
		// Anchor reflection on a clio.mcp.e2e type so the guard scans the e2e assembly, not clio.tests.
		typeof(ExperimentalToolE2ETests).Assembly.GetTypes()
			.Where(type => type.IsClass && FixtureHasCategory(type, category))
			.OrderBy(type => type.FullName, StringComparer.Ordinal)
			.ToArray();

	private static bool FixtureHasCategory(Type fixtureType, string category) =>
		HasCategory(fixtureType.GetCustomAttributes<CategoryAttribute>(inherit: true), category)
		|| fixtureType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Any(method => HasCategory(method.GetCustomAttributes<CategoryAttribute>(inherit: true), category));

	private static bool HasCategory(IEnumerable<CategoryAttribute> attributes, string category) =>
		attributes.Any(attribute => string.Equals(attribute.Name, category, StringComparison.Ordinal));
}
