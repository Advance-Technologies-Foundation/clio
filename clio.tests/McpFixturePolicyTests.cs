using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
/// pre-merge Unit lane (<c>dotnet test clio.tests.csproj --filter "TestCategory!=Integration"</c>).
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

	[Test]
	[Description("Every fixture on the shared-server base keeps stand access and skips out of [OneTimeSetUp]: the server may start once per fixture, but the destructive opt-in, the sandbox-configuration check, the reachability probe and Assert.Ignore must stay inside test bodies, so NUnit still reports a missing stand per test instead of as a whole-fixture setup failure and no [OneTimeSetUp] ever reaches Creatio.")]
	public void SharedServerFixtures_ShouldKeepStandAccessAndSkipsOutOfOneTimeSetUp() {
		// Arrange
		string e2eRoot = Path.Combine(RepositoryRoot, "clio.mcp.e2e");
		Directory.Exists(e2eRoot).Should().BeTrue(
			because: $"this guard scans the e2e fixture sources under {e2eRoot}; a moved project must fail here rather than pass on an empty scan");
		// Every source in the project, subdirectories included: the two [OneTimeSetUp] methods that exist
		// today live in Support/Mcp (the shared-server base) and in the suite-level shared-home fixture, so
		// a scan limited to files that name a base type inspects nothing at all.
		string[] e2eSources = Directory.EnumerateFiles(e2eRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
				&& !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.OrderBy(path => path, StringComparer.Ordinal)
			.ToArray();

		// Act
		List<string> violations = [];
		int inspectedSetUpBodies = 0;
		foreach (string path in e2eSources) {
			IReadOnlyList<string> found = FindStandAccessInOneTimeSetUp(
				ReadSourceWithLfLineEndings(path),
				out int setUpBodies);
			inspectedSetUpBodies += setUpBodies;
			violations.AddRange(found.Select(violation => $"{Path.GetFileName(path)}: {violation}"));
		}

		// Assert
		inspectedSetUpBodies.Should().BeGreaterThanOrEqualTo(2,
			because: "the shared-server base and the shared-home fixture each declare one [OneTimeSetUp]; if the scan inspects fewer bodies than that it is matching nothing and the empty violation list below means nothing");
		violations.Should().BeEmpty(
			because: "a stand probe or an Assert.Ignore in [OneTimeSetUp] turns one unreachable sandbox into a whole-fixture failure (or a fixture-level skip that hides which test needed the stand), which is the exact regression the shared-server conversion was reviewed against");
	}

	[Test]
	[Description("Proves the [OneTimeSetUp] scanner actually reports a violation - directly and through one level of same-file helper indirection - so the empty result of the guard above is evidence rather than a silent no-match.")]
	public void OneTimeSetUpScanner_ShouldReportStandAccess_WhenItIsDirectOrOneHelperAway() {
		// Arrange
		const string cleanSource = """
			public sealed class Clean {
				[OneTimeSetUp]
				public void SetUp() {
					string home = Path.Combine(Path.GetTempPath(), "x");
					Directory.CreateDirectory(home);
				}

				[Test]
				public void Probe() {
					Assert.Ignore("the skip belongs here, in the test body");
				}
			}
			""";
		const string directViolationSource = """
			public sealed class Direct {
				[OneTimeSetUp]
				public void SetUp() {
					Assert.Ignore("skipping the whole fixture");
				}
			}
			""";
		const string indirectViolationSource = """
			public sealed class Indirect {
				[OneTimeSetUp]
				public async Task SetUpAsync() {
					await EnsureStandAsync();
				}

				private static async Task EnsureStandAsync() {
					await ClioCliCommandRunner.RunAndAssertSuccessAsync("ping-app");
				}
			}
			""";

		// Act
		IReadOnlyList<string> onClean = FindStandAccessInOneTimeSetUp(cleanSource, out int cleanBodies);
		IReadOnlyList<string> onDirect = FindStandAccessInOneTimeSetUp(directViolationSource, out int directBodies);
		IReadOnlyList<string> onIndirect = FindStandAccessInOneTimeSetUp(indirectViolationSource, out int indirectBodies);

		// Assert
		cleanBodies.Should().Be(1, because: "the clean sample declares exactly one [OneTimeSetUp] and the scanner must find its body");
		onClean.Should().BeEmpty(
			because: "an Assert.Ignore inside a [Test] body is the required shape, so the scanner must not flag it as a setup violation");
		directBodies.Should().Be(1, because: "the direct sample declares one [OneTimeSetUp]");
		onDirect.Should().ContainSingle(violation => violation.Contains("Assert.Ignore", StringComparison.Ordinal),
			because: "an Assert.Ignore written straight into [OneTimeSetUp] is the regression this guard exists to catch");
		indirectBodies.Should().Be(1, because: "the indirect sample declares one [OneTimeSetUp]");
		onIndirect.Should().ContainSingle(violation => violation.Contains("ClioCliCommandRunner", StringComparison.Ordinal),
			because: "moving the stand call into a private helper is the established shape in this suite (Ensure*Async), so hiding it one call away must not defeat the guard");
	}

	[Test]
	[Description("Every [Parallelizable(ParallelScope.Self)] e2e fixture stays free of the two things that CAN collide inside the parallel pool itself: a process-global Environment.SetEnvironmentVariable (leaks into whichever child server another worker is starting at that moment) and a TemporaryClioSettingsOverride (a bare read-modify-write of an appsettings.json that, without a fixture-owned home, is the suite-shared one every other pooled server reads). Stand safety is covered by SandboxFixtures_ShouldBeNonParallelizable; this guard is about pool-internal isolation.")]
	public void ParallelFixtures_ShouldNotMutateProcessEnvironmentOrSharedSettings() {
		// Arrange
		string e2eRoot = Path.Combine(RepositoryRoot, "clio.mcp.e2e");
		Directory.Exists(e2eRoot).Should().BeTrue(
			because: $"this guard scans the e2e fixture sources under {e2eRoot}; a moved project must fail here rather than pass on an empty scan");
		// Any class-level [Parallelizable] on its own line: bare [Parallelizable] means ParallelScope.Self in
		// NUnit, and ParallelScope.All / .Fixtures join the same worker pool, so anchoring on the explicit
		// ".Self" spelling alone would let a fixture into the pool completely unscreened.
		const string parallelAttribute = "\n[Parallelizable";
		string[] forbiddenInParallelFixtures = [
			"Environment.SetEnvironmentVariable(",
			"TemporaryClioSettingsOverride."
		];
		string[] parallelFixtureSources = Directory.EnumerateFiles(e2eRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => {
				string source = ReadSourceWithLfLineEndings(path);
				return source.Contains(parallelAttribute, StringComparison.Ordinal)
					&& !source.Contains("\n[Parallelizable(ParallelScope.None)]\n", StringComparison.Ordinal);
			})
			.OrderBy(path => path, StringComparer.Ordinal)
			.ToArray();

		// Act
		string[] violations = parallelFixtureSources
			.SelectMany(path => {
				string source = ReadSourceWithLfLineEndings(path);
				return forbiddenInParallelFixtures
					.Where(token => source.Contains(token, StringComparison.Ordinal))
					.Select(token => $"{Path.GetFileName(path)}: parallel fixture uses {token}");
			})
			.ToArray();

		// Assert
		parallelFixtureSources.Should().HaveCountGreaterThanOrEqualTo(65,
			because: "the pool is 67 fixture classes in 65 files - the 26 files vetted by ENG-92558 plus the 39 added by PR #1427; a smaller set means the scan missed the sources and this guard pins nothing");
		violations.Should().BeEmpty(
			because: "a pooled fixture that poisons the test-host environment or rewrites the shared appsettings.json turns an unrelated pooled fixture red with a failure that points nowhere near the change - the flake class ENG-94529 and TeamCity 15893259 already paid for once; move such a fixture back to [NonParallelizable] or give it a fixture-owned CLIO_HOME instead");
	}

	private static bool HasCategory(IEnumerable<CategoryAttribute> attributes, string category) =>
		attributes.Any(attribute => string.Equals(attribute.Name, category, StringComparison.Ordinal));

	/// <summary>
	/// Reads a fixture source with line endings normalized to LF, so the line-anchored patterns above
	/// ("\n[Parallelizable(ParallelScope.Self)]\n") match on a Windows checkout where core.autocrlf
	/// turned every line ending into CRLF.
	/// </summary>
	private static string ReadSourceWithLfLineEndings(string path) =>
		File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

	/// <summary>
	/// Calls that must never run in an <c>[OneTimeSetUp]</c>: they reach the stand or skip the fixture,
	/// and both belong in a test body so NUnit reports the outcome per test.
	/// </summary>
	private static readonly string[] StandAccessForbiddenInOneTimeSetUp = [
		"Assert.Ignore",
		"ResolveReachableEnvironmentAsync",
		"ResolveEnvironmentOnceAsync",
		"ClioCliCommandRunner",
		"EnsureSandboxIsConfigured",
		"AllowDestructiveMcpTests"
	];

	/// <summary>Block keywords that a method-declaration scan must not mistake for a method name.</summary>
	private static readonly HashSet<string> BlockKeywords = new(StringComparer.Ordinal) {
		"if", "else", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "do", "try",
		"unsafe", "get", "set", "return", "new"
	};

	/// <summary>
	/// Reports every forbidden stand-access call reachable from an <c>[OneTimeSetUp]</c> in one source
	/// file, following one further level of same-file helper calls, and tells the caller how many setup
	/// bodies it actually inspected.
	/// </summary>
	/// <param name="source">The C# source to scan; CRLF is normalized internally.</param>
	/// <param name="inspectedSetUpBodies">Number of <c>[OneTimeSetUp]</c> bodies the scan parsed.</param>
	/// <returns>One entry per forbidden token reachable from a setup body; empty when the file is clean.</returns>
	/// <remarks>
	/// Helper indirection is followed because the established shape in this suite is a lazily invoked
	/// <c>Ensure*Async</c> that holds the stand call, so a direct-text scan of the setup body alone would
	/// miss exactly the regression this guard exists to catch.
	/// </remarks>
	private static IReadOnlyList<string> FindStandAccessInOneTimeSetUp(string source, out int inspectedSetUpBodies) {
		string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
		IReadOnlyDictionary<string, string> methodBodies = CollectMethodBodies(text);
		List<string> violations = [];
		inspectedSetUpBodies = 0;
		foreach (Match attribute in Regex.Matches(text, @"(?m)^[ \t]*\[OneTimeSetUp\][ \t]*$")) {
			int bodyStart = FindBodyBrace(text, attribute.Index + attribute.Length);
			if (bodyStart < 0) {
				continue;
			}
			string body = ExtractBracedBlock(text, bodyStart);
			inspectedSetUpBodies++;
			string reachable = body + "\n" + string.Join('\n', CollectCalledHelperBodies(body, methodBodies));
			violations.AddRange(StandAccessForbiddenInOneTimeSetUp
				.Where(token => reachable.Contains(token, StringComparison.Ordinal))
				.Select(token => $"[OneTimeSetUp] reaches {token}"));
		}
		return violations;
	}

	/// <summary>
	/// Returns the index of the brace that opens the member declared after <paramref name="searchFrom"/>,
	/// or -1 when that member is expression-bodied or abstract (a ';' comes first).
	/// </summary>
	private static int FindBodyBrace(string text, int searchFrom) {
		int brace = text.IndexOf('{', searchFrom);
		int semicolon = text.IndexOf(';', searchFrom);
		return brace >= 0 && (semicolon < 0 || brace < semicolon) ? brace : -1;
	}

	/// <summary>Returns the balanced <c>{ … }</c> block that starts at <paramref name="openBrace"/>.</summary>
	private static string ExtractBracedBlock(string text, int openBrace) {
		int depth = 0;
		for (int index = openBrace; index < text.Length; index++) {
			depth += text[index] switch { '{' => 1, '}' => -1, _ => 0 };
			if (depth == 0) {
				return text[openBrace..(index + 1)];
			}
		}
		return text[openBrace..];
	}

	/// <summary>Maps every method name declared with a block body in one source file to that body.</summary>
	private static IReadOnlyDictionary<string, string> CollectMethodBodies(string text) {
		Dictionary<string, string> bodies = new(StringComparer.Ordinal);
		foreach (Match declaration in Regex.Matches(text, @"(?m)^[ \t]+(?:[\w<>,\[\]\?\.\s]+\s)?(\w+)\s*\([^;{}]*\)[^;{}\n]*\{")) {
			string name = declaration.Groups[1].Value;
			if (BlockKeywords.Contains(name)) {
				continue;
			}
			string body = ExtractBracedBlock(text, declaration.Index + declaration.Length - 1);
			bodies[name] = bodies.TryGetValue(name, out string? existing) ? existing + "\n" + body : body;
		}
		return bodies;
	}

	/// <summary>Returns the bodies of same-file methods invoked from <paramref name="body"/>, one level deep.</summary>
	private static IReadOnlyList<string> CollectCalledHelperBodies(string body, IReadOnlyDictionary<string, string> methodBodies) =>
		Regex.Matches(body, @"\b(\w+)\s*\(")
			.Select(call => call.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.Where(name => methodBodies.ContainsKey(name))
			.Select(name => methodBodies[name])
			.ToArray();
}
