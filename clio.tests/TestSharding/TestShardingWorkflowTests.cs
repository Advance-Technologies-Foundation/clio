using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests.TestSharding;

[TestFixture]
[Category("Unit")]
internal sealed class TestShardingWorkflowTests {

	private static readonly string RepositoryRoot =
		FindRepositoryRoot(GetSourceDirectory(), Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
	private static readonly string WorkflowPath =
		Path.Combine(RepositoryRoot, ".github", "workflows", "build.yml");
	private static readonly string MatrixScriptPath =
		Path.Combine(RepositoryRoot, ".github", "scripts", "Get-TestShardMatrix.ps1");
	private static readonly string ManifestPath =
		Path.Combine(RepositoryRoot, "clio.tests", "TestSharding", "test-shards.json");

	[Test]
	[Description("The NET8 compatibility build has one standalone job with the same change conditions as the unit shards.")]
	public void BuildWorkflow_ShouldRunNet8CompatibilityOnce_WhenUnitGateIsRelevant() {
		// Arrange
		Dictionary<object, object> jobs = ReadJobs();
		Dictionary<object, object> compatibilityJob = GetMap(jobs, "net8-compatibility");
		Dictionary<object, object> unitShardJob = GetMap(jobs, "unit-test-shards");
		List<Dictionary<object, object>> compatibilitySteps = GetSteps(compatibilityJob);
		string compatibilityRun = string.Join("\n", compatibilitySteps
			.Where(step => step.ContainsKey("run"))
			.Select(step => step["run"].ToString()));

		// Act
		bool hasMatrix = compatibilityJob.ContainsKey("strategy");
		Dictionary<object, object> setupStep = compatibilitySteps.Single(step =>
			step.TryGetValue("uses", out object? value) && value.ToString() == "actions/setup-dotnet@v4");
		Dictionary<object, object> setupArguments = GetMap(setupStep, "with");

		// Assert
		compatibilityJob["name"].Should().Be(".NET 8 Product Compatibility",
			because: "the dedicated job should have one stable, descriptive check name");
		compatibilityJob["needs"].Should().Be("changes",
			because: "compatibility should start as soon as change detection completes");
		compatibilityJob["if"].Should().Be(unitShardJob["if"],
			because: "compatibility and unit tests should use the same relevance conditions");
		hasMatrix.Should().BeFalse(because: "NET8 compatibility must run exactly once rather than once per shard");
		setupArguments["dotnet-version"].Should().Be("8.0.x",
			because: "the compatibility lane must install the supported NET8 SDK");
		compatibilityRun.Should().Contain("dotnet build .\\clio\\clio.csproj",
			because: "the dedicated lane must build the product project");
		compatibilityRun.Should().Contain("--framework net8.0",
			because: "the dedicated lane must validate the NET8 target");
		compatibilityRun.Should().Contain("-p:RunAnalyzers=false",
			because: "compatibility should retain the existing analyzer behavior");
	}

	[Test]
	[Description("The required Unit Tests check aggregates all unit shards and the standalone NET8 compatibility job.")]
	public void BuildWorkflow_ShouldFailUnitAggregate_WhenShardOrCompatibilityFails() {
		// Arrange
		Dictionary<object, object> jobs = ReadJobs();
		Dictionary<object, object> aggregateJob = GetMap(jobs, "unit-tests");
		List<object> dependencies = (List<object>)aggregateJob["needs"];
		Dictionary<object, object> guardStep = GetSteps(aggregateJob).Single();

		// Act
		string aggregateCondition = aggregateJob["if"].ToString()!;
		string failureCondition = guardStep["if"].ToString()!;
		string normalizedFailureCondition = string.Concat(failureCondition.Where(character => !char.IsWhiteSpace(character)));

		// Assert
		aggregateJob["name"].Should().Be("Unit Tests",
			because: "repository rules depend on this stable required-check name");
		aggregateCondition.Should().Be("always()",
			because: "the aggregate must still run and report failure when one of its dependencies fails or is skipped");
		dependencies.Select(value => value.ToString()).Should().BeEquivalentTo(
			new[] { "changes", "unit-test-shards", "net8-compatibility" },
			because: "the aggregate must wait for planning, every shard, and product compatibility");
		normalizedFailureCondition.Should().Be(
			"needs.changes.result!='success'||((needs.changes.outputs.clio-src=='true'||" +
			"needs.changes.outputs.tests=='true'||github.ref=='refs/heads/master')&&" +
			"(needs.unit-test-shards.result!='success'||needs.net8-compatibility.result!='success'))",
			because: "the stable gate must fail for change detection or either relevant prerequisite without failing irrelevant changes");
	}

	[Test]
	[Description("Unit shard matrices preserve their sharded and unsharded contracts without assigning NET8 compatibility.")]
	public void MatrixScript_ShouldPreserveUnitModes_WithoutAssigningNet8Compatibility() {
		// Arrange
		JsonElement shardedMatrix = RunUnitMatrix(disableSharding: false);
		JsonElement unshardedMatrix = RunUnitMatrix(disableSharding: true);

		// Act
		JsonElement[] shardedEntries = shardedMatrix.GetProperty("include").EnumerateArray().ToArray();
		JsonElement[] unshardedEntries = unshardedMatrix.GetProperty("include").EnumerateArray().ToArray();

		// Assert
		shardedEntries.Select(entry => entry.GetProperty("name").GetString()).Should().BeEquivalentTo(
			new[] { "unit-1", "unit-2", "unit-3", "unit-4" },
			because: "normal unit execution must retain all four named shards");
		shardedEntries.Should().OnlyContain(entry => !entry.GetProperty("shardingDisabled").GetBoolean(),
			because: "normal matrix entries must continue to apply fixture filters");
		shardedEntries.Count(entry => entry.GetProperty("runConflictResolverTests").GetBoolean()).Should().Be(1,
			because: "ConflictResolver compatibility must run exactly once in sharded mode");
		shardedEntries.Single(entry => entry.GetProperty("runConflictResolverTests").GetBoolean())
			.GetProperty("name").GetString().Should().Be("unit-2",
				because: "the fixed ConflictResolver cost is assigned to unit-2 in the committed balance");
		shardedEntries.Should().OnlyContain(entry => !HasProperty(entry, "runNet8Compatibility"),
			because: "NET8 compatibility belongs exclusively to its standalone workflow job");
		unshardedEntries.Should().ContainSingle(
			because: "disabled sharding must collapse unit execution to exactly one worker");
		unshardedEntries[0].GetProperty("name").GetString().Should().Be("unit-unsharded",
			because: "the fallback worker has a stable diagnostic name");
		unshardedEntries[0].GetProperty("shardingDisabled").GetBoolean().Should().BeTrue(
			because: "the fallback worker must bypass fixture filtering");
		unshardedEntries[0].GetProperty("runConflictResolverTests").GetBoolean().Should().BeTrue(
			because: "ConflictResolver compatibility must still run exactly once when sharding is disabled");
		unshardedEntries[0].TryGetProperty("runNet8Compatibility", out _).Should().BeFalse(
			because: "disabled sharding must not move NET8 compatibility back into the unit worker");
	}

	[Test]
	[Description("Repository workflow tests find the checkout even when the test output is outside the repository.")]
	public void FindRepositoryRoot_ShouldUseValidCandidate_WhenOutputDirectoryIsExternal() {
		// Arrange
		string externalDirectory = Path.GetTempPath();

		// Act
		string repositoryRoot = FindRepositoryRoot(externalDirectory, GetSourceDirectory());

		// Assert
		repositoryRoot.Should().Be(RepositoryRoot,
			because: "redirected build output must not make repository contract tests look under the output root");
	}

	private static Dictionary<object, object> ReadJobs() {
		string workflow = File.ReadAllText(WorkflowPath);
		Dictionary<object, object> root = new DeserializerBuilder()
			.Build()
			.Deserialize<Dictionary<object, object>>(workflow);
		return GetMap(root, "jobs");
	}

	private static Dictionary<object, object> GetMap(Dictionary<object, object> parent, string key) =>
		(Dictionary<object, object>)parent[key];

	private static List<Dictionary<object, object>> GetSteps(Dictionary<object, object> job) =>
		((List<object>)job["steps"]).Cast<Dictionary<object, object>>().ToList();

	private static bool HasProperty(JsonElement element, string propertyName) =>
		element.TryGetProperty(propertyName, out _);

	private static JsonElement RunUnitMatrix(bool disableSharding) {
		using PowerShell powerShell = PowerShell.Create();
		powerShell.AddScript(File.ReadAllText(MatrixScriptPath))
			.AddParameter("Suite", "unit")
			.AddParameter("ManifestPath", ManifestPath);
		if (disableSharding) {
			powerShell.AddParameter("DisableSharding");
		}
		string output = string.Join(Environment.NewLine, powerShell.Invoke().Select(value => value.ToString()));
		if (powerShell.HadErrors) {
			string errors = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(error => error.ToString()));
			throw new InvalidOperationException($"The unit shard matrix script failed: {errors}");
		}
		return JsonDocument.Parse(output).RootElement.Clone();
	}

	private static string GetSourceDirectory([CallerFilePath] string sourcePath = "") =>
		Path.GetDirectoryName(sourcePath)!;

	private static string FindRepositoryRoot(params string[] startPaths) {
		foreach (string startPath in startPaths) {
			DirectoryInfo? candidate = new(Path.GetFullPath(startPath));
			while (candidate is not null) {
				bool hasWorkflow = File.Exists(Path.Combine(candidate.FullName, ".github", "workflows", "build.yml"));
				bool hasTestProject = File.Exists(Path.Combine(candidate.FullName, "clio.tests", "clio.tests.csproj"));
				if (hasWorkflow && hasTestProject) {
					return candidate.FullName;
				}
				candidate = candidate.Parent;
			}
		}
		throw new DirectoryNotFoundException("Could not locate the Clio repository root from the current or test-output directory.");
	}
}
