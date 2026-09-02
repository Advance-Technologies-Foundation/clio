using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
		string failureCondition = guardStep["if"].ToString()!;

		// Assert
		aggregateJob["name"].Should().Be("Unit Tests",
			because: "repository rules depend on this stable required-check name");
		dependencies.Select(value => value.ToString()).Should().BeEquivalentTo(
			new[] { "changes", "unit-test-shards", "net8-compatibility" },
			because: "the aggregate must wait for planning, every shard, and product compatibility");
		failureCondition.Should().Contain("needs.unit-test-shards.result != 'success'",
			because: "a failed unit shard must fail the aggregate check");
		failureCondition.Should().Contain("needs.net8-compatibility.result != 'success'",
			because: "failed NET8 compatibility must fail the aggregate check");
	}

	[Test]
	[Description("Unit shard matrices no longer carry the NET8 compatibility switch in sharded or unsharded mode.")]
	public void MatrixScript_ShouldNotAssignNet8Compatibility_WhenBuildingUnitMatrix() {
		// Arrange
		string matrixScript = File.ReadAllText(MatrixScriptPath);

		// Act
		bool containsCompatibilityFlag = matrixScript.Contains("runNet8Compatibility", StringComparison.Ordinal);

		// Assert
		containsCompatibilityFlag.Should().BeFalse(
			because: "NET8 compatibility now belongs exclusively to its standalone workflow job");
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
