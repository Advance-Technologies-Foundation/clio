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
	[Description("Every path the TeamCity trigger workflow listens to is a relevant path in the manifest, so a change that queues the build is also classified rather than ignored.")]
	public void WorkflowTriggerPaths_ShouldAllBeRelevantPathsInTheManifest() {
		// Arrange
		HashSet<string> relevant = ReadManifest().GetProperty("relevantPaths").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal);
		string[] triggerPaths = ReadTriggerPaths();

		// Act
		string[] unclassified = triggerPaths.Where(path => !relevant.Contains(path)).ToArray();

		// Assert
		triggerPaths.Should().NotBeEmpty(because: "the workflow must declare the paths that queue the TeamCity build");
		unclassified.Should().BeEmpty(
			because: "a path that triggers the workflow but is not in relevantPaths is ignored by the selection, and a run with only such files falls back to the full suite instead of the intended subset");
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
		string[] changed = ["clio/Common/SomeService.cs", "clio/Command/McpServer/Tools/PageSyncTool.cs"];

		// Act
		JsonElement selection = RunSelection(changed, includeNoEnvironment: false);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("full",
			because: "clio/Common is used by every tool, so no subset is safe");
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
	[Description("A diff without any relevant file falls back to the full run rather than to an empty selection.")]
	public void Script_ShouldSelectFullRun_WhenNoRelevantFileChanged() {
		// Arrange
		string[] changed = ["README.md", "clio/docs/commands/ping.md", "clio.mcp.e2e/AGENTS.md"];

		// Act
		JsonElement selection = RunSelection(changed, includeNoEnvironment: false);

		// Assert
		selection.GetProperty("mode").GetString().Should().Be("full",
			because: "an unclassifiable diff must never shrink the run; the safe default is everything");
		selection.GetProperty("decisions").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("no relevant file changed"),
			because: "documentation under clio.mcp.e2e is excluded from relevantPaths and must be ignored, not classified");
	}

	[Test]
	[Description("In a synthetic repository, a product file whose only consumers are tool files selects the fixtures of those tools; a product file also consumed outside the tools, or consumed by nothing, forces a full run.")]
	public void Script_ShouldApplyClosedConsumerRule_ForProductFilesOutsideTools() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement onlyToolConsumer = RunSelection(["clio/Command/AlphaService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement outsideConsumer = RunSelection(["clio/Command/SharedHelper.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement noConsumer = RunSelection(["clio/Command/OrphanService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement toolFile = RunSelection(["clio/Command/McpServer/Tools/AlphaTool.cs"], includeNoEnvironment: false, repo.Root);

		// Assert
		onlyToolConsumer.GetProperty("mode").GetString().Should().Be("subset",
			because: "AlphaService is named only by AlphaTool.cs, so its blast radius is AlphaTool's fixtures");
		onlyToolConsumer.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(
			["AlphaToolE2ETests", "AlphaLiteralE2ETests", "AlphaContractE2ETests"],
			because: "AlphaTool is selected by the fixture named after it, by the one that uses its tool-name literal and by the NoEnvironment contract fixture that names its class");
		outsideConsumer.GetProperty("mode").GetString().Should().Be("full",
			because: "SharedHelper is also named by clio/Command/OtherCommand.cs, so consumers outside the tools exist and the closure is unknown");
		noConsumer.GetProperty("mode").GetString().Should().Be("full",
			because: "a type nobody names under clio/ is reached through DI or reflection, which the textual rule cannot follow");
		toolFile.GetProperty("mode").GetString().Should().Be("subset",
			because: "a tool file resolves directly to the fixtures that reference it");
		toolFile.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(
			["AlphaToolE2ETests", "AlphaLiteralE2ETests", "AlphaContractE2ETests"],
			because: "the class identifier, the tool-name literal and the naming convention all point at the same fixtures");
	}

	[Test]
	[Description("In a synthetic repository, a tool file that no fixture references forces a full run instead of an empty subset, a registration file does not count as a consumer, an abstract base class in a fixture file is not emitted as a fixture, and a NoEnvironment-only subset becomes mode none unless that tier is kept on TeamCity.")]
	public void Script_ShouldForceFullRun_WhenToolFileSelectsNoFixture_AndSkipAbstractBases() {
		// Arrange
		using SyntheticRepository repo = SyntheticRepository.Create();

		// Act
		JsonElement unreferencedTool = RunSelection(["clio/Command/McpServer/Tools/LonelyTool.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement registeredOnly = RunSelection(["clio/Command/RegisteredOnlyService.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement fixtureFile = RunSelection(["clio.mcp.e2e/AlphaToolE2ETests.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement noEnvironmentOnly = RunSelection(["clio.mcp.e2e/AlphaContractE2ETests.cs"], includeNoEnvironment: false, repo.Root);
		JsonElement noEnvironmentKept = RunSelection(["clio.mcp.e2e/AlphaContractE2ETests.cs"], includeNoEnvironment: true, repo.Root);

		// Assert
		unreferencedTool.GetProperty("mode").GetString().Should().Be("full",
			because: "a tool without fixtures must not shrink the run to nothing");
		unreferencedTool.GetProperty("decisions").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("selects no fixture"),
			because: "the decision log must say why the run became full");
		registeredOnly.GetProperty("mode").GetString().Should().Be("full",
			because: "BindingsModule.cs is a registration file and is excluded from the consumer set, leaving no consumer");
		fixtureFile.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString()).Should().BeEquivalentTo(["AlphaToolE2ETests"],
			because: "the abstract AlphaFixtureBase declared in the same file is not a runnable fixture and must not enter the filter");
		noEnvironmentOnly.GetProperty("mode").GetString().Should().Be("none",
			because: "a subset made only of NoEnvironment fixtures has nothing left to run once that tier is excluded, and queuing a Creatio deploy for zero tests is waste");
		noEnvironmentOnly.GetProperty("filter").GetString().Should().BeEmpty(
			because: "mode none must not hand TeamCity any filter");
		noEnvironmentKept.GetProperty("mode").GetString().Should().Be("subset",
			because: "when the NoEnvironment tier stays on TeamCity the same fixture is a normal subset");
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
				"\t[McpServerTool(Name = ToolName)]\n\tpublic void Run(AlphaService service, SharedHelper helper) { }\n}");
			Write("clio/Command/McpServer/Tools/LonelyTool.cs",
				"public sealed class LonelyTool : BaseTool {\n\t[McpServerTool(Name = \"lonely-run\")]\n\tpublic void Run() { }\n}");
			Write("clio/Command/AlphaService.cs", "public sealed class AlphaService { }");
			Write("clio/Command/SharedHelper.cs", "public sealed class SharedHelper { }");
			Write("clio/Command/OtherCommand.cs", "public sealed class OtherCommand { private readonly SharedHelper _helper; }");
			Write("clio/Command/OrphanService.cs", "public sealed class OrphanService { }");
			Write("clio/Command/RegisteredOnlyService.cs", "public sealed class RegisteredOnlyService { }");
			Write("clio/BindingsModule.cs", "public static class BindingsModule { static void Register() { _ = typeof(RegisteredOnlyService); } }");
			Write("clio.mcp.e2e/AlphaToolE2ETests.cs",
				"public abstract class AlphaFixtureBase { }\n[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class AlphaToolE2ETests : AlphaFixtureBase {\n\t[Test] public void Works() => Call(AlphaTool.ToolName);\n}");
			Write("clio.mcp.e2e/AlphaLiteralE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class AlphaLiteralE2ETests {\n\t[Test] public void Works() => Call(\"alpha-run\");\n}");
			Write("clio.mcp.e2e/AlphaContractE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.NoEnvironment\")]\npublic sealed class AlphaContractE2ETests {\n\t[Test] public void Advertises() => Call(AlphaTool.ToolName);\n}");
			Write("clio.mcp.e2e/UnrelatedE2ETests.cs",
				"[TestFixture]\n[Category(\"McpE2E.Sandbox\")]\npublic sealed class UnrelatedE2ETests {\n\t[Test] public void Works() { }\n}");
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
