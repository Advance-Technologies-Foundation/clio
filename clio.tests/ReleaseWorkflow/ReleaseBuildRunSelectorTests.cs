using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests.ReleaseWorkflow;

/// <summary>
/// Guards the run selection of the release gate (<c>.github/scripts/Select-ReleaseBuildRun.ps1</c>, called by
/// <c>.github/workflows/reliase-to-nuget.yml</c>).
/// </summary>
/// <remarks>
/// The release publishes when the chosen <c>Build</c> run is green. A master push run executes every unit test
/// shard, while a <c>pull_request</c> run of the same SHA runs only the jobs for the areas the PR changed, so if
/// the selector ever accepts one again the release can be gated on a run that skipped most tests, and nothing
/// else fails. The script runs in-process on hand-made runs.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
// Creating a PowerShell runspace enumerates drives (DriveInfo.GetDrives). On macOS that call is not safe to
// run concurrently, and a runspace created here in parallel with another PowerShell-hosting fixture
// (McpE2eSelectionCoverageTests) crashed the test host with an AccessViolationException in
// Interop.Sys.GetAllMountPoints. Running this small fixture on its own avoids that race.
[NonParallelizable]
internal sealed class ReleaseBuildRunSelectorTests {

	private const string BuildWorkflow = ".github/workflows/build.yml";
	private const string GateStepName = "Verify Build workflow is green for the tagged commit";

	private static readonly string RepositoryRoot =
		FindRepositoryRoot(GetSourceDirectory(), Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

	private static readonly string ScriptPath =
		Path.Combine(RepositoryRoot, ".github", "scripts", "Select-ReleaseBuildRun.ps1");

	private static readonly string ReleaseWorkflowPath =
		Path.Combine(RepositoryRoot, ".github", "workflows", "reliase-to-nuget.yml");

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	private static readonly Regex SelectorScriptAssignment = new(
		@"^\s*\$selectorScript\s*=.*[""']\.github\\scripts\\Select-ReleaseBuildRun\.ps1[""']\s*\)?\s*$",
		RegexOptions.Multiline | RegexOptions.IgnoreCase, RegexTimeout);

	private static readonly Regex SelectorCall = new(
		@"^\s*\$latest\s*=\s*&\s*\$selectorScript\s+-WorkflowRuns\s+@\(\$response\.workflow_runs\)\s*$",
		RegexOptions.Multiline | RegexOptions.IgnoreCase, RegexTimeout);

	private static readonly Regex RunsReference = new(@"\$response\.workflow_runs", RegexOptions.IgnoreCase, RegexTimeout);

	private static readonly Regex LatestAssignment = new(@"\$latest\s*[+\-*/]?=(?!=)", RegexOptions.IgnoreCase, RegexTimeout);

	private static readonly Regex InlineFilter = new(
		@"Where-Object|\.Where\s*\(|\|\s*(\?|where)\s*\{|ForEach-Object|\|\s*%\s*\{|\.ForEach\s*\(|\bforeach\s*\(|\.(event|head_branch|path)\b",
		RegexOptions.IgnoreCase, RegexTimeout);

	[Test]
	[Description("A master push run of build.yml is selected.")]
	public void Select_ShouldReturnRun_WhenItIsAMasterPushRunOfBuildWorkflow() {
		// Arrange
		PSObject run = CreateRun(1, BuildWorkflow, "push", "master", "2026-09-20T10:00:00Z");

		// Act
		PSObject? selected = Select(run);

		// Assert
		selected.Should().NotBeNull(because: "a master push run of build.yml is the run the release trusts");
		selected!.Properties["id"].Value.Should().Be(1L, because: "the only matching run must be the one returned");
	}

	[Test]
	[Description("A build.yml run started by any event other than push is never selected, so a pull_request run that ran only the jobs for the changed areas cannot satisfy the release gate.")]
	[TestCase("pull_request")]
	[TestCase("pull_request_target")]
	[TestCase("workflow_dispatch")]
	[TestCase("merge_group")]
	public void Select_ShouldReturnNothing_WhenRunIsNotAPush(string eventName) {
		// Arrange
		PSObject run = CreateRun(1, BuildWorkflow, eventName, "master", "2026-09-20T10:00:00Z");

		// Act
		PSObject? selected = Select(run);

		// Assert
		selected.Should().BeNull(
			because: $"a '{eventName}' run is not the master push run; accepting it would gate the release on a run that may have skipped most tests");
	}

	[Test]
	[Description("A build.yml push run from a branch other than master is never selected.")]
	[TestCase("feature/ENG-1-x")]
	[TestCase("Alexandr-Kravchuk/issue-1573")]
	[TestCase("main")]
	[TestCase("")]
	public void Select_ShouldReturnNothing_WhenRunIsNotOnMaster(string branch) {
		// Arrange
		PSObject run = CreateRun(1, BuildWorkflow, "push", branch, "2026-09-20T10:00:00Z");

		// Act
		PSObject? selected = Select(run);

		// Assert
		selected.Should().BeNull(because: $"a push to '{branch}' did not test what master holds, so it must not gate a release");
	}

	[Test]
	[Description("A master push run of a workflow other than build.yml is never selected.")]
	public void Select_ShouldReturnNothing_WhenRunIsFromAnotherWorkflow() {
		// Arrange
		PSObject run = CreateRun(1, ".github/workflows/knowledge-base-check.yml", "push", "master", "2026-09-20T10:00:00Z");

		// Act
		PSObject? selected = Select(run);

		// Assert
		selected.Should().BeNull(because: "only build.yml runs the test suite the release relies on");
	}

	[Test]
	[Description("Among several runs of the SHA, the newest master push run of build.yml is selected and newer non-matching runs are ignored.")]
	public void Select_ShouldReturnNewestMatchingRun_WhenSeveralRunsExist() {
		// Arrange
		PSObject[] runs = [
			CreateRun(1, BuildWorkflow, "push", "master", "2026-09-20T10:00:00Z"),
			CreateRun(2, BuildWorkflow, "push", "master", "2026-09-20T11:00:00Z"),
			CreateRun(3, BuildWorkflow, "pull_request", "master", "2026-09-20T12:00:00Z"),
			CreateRun(4, BuildWorkflow, "push", "feature/x", "2026-09-20T13:00:00Z")
		];

		// Act
		PSObject? selected = Select(runs);

		// Assert
		selected.Should().NotBeNull(because: "two master push runs exist");
		selected!.Properties["id"].Value.Should().Be(2L,
			because: "a re-run supersedes an earlier attempt, and newer pull_request or branch runs must not win");
	}

	[Test]
	[Description("No runs at all selects nothing, so the gate reports that no master push run exists.")]
	public void Select_ShouldReturnNothing_WhenThereAreNoRuns() {
		// Arrange
		PSObject[] runs = [];

		// Act
		PSObject? selected = Select(runs);

		// Assert
		selected.Should().BeNull(because: "the gate must refuse to publish when build.yml has not run for the commit");
	}

	[Test]
	[Description("The release gate step delegates run selection to Select-ReleaseBuildRun.ps1 instead of filtering inline: the runs reach only the script call, $latest comes only from it, and no filtering construct touches the runs or the selected run, so the selector tests cover what the release actually runs.")]
	public void ReleaseWorkflow_ShouldSelectBuildRunThroughTheTestedScript() {
		// Arrange
		string run = GetGateStepScript();

		// Act
		bool resolvesScript = SelectorScriptAssignment.IsMatch(run);
		int selectorCalls = SelectorCall.Count(run);
		int runsReferences = RunsReference.Count(run);
		int latestAssignments = LatestAssignment.Count(run);
		string[] inlineFilters = InlineFilter.Matches(run).Select(match => match.Value).ToArray();

		// Assert
		resolvesScript.Should().BeTrue(because: "$selectorScript must point at the selector these tests cover");
		selectorCalls.Should().Be(1,
			because: "$latest must be assigned from one call of the tested selector with $response.workflow_runs as its only input");
		runsReferences.Should().Be(1,
			because: "the runs must reach only the selector call; any other use of $response.workflow_runs could pick a run the selector did not choose");
		latestAssignments.Should().Be(1,
			because: "reassigning $latest after the selector call would replace the tested selection");
		inlineFilters.Should().BeEmpty(
			because: "a Where-Object/.Where()/?/where/ForEach-Object/foreach filter, or a check of path, event or head_branch next to the selector call, would bypass the tested selection and could widen it silently");
	}

	private static PSObject? Select(params PSObject[] runs) {
		using PowerShell powerShell = PowerShell.Create();
		powerShell.AddScript(File.ReadAllText(ScriptPath)).AddParameter("WorkflowRuns", runs);
		PSObject[] output = powerShell.Invoke().Where(value => value is not null).ToArray();
		if (powerShell.HadErrors) {
			string errors = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(error => error.ToString()));
			throw new InvalidOperationException($"Select-ReleaseBuildRun.ps1 failed: {errors}");
		}
		if (output.Length > 1) {
			throw new InvalidOperationException($"Select-ReleaseBuildRun.ps1 returned {output.Length} runs; it must return at most one.");
		}
		return output.SingleOrDefault();
	}

