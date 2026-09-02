using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Clio.Mcp.E2E;
using Clio.Tests.Command;
using Clio.Tests.Command.ProcessModel;
using Clio.Tests.Common;
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

	/// <summary>
	/// The repository root, four levels above the test output directory
	/// (<c>clio.tests/bin/&lt;configuration&gt;/&lt;framework&gt;</c>).
	/// </summary>
	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));


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

	[Test]
	[Description("Verifies that every destructive LocalOnly fixture stays [Explicit] and retains the McpE2E.Sandbox and McpE2E.Manual categories so it can never run automatically in CI.")]
	public void LocalOnlyDestructiveFixtures_ShouldStayExplicitAndRetainSandboxAndManual_WhenTheyTearDownSharedStand() {
		// Arrange
		IReadOnlyList<Type> localOnlyFixtures = GetFixturesWithCategory("LocalOnly");

		// Act
		Type[] misconfigured = localOnlyFixtures
			.Where(fixture => fixture.GetCustomAttribute<ExplicitAttribute>(inherit: true) is null
				|| !FixtureHasCategory(fixture, "McpE2E.Sandbox")
				|| !FixtureHasCategory(fixture, "McpE2E.Manual"))
			.OrderBy(fixture => fixture.FullName, StringComparer.Ordinal)
			.ToArray();

		// Assert
		localOnlyFixtures.Should().Contain(typeof(UninstallCreatioWarningE2ETests),
			because: "UninstallCreatioWarningE2ETests is a documented LocalOnly member; if it lost the category this invariant must fail rather than pass on the remaining fixture");
		localOnlyFixtures.Should().Contain(typeof(DbHubLifecycleWarningE2ETests),
			because: "DbHubLifecycleWarningE2ETests is the other documented LocalOnly member; asserting it explicitly stops a silent category drop from being masked by the open-ended scan");
		localOnlyFixtures.Should().Contain(typeof(DataBindingDbColorSchemaE2ETests),
			because: "DataBindingDbColorSchemaE2ETests publishes a schema through create-entity-schema, which starts the global OData rebuild; if it lost the category it would run in the automatic lane again and make every concurrent test on the shared stand fail with \"Creatio is currently rebuilding the OData library\"");
		misconfigured.Should().BeEmpty(
			because: "a destructive LocalOnly fixture must stay [Explicit] and keep McpE2E.Sandbox + McpE2E.Manual (additive-only per the tiering spec) so it never runs automatically in CI nor drops its tier classification");
	}

	[Test]
	[Description("Keeps the Creatio merge E2E fixtures explicit and manual so GitHub Actions and TeamCity never execute them automatically.")]
	public void CreatioMergeFixtures_ShouldStayExplicitAndManual_WhenTheyAreDeveloperLocal() {
		// Arrange
		Type[] fixtures = [
			typeof(CreatioArtifactMergeToolE2ETests),
			typeof(CreatioArtifactMergeGitLabE2ETests)
		];

		// Act
		Type[] misconfigured = fixtures
			.Where(fixture => fixture.GetCustomAttribute<ExplicitAttribute>(inherit: true) is null
				|| !FixtureHasCategory(fixture, "McpE2E.Manual"))
			.ToArray();

		// Assert
		misconfigured.Should().BeEmpty(
			because: "the feature owner requires all Creatio merge E2E coverage to remain outside automatic GitHub and TeamCity execution");
	}

	[Test]
	[Description("Asserts the off-stand tests that cover the uninstall warning contract still exist, since UninstallCreatioWarningE2ETests is [Explicit] and never runs in CI to catch a regression itself.")]
	public void UninstallWarningContract_ShouldStayCoveredOffStand_WhenExplicitFixtureNeverRunsInCi() {
		// Arrange
		(Type Fixture, string Method)[] coveringTests = [
			(typeof(CreatioUninstallerTestFixture),
				nameof(CreatioUninstallerTestFixture.UninstallByEnvironmentName_ShouldWarnAndContinueUnregister_WhenProfileDeletionFails)),
			(typeof(AppPoolProfileCleanerTests),
				nameof(AppPoolProfileCleanerTests.TryDelete_ShouldReturnWarningAfterThreeAttempts_WhenNativeDeletionKeepsFailing))
		];

		// Act
		string[] missing = coveringTests
			.Where(covering => covering.Fixture.GetMethod(covering.Method,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null)
			.Select(covering => $"{covering.Fixture.Name}.{covering.Method}")
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		// Assert
		missing.Should().BeEmpty(
			because: "the developer-local uninstall exemption relies on these off-stand tests as the only automated guard of the warning contract; a rename or removal must fail here and point back to the exemption rather than silently losing coverage");
	}

	[Test]
	[Description("Asserts the off-stand tests that cover the Color data-binding contract still exist, since DataBindingDbColorSchemaE2ETests is [Explicit] and never runs in CI to catch a regression itself.")]
	public void ColorDataBindingContract_ShouldStayCoveredOffStand_WhenExplicitFixtureNeverRunsInCi() {
		// Arrange
		(Type Fixture, string Method)[] coveringTests = [
			(typeof(SchemaTestFixture),
				nameof(SchemaTestFixture.FromRuntimeValueType_Should_Map_Color_To_NativeColorDataType)),
			(typeof(SchemaTestFixture),
				nameof(SchemaTestFixture.IsStringLike_Should_Reject_Color_So_It_Stays_Out_Of_Localization_Rows)),
			(typeof(DataBindingValueConverterTests),
				nameof(DataBindingValueConverterTests.ConvertValue_Should_Pass_Through_The_Hex_Literal_For_A_Color_Column)),
			(typeof(DataBindingValueConverterTests),
				nameof(DataBindingValueConverterTests.ConvertValue_Should_Reject_A_Numeric_Value_For_A_Color_Column)),
			(typeof(DataBindingValueConverterTests),
				nameof(DataBindingValueConverterTests.ConvertValue_Should_Reject_An_Object_Value_For_A_Color_Column)),
			(typeof(DataBindingDbCommandTests),
				nameof(DataBindingDbCommandTests.CreateDataBindingDb_Should_Support_Color_Runtime_Column)),
			(typeof(DataBindingDbCommandTests),
				nameof(DataBindingDbCommandTests.CreateDataBindingDb_Should_Reject_A_Numeric_Color_Value_Before_Any_Remote_Write)),
			(typeof(DataBindingDbCommandTests),
				nameof(DataBindingDbCommandTests.UpsertDataBindingRowDb_Should_Reject_An_Object_Color_Value_Before_Any_Remote_Write)),
			(typeof(DataBindingDbCommandTests),
				nameof(DataBindingDbCommandTests.UpsertDataBindingRowDb_Should_Preserve_Null_For_A_Color_Column))
		];

		// Act
		string[] missing = coveringTests
			.Where(covering => covering.Fixture.GetMethod(covering.Method,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null)
			.Select(covering => $"{covering.Fixture.Name}.{covering.Method}")
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		// Assert
		missing.Should().BeEmpty(
			because: "pulling the Color round-trip out of the automatic lane is only safe while these off-stand tests remain the automated guard of the Color mapping and its wire format; a rename or removal must fail here and point back to the exemption");
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

	[Test]
	[Category("Unit")]
	[Description("A fixture that needs outbound internet must not also carry a blocking tier category. Per-method categories are additive on top of the fixture tag, so leaving one in McpE2E.NoEnvironment keeps it in the pre-merge sweep whose gate is Total == Passed AND Skipped == 0 — an egress-blocked runner then fails the gate on the skip.")]
	public void LiveNetworkFixtures_ShouldNotCarryABlockingTierCategory() {
		// Arrange
		IReadOnlyList<Type> liveFixtures = GetFixturesWithCategory("McpE2E.LiveGoogleFonts");

		// Act
		IReadOnlyList<Type> leaked = liveFixtures
			.Where(fixture => FixtureHasCategory(fixture, "McpE2E.NoEnvironment")
				|| FixtureHasCategory(fixture, "McpE2E.Sandbox"))
			.ToArray();

		// Assert
		liveFixtures.Should().NotBeEmpty(
			because: "the live Google Fonts fixture carries this category; an empty set means this guard pins nothing");
		leaked.Should().BeEmpty(
			because: "a live-network fixture selected by a blocking tier filter turns an unreachable endpoint into a gate failure instead of an excluded test");
	}

	[Test]
	[TestCase(true, false, false, Description = "A stand-touching arrange without the opt-in is denied")]
	[TestCase(true, true, true, Description = "A stand-touching arrange with the opt-in runs")]
	[TestCase(false, false, true, Description = "An arrange that never reaches a stand needs no opt-in")]
	[Description("Pins the destructive-authorization decision: an arrange step that reaches a Creatio stand runs only under McpE2E:AllowDestructiveMcpTests.")]
	public void DestructiveStandAuthorization_ShouldAllowOnlyOptedInStandAccess(
		bool touchesStand, bool allowDestructiveMcpTests, bool expected) {
		// Act
		bool authorized = DestructiveStandAuthorization.IsAuthorized(touchesStand, allowDestructiveMcpTests);

		// Assert
		authorized.Should().Be(expected,
			because: "a fixture may mutate the configured sandbox only when the developer turned the destructive opt-in on");
	}

	[Test]
	[Description("Proves the DB-first data-binding arrange consults the destructive opt-in before it resolves the environment or runs any clio command, so a hand-selected fixture cannot mutate the stand while the opt-in is false.")]
	public void DataBindingDbArrange_ShouldCheckDestructiveOptIn_BeforeItRunsAnyCommand() {
		// Arrange
		string fixtureSourcePath = Path.Combine(
			RepositoryRoot, "clio.mcp.e2e", "DataBindingDbFixtureBase.cs");
		File.Exists(fixtureSourcePath).Should().BeTrue(
			because: $"this guard reads the arrange step from {fixtureSourcePath}; a moved file must fail here rather than pass on a missing source");
		string source = File.ReadAllText(fixtureSourcePath);

		// Act
		int authorizationIndex = source.IndexOf(
			nameof(DestructiveStandAuthorization) + "." + nameof(DestructiveStandAuthorization.IsAuthorized),
			StringComparison.Ordinal);
		int[] standTouchingIndexes = [
			source.IndexOf("ResolveReachableEnvironmentAsync(settings)", StringComparison.Ordinal),
			source.IndexOf("ClioCliCommandRunner.RunAndAssertSuccessAsync", StringComparison.Ordinal),
			source.IndexOf("ResolveFreshClioProcessPath", StringComparison.Ordinal)
		];

		// Assert
		authorizationIndex.Should().BeGreaterThan(-1,
			because: "the arrange step must consult DestructiveStandAuthorization.IsAuthorized; without it a hand-selected fixture pushes a package and publishes a schema on the configured stand with the opt-in off");
		standTouchingIndexes.Should().OnlyContain(index => index > -1,
			because: "this guard pins the order against the calls that actually reach the stand; if they were renamed the guard would silently pin nothing");
		standTouchingIndexes.Should().OnlyContain(index => index > authorizationIndex,
			because: "the opt-in has to be checked before the environment is resolved and before the first clio process is spawned, otherwise the guard runs after the damage");
	}

	private static bool HasCategory(IEnumerable<CategoryAttribute> attributes, string category) =>
		attributes.Any(attribute => string.Equals(attribute.Name, category, StringComparison.Ordinal));
}
