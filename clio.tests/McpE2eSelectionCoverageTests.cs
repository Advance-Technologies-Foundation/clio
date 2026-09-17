using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Mcp.E2E;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests;

/// <summary>
/// Guards the pull-request fixture selection for the TeamCity MCP e2e build
/// (<c>.github/scripts/Select-McpE2eTestFilter.ps1</c> driven by
/// <c>clio.mcp.e2e/TestSelection/mcp-e2e-selection.json</c>).
/// </summary>
/// <remarks>
/// The script owns the textual rules. This fixture runs it in-process (<c>-Inventory</c> and real
/// selections) and compares its view of <c>clio.mcp.e2e</c> with reflection over the compiled e2e
/// assembly: every runnable fixture the compiler knows must be one the text-based detector can see
/// and select, otherwise a code change silently stops running that fixture on pull requests. The
/// guard lives in <c>clio.tests</c> because nothing in <c>clio.mcp.e2e</c> runs before merge.
/// </remarks>
[TestFixture]
[Category("Unit")]
internal sealed class McpE2eSelectionCoverageTests {

	private static readonly string RepositoryRoot =
		FindRepositoryRoot(GetSourceDirectory(), Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

	private static readonly string ManifestPath =
		Path.Combine(RepositoryRoot, "clio.mcp.e2e", "TestSelection", "mcp-e2e-selection.json");

	private static readonly string ScriptPath =
		Path.Combine(RepositoryRoot, ".github", "scripts", "Select-McpE2eTestFilter.ps1");

	private static readonly string TriggerWorkflowPath =
		Path.Combine(RepositoryRoot, ".github", "workflows", "teamcity-mcp-e2e.yml");

	private static readonly string BuildWorkflowPath =
		Path.Combine(RepositoryRoot, ".github", "workflows", "build.yml");

	private static readonly string SupportDirectory = Path.Combine(RepositoryRoot, "clio.mcp.e2e", "Support");

	private static readonly string UnreachablePinPath =
		Path.Combine(RepositoryRoot, "clio.mcp.e2e", "TestSelection", "unreachable-product-files.txt");

	/// <summary>The script's inventory of the live tree, computed once per run.</summary>
	private static readonly Lazy<JsonElement> Inventory = new(() => RunScript(ps => ps.AddParameter("Inventory")));

	[Test]
	[Description("Every fixture the compiler knows in the Clio.Mcp.E2E namespace is one the script sees in a top-level clio.mcp.e2e/*.cs file, or is a harness self-test under Support/** (a full run by manifest), so no fixture is invisible to the text-based selection.")]
	public void Fixtures_ShouldBeVisibleToTheScript_OrLiveUnderSupport() {
		// Arrange
		HashSet<string> seenByScript = GetInventoryFixtureNames();
		IReadOnlyList<Type> compiled = GetFixtureTypes();

		// Act
		string[] invisible = compiled
			.Where(fixture => !seenByScript.Contains(fixture.Name) && !IsDeclaredUnderSupport(fixture.Name))
			.Select(fixture => fixture.Name)
			.ToArray();

		// Assert
		compiled.Should().NotBeEmpty(because: "the guard is meaningless if no fixture is discovered");
		invisible.Should().BeEmpty(
			because: "the script resolves a changed top-level clio.mcp.e2e/*.cs to the fixtures declared in it and treats Support/** as a full run; a fixture it cannot see is never selected by its own change");
	}

	[Test]
	[Description("Every name the script emits as a fixture is a real, runnable fixture type, so the filter never carries an abstract base or helper class that matches nothing and masks a missing selection.")]
	public void Script_ShouldEmitOnlyCompiledFixtures() {
		// Arrange
		HashSet<string> compiled = GetFixtureTypes().Select(fixture => fixture.Name).ToHashSet(StringComparer.Ordinal);

		// Act
		string[] phantom = GetInventoryFixtureNames().Where(name => !compiled.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

		// Assert
		phantom.Should().BeEmpty(
			because: "a name in the filter that is not a fixture selects nothing and the run looks green for a change that ran no test");
	}

	[Test]
	[Description("No fixture name is a prefix of another fixture name, because FullyQualifiedName~ is a substring match and a shorter name would select the longer fixture too or mask that it was not selected on its own.")]
	public void FixtureNames_ShouldNotBePrefixesOfEachOther() {
		// Arrange
		string[] names = GetFixtureTypes().Select(fixture => fixture.Name)
			.Concat(GetInventoryFixtureNames())
			.Distinct()
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToArray();

		// Act
		string[] collisions = names
			.SelectMany(shorter => names
				.Where(longer => longer != shorter && longer.StartsWith(shorter, StringComparison.Ordinal))
				.Select(longer => $"{shorter} is a prefix of {longer}"))
			.ToArray();

		// Assert
		collisions.Should().BeEmpty(
			because: "the subset filter matches FullyQualifiedName~Clio.Mcp.E2E.<Fixture> without a terminator, so a prefix collision selects the wrong fixture");
	}

	[Test]
	[Description("Every automatically run fixture is selected by at least one tool source file according to the script's own reachability, or is declared in the manifest as explicitly mapped or full-run-only, so a change to the code it exercises cannot leave it out of a pull-request run.")]
	public void EveryAutomaticFixture_ShouldBeReachableFromAToolFileOrDeclaredInTheManifest() {
		// Arrange
		JsonElement manifest = ReadManifest();
		HashSet<string> declared = GetManifestFixtureNames(manifest);
		JsonElement reachability = Inventory.Value.GetProperty("reachability");
		HashSet<string> seenByScript = GetInventoryFixtureNames();
		IReadOnlyList<Type> automaticFixtures = GetFixtureTypes()
			.Where(fixture => seenByScript.Contains(fixture.Name))
			// Manual and LocalOnly never run automatically; Category("Unit") fixtures are self-tests of
			// Support/** harness code, a full run by manifest, and exercise no MCP tool.
			.Where(fixture => !HasClassCategory(fixture, "McpE2E.Manual") && !HasClassCategory(fixture, "LocalOnly") && !HasClassCategory(fixture, "Unit"))
			.ToArray();

		// Act
		string[] unreachable = automaticFixtures
			.Where(fixture => !declared.Contains(fixture.Name))
			.Where(fixture => !reachability.TryGetProperty(fixture.Name, out JsonElement tools) || tools.GetArrayLength() == 0)
			.Select(fixture => fixture.Name)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		// Assert
		unreachable.Should().BeEmpty(
			because: "a fixture no tool file selects only runs when the whole suite runs; reference the tool class (e.g. `PageSyncTool.ToolName`) or its tool-name literal from the fixture, name it <Tool>*E2ETests, or add it to explicitMappings or fullRunOnlyFixtures in mcp-e2e-selection.json");
	}

	[Test]
	[Description("Every fixture the manifest lists as full-run-only or explicitly mapped still exists, so the manifest does not keep exempting a fixture that was renamed or deleted.")]
	public void ManifestFixtureNames_ShouldAllExist() {
		// Arrange
		HashSet<string> compiled = GetFixtureTypes().Select(fixture => fixture.Name).ToHashSet(StringComparer.Ordinal);

		// Act
		string[] stale = GetManifestFixtureNames(ReadManifest()).Where(name => !compiled.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

		// Assert
		stale.Should().BeEmpty(
			because: "a stale name in the manifest is dead configuration that hides the fact that the fixture it once covered is gone or renamed");
	}

	[Test]
	[Description("The manifest's base filter equals the default of the TeamCity parameter McpE2eTestFilter, and the NoEnvironment category is the one build.yml runs, so the three copies of the tier boundary agree.")]
	public void Manifest_ShouldKeepTheTeamCityBaseFilter_AndBuildWorkflowShouldRunTheNoEnvironmentTier() {
		// Arrange
		JsonElement manifest = ReadManifest();
		string baseFilter = manifest.GetProperty("baseFilter").GetString()!;
		string noEnvironment = manifest.GetProperty("noEnvironmentCategory").GetString()!;
		Dictionary<object, object> jobs = ReadWorkflowJobs(BuildWorkflowPath);
		Dictionary<object, object> noEnvironmentJob = GetMap(jobs, "mcp-e2e-noenvironment");
		Dictionary<object, object> unitShardJob = GetMap(jobs, "unit-test-shards");

		// Act
		string run = string.Join("\n", GetSteps(noEnvironmentJob).Where(step => step.ContainsKey("run")).Select(step => step["run"].ToString()));

		// Assert
		baseFilter.Should().Be("TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual",
			because: "this is the default of the TeamCity parameter McpE2eTestFilter; changing one side without the other changes what master builds run");
		noEnvironment.Should().Be("McpE2E.NoEnvironment",
			because: "the TeamCity pull-request filter excludes this category by name and build.yml must run exactly it");
		noEnvironmentJob["if"].Should().Be(unitShardJob["if"],
			because: "the NoEnvironment tier is a code gate and must run under the same change conditions as the unit shards");
		run.Should().Contain($"TestCategory={noEnvironment}",
			because: "the hosted job must run the tier the TeamCity pull-request run skips");
		foreach (string exclusion in baseFilter.Split('&')) {
			run.Should().Contain(exclusion,
				because: "the hosted job must keep every exclusion of the TeamCity base filter, or manual/process-designer fixtures start running without a stand");
		}
	}

	[Test]
	[Description("Every path the TeamCity trigger workflow listens to is classified by the manifest: a path that starts a run is a relevant path, and a path the trigger excludes is one the manifest ignores, so the two filters cannot drift apart.")]
	public void WorkflowTriggerPaths_ShouldAllBeClassifiedByTheManifest() {
		// Arrange
		JsonElement manifest = ReadManifest();
		HashSet<string> relevant = manifest.GetProperty("relevantPaths").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal);
		HashSet<string> ignored = manifest.GetProperty("ignoredPaths").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal);
		string[] triggerPaths = ReadTriggerPaths();

		// Act
		string[] unclassified = triggerPaths
			.Where(path => path.StartsWith('!')
				? !ignored.Contains(path[1..])
				: !relevant.Contains(path) && !ignored.Contains("!" + path))
			.ToArray();

		// Assert
		triggerPaths.Should().NotBeEmpty(because: "the workflow must declare the paths that queue the TeamCity build");
		unclassified.Should().BeEmpty(
			because: "a path that triggers the workflow but is not in relevantPaths is never classified, and a path the trigger excludes but the manifest does not ignore would be classified on a run started by some other file; both readings of the same filter have to agree");
	}

	[Test]
	[Description("Changing one tool source file selects a subset that contains the fixture named after it, excludes the NoEnvironment tier and keeps the base exclusions.")]
	public void Script_ShouldSelectSubset_WhenOnlyOneToolFileChanged() {
		// Arrange
		string[] changed = ["clio/Command/McpServer/Tools/PageSyncTool.cs", "clio/docs/commands/sync-pages.md"];

		// Act
		JsonElement selection = RunSelection(changed, includeNoEnvironment: false);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("subset",
			because: "a single tool file resolves to the fixtures that exercise it");
		selection.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().Contain("PageSyncToolE2ETests",
			because: "the fixture named after the tool must always be part of the tool's subset");
		string filter = selection.GetProperty("filter").GetString()!;
		filter.Should().Contain("FullyQualifiedName~Clio.Mcp.E2E.PageSyncToolE2ETests",
			because: "the filter has to name the fixture with its namespace to stay exact");
		filter.Should().Contain("TestCategory!=McpE2E.NoEnvironment",
			because: "the NoEnvironment tier runs on GitHub and must not be repeated on TeamCity");
		filter.Should().EndWith("TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual",
			because: "a subset must keep the exclusions the full run has");
	}

	[Test]
	[Description("Changing shared infrastructure forces a full run even when a tool file changed alongside it.")]
	public void Script_ShouldSelectFullRun_WhenSharedInfrastructureChanged() {
		// Arrange
		string[] changed = ["clio/BindingsModule.cs", "clio/Command/McpServer/Tools/PageSyncTool.cs"];

		// Act
		JsonElement selection = RunSelection(changed, includeNoEnvironment: false);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("full",
			because: "the composition root decides what every tool resolves, so no subset is safe");
		selection.GetProperty("filter").GetString().Should().Be(
			"TestCategory!=McpE2E.NoEnvironment&TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual",
			because: "a full pull-request run still hands the NoEnvironment tier to GitHub");
	}

	[Test]
	[Description("A full run that keeps the NoEnvironment tier sends no filter at all, so the TeamCity parameter default applies unchanged.")]
	public void Script_ShouldSendNoFilter_WhenFullRunKeepsNoEnvironmentTier() {
		// Arrange
		string[] changed = ["Directory.Packages.props"];

		// Act
		JsonElement selection = RunSelection(changed, includeNoEnvironment: true);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("full",
			because: "a package-version change can affect every fixture");
		selection.GetProperty("filter").GetString().Should().BeEmpty(
			because: "an empty filter means the queue script does not send McpE2eTestFilter and TeamCity uses its default");
	}

	[Test]
	[Description("A diff of nothing but documentation and generated help runs no e2e build at all, because none of it is compiled into clio or read by the harness.")]
	public void Script_ShouldRunNothing_WhenOnlyDocumentationChanged() {
		// Arrange
		string[] changed = ["README.md", "clio/docs/commands/ping.md", "clio/Commands.md", "clio/help/en/ping.txt", "clio/Wiki/WikiAnchors.txt", "clio.mcp.e2e/AGENTS.md"];

		// Act
		JsonElement selection = RunSelection(changed, includeNoEnvironment: false);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("none",
			because: "no fixture can observe a change to text that is never compiled or loaded, so deploying a Creatio for it is pure waste");
		selection.GetProperty("filter").GetString().Should().BeEmpty(
			because: "mode none must not hand TeamCity any filter");
		selection.GetProperty("decisions").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("ignored (documentation"),
			because: "the decision log must name the rule that discarded each file");
	}