	/// <summary>A run shaped like an item of the GitHub REST <c>workflow_runs</c> array.</summary>
	private static PSObject CreateRun(long id, string path, string eventName, string headBranch, string createdAt) {
		PSObject run = new();
		run.Properties.Add(new PSNoteProperty("id", id));
		run.Properties.Add(new PSNoteProperty("path", path));
		run.Properties.Add(new PSNoteProperty("event", eventName));
		run.Properties.Add(new PSNoteProperty("head_branch", headBranch));
		run.Properties.Add(new PSNoteProperty("created_at", createdAt));
		run.Properties.Add(new PSNoteProperty("status", "completed"));
		run.Properties.Add(new PSNoteProperty("conclusion", "success"));
		return run;
	}

	private static string GetGateStepScript() {
		Dictionary<object, object> workflow = new DeserializerBuilder().Build()
			.Deserialize<Dictionary<object, object>>(File.ReadAllText(ReleaseWorkflowPath));
		Dictionary<object, object> job = (Dictionary<object, object>)((Dictionary<object, object>)workflow["jobs"])["build"];
		Dictionary<object, object> step = ((List<object>)job["steps"]).Cast<Dictionary<object, object>>()
			.Single(candidate => candidate.TryGetValue("name", out object? name) && name.ToString() == GateStepName);
		return step["run"].ToString()!;
	}

	private static string GetSourceDirectory([CallerFilePath] string sourcePath = "") =>
		Path.GetDirectoryName(sourcePath)!;

	private static string FindRepositoryRoot(params string[] startPaths) {
		foreach (string startPath in startPaths) {
			DirectoryInfo? candidate = new(Path.GetFullPath(startPath));
			while (candidate is not null) {
				bool hasScript = File.Exists(Path.Combine(candidate.FullName, ".github", "scripts", "Select-ReleaseBuildRun.ps1"));
				bool hasTestProject = File.Exists(Path.Combine(candidate.FullName, "clio.tests", "clio.tests.csproj"));
				if (hasScript && hasTestProject) {
					return candidate.FullName;
				}
				candidate = candidate.Parent;
			}
		}
		throw new DirectoryNotFoundException("Could not locate the Clio repository root from the current or test-output directory.");
	}
}