	[Test]
	[Description("A changed product file that no MCP tool and no covered CLI verb consumes runs nothing, because no fixture in this suite executes that code.")]
	public void Script_ShouldRunNothing_WhenTheChangedCodeIsUnreachableFromTheMcpSurface() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement selection = RunSelection(["clio/Command/OrphanService.cs"], includeNoEnvironment: false, repo.Root);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("none",
			because: "OrphanService is named by no tool, reaches no verb a fixture spells out, and is registered nowhere, so no e2e test can show a regression in it");
		selection.GetProperty("decisions").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("no MCP tool and no covered CLI verb"),
			because: "skipping the build is only defensible when the log says which reachability check came back empty");
	}

	[Test]
	[Description("In a synthetic repository, a product file is selected through the transitive consumer graph: a direct tool consumer, a consumer one hop away, a DI-registered implementation reached only through its interface, a CLI verb a fixture spells out, and an asset a tool loads by file name.")]
	public void Script_ShouldFollowTheReferenceGraph_ForProductFilesOutsideTools() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();
		string[] alphaFixtures = ["AlphaToolE2ETests", "AlphaLiteralE2ETests", "AlphaContractE2ETests", "AlphaLegacyE2ETests"];

		// Act
		JsonElement directToolConsumer = RunSelection(["clio/Command/AlphaService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement indirectConsumer = RunSelection(["clio/Command/SharedHelper.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement registeredImplementation = RunSelection(["clio/Common/BetaService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement factoryImplementation = RunSelection(["clio/Common/DeltaBackend.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement cliVerb = RunSelection(["clio/Command/GammaCommand.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement asset = RunSelection(["clio/Command/McpServer/Data/BetaRules.json"], includeNoEnvironment: false, repo.Root);
		JsonElement toolFile = RunSelection(["clio/Command/McpServer/Tools/AlphaTool.cs"], includeNoEnvironment: false, repo.Root);

		// Assert
		directToolConsumer.GetProperty("mode").GetString().Should().Be("subset",
			because: "AlphaService is named by AlphaTool.cs, so its blast radius is AlphaTool's fixtures");
		directToolConsumer.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(alphaFixtures,
			because: "AlphaTool is selected by the fixture named after it, by the ones that use its tool-name literal and by the NoEnvironment contract fixture that names its class");
		indirectConsumer.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(alphaFixtures,
			because: "SharedHelper is named by OtherCommand.cs as well, but following that consumer further reaches no other tool, so the old rule's full run was pure over-approximation");
		registeredImplementation.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["BetaToolE2ETests"],
			because: "BetaTool names only IBetaService, so the AddSingleton<IBetaService, BetaService> pair is the only edge that links the implementation to its coverage");
		factoryImplementation.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["DeltaToolE2ETests"],
			because: "DeltaBackend declares no interface, so the factory line in the composition root is the only thing that ties it to DeltaTool; without reading that line a change to it looks unreachable and skips the build entirely");
		cliVerb.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["GammaCliE2ETests"],
			because: "a command is reached by its verb string, so the fixture that spells the verb out is its only textual coverage link");
		asset.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["BetaToolE2ETests"],
			because: "an asset loaded by file name belongs to the fixtures of the tools that load it");
		toolFile.GetProperty("mode").GetString().Should().Be("subset",
			because: "a tool file resolves directly to the fixtures that reference it");
		toolFile.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(alphaFixtures,
			because: "the class identifier, the tool-name literal and the naming convention all point at the same fixtures");
	}

	[Test]
	[Description("The graph follows the references a plain identifier scan cannot see: a fully qualified type name, an extension method reached only through the type it extends, and a declaration that only looks like one because it sits inside a raw string.")]
	public void Script_ShouldFollowReferencesThatAPlainIdentifierScanMisses() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement qualified = RunSelection(["clio/Common/EpsilonService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement extensionMethod = RunSelection(["clio/Common/ZetaExtensions.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement pastARawString = RunSelection(["clio/Common/ThetaDependency.cs"], includeNoEnvironment: false, repo.Root);

		// Assert
		qualified.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["EpsilonToolE2ETests"],
			because: "EpsilonTool writes Clio.Common.EpsilonService, and a dot in front of the type name must not hide the reference or the change looks unobservable");
		extensionMethod.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["ZetaToolE2ETests"],
			because: "the call site writes value.Normalize() and never names ZetaExtensions, so the extended type is the only route from the tool to the extension");
		pastARawString.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["ThetaToolE2ETests"],
			because: "the declaration inside ThetaService's raw string must not end ThetaService's body early, or the dependency it uses after the literal loses its consumer");
	}

	[Test]
	[Description("A registration spread over several lines still links the implementation, a namespace alias still resolves the type behind it, and an extension on a type the repository does not declare runs the whole suite because its callers cannot be enumerated.")]
	public void Script_ShouldHandleMultilineRegistrations_AliasedNamespaces_AndUnboundedExtensions() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement multilineFactory = RunSelection(["clio/Common/IotaBackend.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement aliasedNamespace = RunSelection(["clio/Common/KappaService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement unboundedExtension = RunSelection(["clio/Common/LambdaExtensions.cs"], includeNoEnvironment: false, repo.Root);

		// Assert
		multilineFactory.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["IotaToolE2ETests"],
			because: "the implementation sits on a continuation line, and a rule that reads only the line with the generic argument would call the change unobservable");
		aliasedNamespace.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["KappaToolE2ETests"],
			because: "Contracts.KappaService is a reference to Clio.Common.KappaService, and an alias must resolve to the namespace behind it");
		unboundedExtension.GetProperty("mode").GetString().Should().Be("full",
			because: "an extension on string is called as value.Shorten() from anywhere, including code outside this repository, so no consumer set bounds it and skipping the build would be a guess");
	}

	[Test]
	[Description("A data asset runs the whole suite when any file naming it has an unknown blast radius, even when another file naming it resolved to fixtures first.")]
	public void Script_ShouldSelectFullRun_WhenOneHolderOfADataAssetIsUnclassifiable() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement selection = RunSelection(["clio/Command/McpServer/Data/AlphaRules.json"], includeNoEnvironment: false, repo.Root);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("full",
			because: "EtaLoader also reads the asset and is resolved dynamically, so its blast radius is unknown; AlphaTool resolving first must not hide that");
		selection.GetProperty("decisions").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("asset holder"),
			because: "the decision log must name the holder that forced the full run");
	}

	[Test]
	[Description("In a synthetic repository, a tool file that no fixture references forces a full run instead of an empty subset, a registration file does not count as a consumer, an abstract base class in a fixture file is not emitted as a fixture, and a subset becomes mode none only when every fixture is positively NoEnvironment-only and that tier is not kept on TeamCity.")]
	public void Script_ShouldForceFullRun_WhenToolFileSelectsNoFixture_AndSkipAbstractBases() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement unreferencedTool = RunSelection(["clio/Command/McpServer/Tools/LonelyTool.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement registeredOnly = RunSelection(["clio/Command/RegisteredOnlyService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement fixtureFile = RunSelection(["clio.mcp.e2e/AlphaToolE2ETests.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement noEnvironmentOnly = RunSelection(["clio.mcp.e2e/AlphaContractE2ETests.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement noEnvironmentKept = RunSelection(["clio.mcp.e2e/AlphaContractE2ETests.cs"], includeNoEnvironment: true, repo.Root);
		JsonElement untieredOnly = RunSelection(["clio.mcp.e2e/AlphaLegacyE2ETests.cs"], includeNoEnvironment: false, repo.Root);

		// Assert
		unreferencedTool.GetProperty("mode").GetString().Should().Be("full",
			because: "a tool without fixtures must not shrink the run to nothing");
		unreferencedTool.GetProperty("decisions").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("selects no fixture"),
			because: "the decision log must say why the run became full");
		registeredOnly.GetProperty("mode").GetString().Should().Be("none",
			because: "a type only the composition root mentions, with no interface registration pointing at it, is consumed by nothing this suite executes");
		fixtureFile.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["AlphaToolE2ETests"],
			because: "the abstract AlphaFixtureBase declared in the same file is not a runnable fixture and must not enter the filter");
		noEnvironmentOnly.GetProperty("mode").GetString().Should().Be("none",
			because: "a subset made only of NoEnvironment fixtures has nothing left to run once that tier is excluded, and queuing a Creatio deploy for zero tests is waste");
		noEnvironmentOnly.GetProperty("filter").GetString().Should().BeEmpty(
			because: "mode none must not hand TeamCity any filter");
		noEnvironmentKept.GetProperty("mode").GetString().Should().Be("subset",
			because: "when the NoEnvironment tier stays on TeamCity the same fixture is a normal subset");
		untieredOnly.GetProperty("mode").GetString().Should().Be("subset",
			because: "a fixture with no McpE2E.* tier runs on TeamCity under the base filter and nowhere on GitHub, so the absence of a Sandbox marker must never be read as NoEnvironment coverage (review finding on PR #1571)");
	}

	[Test]
	[Description("The set of product files no fixture can observe matches the list pinned in the repository, so a file drifting into or out of that set is a reviewable diff rather than a silent change to what runs.")]
	public void UnreachableProductFiles_ShouldMatchThePinnedList() {
		// Arrange
		string[] pinned = File.ReadAllLines(UnreachablePinPath)
			.Where(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line))
			.ToArray();
		string[] computed = Inventory.Value.GetProperty("unreachableProductFiles").EnumerateArray().Select(p => p.GetString()!).ToArray();

		// Act
		string[] newlyUnreachable = computed.Except(pinned, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
		string[] noLongerUnreachable = pinned.Except(computed, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();

		// Assert
		pinned.Should().NotBeEmpty(because: "the pinned list is the record of what the detector is allowed to skip a build for");
		newlyUnreachable.Should().BeEmpty(
			because: "a pull request touching only these files queues no e2e build, so adding a file here is a claim that no fixture covers it and has to be reviewed, not discovered later; refresh the list with Select-McpE2eTestFilter.ps1 -Inventory once you agree");
		noLongerUnreachable.Should().BeEmpty(
			because: "a file that became reachable must leave the list, or the pin keeps asserting something that stopped being true");
	}

	[Test]
	[Description("Every MCP tool that declares a tool name has at least one fixture, or is listed as a known gap, so the detector never selects an empty subset for a tool and no tool quietly ships without e2e coverage.")]
	public void ToolsWithoutFixtures_ShouldMatchTheDeclaredGaps() {
		// Arrange
		HashSet<string> declaredGaps = ReadManifest().GetProperty("toolsWithoutFixtures").EnumerateArray()
			.Select(t => t.GetString()!).ToHashSet(StringComparer.Ordinal);
		string[] uncovered = Inventory.Value.GetProperty("uncoveredTools").EnumerateArray().Select(t => t.GetString()!).ToArray();

		// Act
		string[] undeclared = uncovered.Except(declaredGaps, StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray();
		string[] stale = declaredGaps.Except(uncovered, StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray();

		// Assert
		undeclared.Should().BeEmpty(
			because: "a tool no fixture references forces the whole suite to run for every change to it; add the fixture, or record the gap in toolsWithoutFixtures so it is visible");
		stale.Should().BeEmpty(
			because: "a declared gap that has fixtures now is dead configuration and hides the next real gap");
	}

	private static JsonElement RunSelection(string[] changedFiles, bool includeNoEnvironment, string? repositoryRoot = null) =>
		RunScript(powerShell => {
			powerShell.AddParameter("ChangedFiles", changedFiles);
			if (includeNoEnvironment) {
				powerShell.AddParameter("IncludeNoEnvironment");
			}
		}, repositoryRoot);

	private static JsonElement RunScript(Action<PowerShell> configure, string? repositoryRoot = null) {
		using PowerShell powerShell = PowerShell.Create();
		powerShell.AddScript(File.ReadAllText(ScriptPath)).AddParameter("RepositoryRoot", repositoryRoot ?? RepositoryRoot);
		configure(powerShell);
		string output = string.Join(Environment.NewLine, powerShell.Invoke().Select(value => value.ToString()));
		if (powerShell.HadErrors) {
			string errors = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(error => error.ToString()));
			throw new InvalidOperationException($"Select-McpE2eTestFilter.ps1 failed: {errors}");
		}
		return JsonDocument.Parse(output).RootElement.Clone();
	}

	private static JsonElement ReadManifest() => JsonDocument.Parse(File.ReadAllText(ManifestPath)).RootElement.Clone();

	private static HashSet<string> GetManifestFixtureNames(JsonElement manifest) =>
		manifest.GetProperty("fullRunOnlyFixtures").EnumerateArray().Select(f => f.GetString()!)
			.Concat(manifest.GetProperty("explicitMappings").EnumerateArray()
				.SelectMany(mapping => mapping.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()!)))
			.ToHashSet(StringComparer.Ordinal);

	private static HashSet<string> GetInventoryFixtureNames() =>
		Inventory.Value.GetProperty("fixtures").EnumerateObject()
			.SelectMany(file => file.Value.EnumerateArray().Select(name => name.GetString()!))
			.ToHashSet(StringComparer.Ordinal);

	private static IReadOnlyList<Type> GetFixtureTypes() =>
		typeof(ExperimentalToolE2ETests).Assembly.GetTypes()
			.Where(type => type.IsClass && !type.IsAbstract && !type.IsNested)
			.Where(type => type.Namespace == "Clio.Mcp.E2E")
			.Where(type => type.GetCustomAttributes<TestFixtureAttribute>(inherit: true).Any()
				|| type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
					.Any(method => method.GetCustomAttributes<TestAttribute>().Any() || method.GetCustomAttributes<TestCaseAttribute>().Any()))
			.OrderBy(type => type.FullName, StringComparer.Ordinal)
			.ToArray();

	/// <summary>
	/// Class-level categories only: Manual, LocalOnly and Unit are always declared on the class in this
	/// suite (see <see cref="McpFixturePolicyTests"/>), and a method-level exemption would let one tagged
	/// method exempt a whole fixture from the reachability requirement.
	/// </summary>
	private static bool HasClassCategory(Type fixture, string category) =>
		fixture.GetCustomAttributes<CategoryAttribute>(inherit: true).Any(attribute => attribute.Name == category);

	private static bool IsDeclaredUnderSupport(string fixtureName) =>
		Directory.Exists(SupportDirectory) && Directory.EnumerateFiles(SupportDirectory, "*.cs", SearchOption.AllDirectories)
			.Any(path => Regex.IsMatch(File.ReadAllText(path), $@"\bclass\s+{Regex.Escape(fixtureName)}\b"));

	private static string[] ReadTriggerPaths() {
		Dictionary<object, object> workflow = ReadWorkflow(TriggerWorkflowPath);
		Dictionary<object, object> on = GetMap(workflow, "on");
		Dictionary<object, object> pullRequest = GetMap(on, "pull_request");
		return ((List<object>)pullRequest["paths"]).Select(path => path.ToString()!).ToArray();
	}

	private static Dictionary<object, object> ReadWorkflowJobs(string path) => GetMap(ReadWorkflow(path), "jobs");

	private static Dictionary<object, object> ReadWorkflow(string path) {
		IDeserializer deserializer = new DeserializerBuilder().Build();
		return deserializer.Deserialize<Dictionary<object, object>>(File.ReadAllText(path));
	}

	private static Dictionary<object, object> GetMap(Dictionary<object, object> parent, string key) =>
		(Dictionary<object, object>)parent[key];

	private static List<Dictionary<object, object>> GetSteps(Dictionary<object, object> job) =>
		((List<object>)job["steps"]).Cast<Dictionary<object, object>>().ToList();

	private static string GetSourceDirectory([CallerFilePath] string sourcePath = "") =>
		Path.GetDirectoryName(sourcePath)!;

	private static string FindRepositoryRoot(params string[] startPaths) {
		foreach (string startPath in startPaths) {
			DirectoryInfo? candidate = new(Path.GetFullPath(startPath));
			while (candidate is not null) {
				bool hasScript = File.Exists(Path.Combine(candidate.FullName, ".github", "scripts", "Select-McpE2eTestFilter.ps1"));
				bool hasTestProject = File.Exists(Path.Combine(candidate.FullName, "clio.tests", "clio.tests.csproj"));
				if (hasScript && hasTestProject) {
					return candidate.FullName;
				}
				candidate = candidate.Parent;
			}
		}
		throw new DirectoryNotFoundException("Could not locate the Clio repository root from the current or test-output directory.");
	}

	/// <summary>
	/// A minimal repository layout the selection script can run against, with the real manifest copied in,
	/// so each rule is tested on known inputs rather than on whatever the live tree happens to contain.
	/// </summary>
	private sealed class SyntheticRepository : IDisposable {

		public string Root { get; }

		private SyntheticRepository(string root) => Root = root;

		public static SyntheticRepository Create() {
			string root = Path.Combine(Path.GetTempPath(), "clio-selection-" + Guid.NewGuid().ToString("N"));
			void Write(string relative, string content) {
				string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.WriteAllText(path, content);
			}
			Write("clio.mcp.e2e/TestSelection/mcp-e2e-selection.json", File.ReadAllText(ManifestPath));
			Write("clio/Command/McpServer/Tools/BaseTool.cs", "public abstract class BaseTool { }");
			Write("clio/Command/McpServer/Tools/AlphaTool.cs",
				"public sealed class AlphaTool : BaseTool {\n\tinternal const string ToolName = \"alpha-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(AlphaService service, SharedHelper helper) { Load(\"AlphaRules.json\"); }\n}");
			Write("clio/Command/McpServer/Tools/LonelyTool.cs",
				"public sealed class LonelyTool : BaseTool {\n\t[McpServerTool(Name = \"lonely-run\")]\n\tpublic void Run() { }\n}");
			Write("clio/Command/AlphaService.cs", "public sealed class AlphaService { }");
			Write("clio/Command/SharedHelper.cs", "public sealed class SharedHelper { }");
			Write("clio/Command/OtherCommand.cs", "public sealed class OtherCommand { private readonly SharedHelper _helper; }");
			Write("clio/Command/OrphanService.cs", "public sealed class OrphanService { }");
			Write("clio/Command/RegisteredOnlyService.cs", "public sealed class RegisteredOnlyService { }");
			// BetaTool depends on the interface and never spells the implementation out; only the
			// registration pair links them, which is the edge plain name matching cannot see.
			Write("clio/Command/McpServer/Tools/BetaTool.cs",
				"public sealed class BetaTool : BaseTool {\n\tinternal const string ToolName = \"beta-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(IBetaService service) { Load(\"BetaRules.json\"); }\n}");
			Write("clio/Common/IBetaService.cs", "public interface IBetaService { }");
			Write("clio/Common/BetaService.cs", "public sealed class BetaService : IBetaService { }");
			// Registered through a factory. DeltaBackend declares no base list, so the
			// implementation-to-interface edge cannot reach it and the factory line is the only
			// thing tying it to the tool - the form 31 registrations in the real BindingsModule use.
			Write("clio/Command/McpServer/Tools/DeltaTool.cs",
				"public sealed class DeltaTool : BaseTool {\n\tinternal const string ToolName = \"delta-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(IDeltaService service) { }\n}");
			Write("clio/Common/IDeltaService.cs", "public interface IDeltaService { }");
			Write("clio/Common/DeltaAdapter.cs", "public sealed class DeltaAdapter : IDeltaService { }");
			Write("clio/Common/DeltaBackend.cs", "public sealed class DeltaBackend { }");
			// A CLI command reached by its verb string, the way the harness runs the executable.
			Write("clio/Command/GammaCommand.cs",
				"[Verb(\"gamma-run\")]\npublic sealed class GammaOptions { }\npublic sealed class GammaCommand { }");
			// An asset the tool loads by file name rather than by type reference.
			Write("clio/Command/McpServer/Data/AlphaRules.json", "{ }");
			Write("clio/Command/McpServer/Data/BetaRules.json", "{ }");
			// EpsilonTool names its dependency fully qualified, so the dot in front of the type hides
			// it from the plain identifier scan; 1247 references in the real tree are written this way.
			Write("clio/Command/McpServer/Tools/EpsilonTool.cs",
				"namespace Clio.Command.McpServer.Tools;\npublic sealed class EpsilonTool : BaseTool {\n" +
				"\tinternal const string ToolName = \"epsilon-run\";\n\t[McpServerTool(Name = ToolName)]\n" +
				"\tpublic void Run() { var x = new Clio.Common.EpsilonService(); }\n}");
			Write("clio/Common/EpsilonService.cs", "namespace Clio.Common;\npublic sealed class EpsilonService { }");
			// ZetaExtensions is named by nobody: the call site writes value.Normalize() and reaches it
			// only through the type it extends.
			Write("clio/Command/McpServer/Tools/ZetaTool.cs",
				"public sealed class ZetaTool : BaseTool {\n\tinternal const string ToolName = \"zeta-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(ZetaValue value) { value.Normalize(); }\n}");
			Write("clio/Common/ZetaValue.cs", "public sealed class ZetaValue { }");
			Write("clio/Common/ZetaExtensions.cs",
				"public static class ZetaExtensions {\n\tpublic static int Normalize(this ZetaValue value) => 1;\n}");
			// A second holder of AlphaRules.json whose own blast radius is unknown.
			Write("clio/Command/McpServer/Tools/EtaLoader.cs",
				"[ResolvedDynamically]\npublic sealed class EtaLoader {\n\tpublic void Load() { Read(\"AlphaRules.json\"); }\n}");
			// A raw string whose contents look like a top-level declaration.
			Write("clio/Common/ThetaService.cs",
				"public sealed class ThetaService {\n\tconst string Sample = \"\"\"\npublic class FakeDeclaration { }\n\"\"\";\n" +
				"\tpublic void Use(ThetaDependency dependency) { }\n}");
			Write("clio/Command/McpServer/Tools/ThetaTool.cs",
				"public sealed class ThetaTool : BaseTool {\n\tinternal const string ToolName = \"theta-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(ThetaService service) { }\n}");
			Write("clio/Common/ThetaDependency.cs", "public sealed class ThetaDependency { }");
			// The implementation sits on a continuation line of the registration statement.
			Write("clio/Command/McpServer/Tools/IotaTool.cs",
				"public sealed class IotaTool : BaseTool {\n\tinternal const string ToolName = \"iota-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(IIotaService service) { }\n}");
			Write("clio/Common/IIotaService.cs", "public interface IIotaService { }");
			Write("clio/Common/IotaBackend.cs", "public sealed class IotaBackend { }");
			// Reached only through a namespace alias.
			Write("clio/Command/McpServer/Tools/KappaTool.cs",
				"namespace Clio.Command.McpServer.Tools;\nusing Contracts = Clio.Common;\n" +
				"public sealed class KappaTool : BaseTool {\n\tinternal const string ToolName = \"kappa-run\";\n" +
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run() { var x = new Contracts.KappaService(); }\n}");
			Write("clio/Common/KappaService.cs", "namespace Clio.Common;\npublic sealed class KappaService { }");
			// An extension on a type this repository does not declare: its callers cannot be listed.
			Write("clio/Common/LambdaExtensions.cs",
				"public static class LambdaExtensions {\n\tpublic static int Shorten(this string value) => 1;\n}");
			Write("clio/BindingsModule.cs",
				"public static class BindingsModule { static void Register() {\n" +
				"\t_ = typeof(RegisteredOnlyService);\n\tservices.AddSingleton<IBetaService, BetaService>();\n" +
				"\tservices.AddSingleton<IDeltaService>(sp => new DeltaAdapter(new DeltaBackend()));\n" +
				"\tservices.AddSingleton<IIotaService>(\n\t\tsp => new IotaAdapter(new IotaBackend()));\n} }");
			Write("clio.mcp.e2e/AlphaToolE2ETests.cs",
				"public abstract class AlphaFixtureBase { }\n[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class AlphaToolE2ETests : AlphaFixtureBase {\n\t[Test] public void Works() => Call(AlphaTool.ToolName);\n}");
			Write("clio.mcp.e2e/AlphaLiteralE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class AlphaLiteralE2ETests {\n\t[Test] public void Works() => Call(\"alpha-run\");\n}");
			Write("clio.mcp.e2e/AlphaContractE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.NoEnvironment\")]\npublic sealed class AlphaContractE2ETests {\n\t[Test] public void Advertises() => Call(AlphaTool.ToolName);\n}");
			Write("clio.mcp.e2e/UnrelatedE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class UnrelatedE2ETests {\n\t[Test] public void Works() { }\n}");
			Write("clio.mcp.e2e/BetaToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class BetaToolE2ETests {\n\t[Test] public void Works() => Call(BetaTool.ToolName);\n}");
			Write("clio.mcp.e2e/IotaToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class IotaToolE2ETests {\n\t[Test] public void Works() => Call(IotaTool.ToolName);\n}");
			Write("clio.mcp.e2e/KappaToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class KappaToolE2ETests {\n\t[Test] public void Works() => Call(KappaTool.ToolName);\n}");
			Write("clio.mcp.e2e/EpsilonToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class EpsilonToolE2ETests {\n\t[Test] public void Works() => Call(EpsilonTool.ToolName);\n}");
			Write("clio.mcp.e2e/ZetaToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class ZetaToolE2ETests {\n\t[Test] public void Works() => Call(ZetaTool.ToolName);\n}");
			Write("clio.mcp.e2e/ThetaToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class ThetaToolE2ETests {\n\t[Test] public void Works() => Call(ThetaTool.ToolName);\n}");
			Write("clio.mcp.e2e/DeltaToolE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class DeltaToolE2ETests {\n\t[Test] public void Works() => Call(DeltaTool.ToolName);\n}");
			Write("clio.mcp.e2e/GammaCliE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class GammaCliE2ETests {\n\t[Test] public void Works() => RunCli(\"gamma-run\");\n}");
			// Carries no McpE2E.* tier at all, like DownloadSysSettingFileE2ETests in the live tree.
			Write("clio.mcp.e2e/AlphaLegacyE2ETests.cs",
				"[TestFixture]\n[Category(\"E2E\")]\npublic sealed class AlphaLegacyE2ETests {\n\t[Test] public void Works() => Call(\"alpha-run\");\n}");
			return new SyntheticRepository(root);
		}

		public void Dispose() {
			try {
				Directory.Delete(Root, recursive: true);
			}
			catch (IOException) {
				// A leftover temp directory is not worth failing the test run for.
			}
		}

	}

}
