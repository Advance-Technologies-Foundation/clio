using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// The whole Script task compile chain over the real MCP server (ENG-92711): a saved script task, a
/// <c>compile-creatio process-name</c> compile that fails with CS0104 in the process's own code, an aliased using
/// added through <c>modify-business-process</c>, a second compile that succeeds, and a run that returns what the
/// code computes.
/// </summary>
/// <remarks>
/// <para>Developer-local on purpose. Each compile rebuilds the <c>Custom</c> package and reloads the runtime for
/// every user of the stand (about three minutes each on a local 10.1 stand), which the automatic lanes must never
/// do to a shared instance - the same reason the other compiling fixture,
/// <see cref="UserTaskUnlimitedTextToolE2ETests"/>, is in this sub-tier. Run it by hand against an owned stand
/// with CrtProcessBuilder 1.6.6.33 or later, with <c>McpE2E__Sandbox__EnvironmentName</c> and
/// <c>McpE2E__AllowDestructiveMcpTests=true</c>. On a .NET host it also restarts the application between the
/// successful compile and the run, because there the new code does not run before a restart.</para>
/// <para>A process that reached the successful compile is left on the stand under a unique
/// <c>UsrClioBpCompileLifecycleE2e*</c> name, like the other process-designer fixtures leave theirs: deleting a
/// process whose code was compiled into the shared assembly would owe yet another compile. Any other run deletes
/// its process - it stopped before the alias landed, or the second compile failed: a script task that does not
/// compile, left in <c>Custom</c>, would fail every later process-name compile of that package, this test's next
/// run included, and its failed compile never reached the assembly, so the delete owes nothing. A run cut off
/// while a compile may still be running keeps the process and says so, since deleting under a running compile is
/// not safe.</para>
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Category(McpE2ECategories.ProcessDesigner)]
[Explicit("Compiles the Custom package twice and reloads the runtime for every user; run it by hand against an owned stand.")]
// [AllureNUnit] is intentionally omitted, for the reason recorded on EntitySchemaToolE2ETests: its lifecycle hooks
// deadlock a fixture with many sequential async operations, and this one polls compile-status for minutes.
[AllureFeature(CompileCreatioTool.CompileCreatioToolName)]
[NonParallelizable]
public sealed class ScriptTaskCompileLifecycleE2ETests {

	private const string ExitCodeZero = "\\u0022exit-code\\u0022:0";

	/// <summary>The first CrtProcessBuilder whose CompileProcess compiles every process that needs it.</summary>
	private const string MinimumCompilePackageVersion = "1.6.6.33";

	/// <summary>The first CrtProcessBuilder whose activation warning says a compile made before it still counts.</summary>
	private const string MinimumActivationCoversCompilePackageVersion = "1.6.6.57";

	[Test]
	[Description("A script task that references SysSettings under a Terrasoft.Configuration import fails the process-name compile with CS0104 flagged as the process's own error; an aliased using added through modify-business-process makes the second compile succeed, and the run returns the values the code computes (after a restart on a .NET host, which the new code needs there) - the path an agent takes to repair a colleague's process, end to end.")]
	[AllureTag(CompileCreatioTool.CompileCreatioToolName)]
	[AllureName("A script task compiles through process-name after an alias resolves CS0104, and its run returns the computed values")]
	public async Task ScriptTask_Should_CompileAfterAnAliasResolvesCs0104_AndRunTheNewCode() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"This fixture compiles the Custom package twice, reloading the runtime for every user of the stand. "
			+ "Run it by hand against an owned stand.");
		if (!TestConfiguration.Load().AllowDestructiveMcpTests) {
			Assert.Ignore("Opt in with McpE2E__AllowDestructiveMcpTests for this local compile test.");
		}

		// Arrange
		await using ProcessDesignerArrangeContext context = await ProcessDesignerE2EArrange.StartAsync(
			"ScriptTask compile", MinimumCompilePackageVersion, sessionTimeout: TimeSpan.FromMinutes(25));
		string processName = $"UsrClioBpCompileLifecycleE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateBusinessProcessTool.CreateBusinessProcessToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");
		created.Should().Contain(CommandExecutionResult.CompileRequiredWarningMarker,
			because: "a new script task owes a compile before it can run");

		bool aliasLanded = false;
		bool compileMayBeRunning = false;
		CompileOutcome? secondCompile = null;
		try {
			// Act
			compileMayBeRunning = true;
			CompileOutcome firstCompile = await CompileAndWaitAsync(context, processName);
			compileMayBeRunning = false;
			string aliased = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
				ModifyBusinessProcessTool.ModifyBusinessProcessToolName, new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["operations"] = """[{"op":"addUsing","using":{"namespace":"Terrasoft.Core.Configuration.SysSettings","alias":"SysSettings"}}]"""
				}));
			aliasLanded = aliased.Contains(ExitCodeZero, StringComparison.Ordinal);
			compileMayBeRunning = true;
			secondCompile = await CompileAndWaitAsync(context, processName);
			compileMayBeRunning = false;
			string? restart = secondCompile.Succeeded ? await RestartOnNetCoreHostAsync(context) : null;
			CallToolResult ran = await ProcessDesignerE2EArrange.CallToolAsync(context, RunProcessTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["parameters"] = new Dictionary<string, object?> { ["Amount"] = 21 },
					["result-parameters"] = new[] { "Total", "Currency" }
				});

			// Assert
			firstCompile.Succeeded.Should().BeFalse(
				because: "SysSettings is ambiguous between Terrasoft.Configuration and Terrasoft.Core.Configuration");
			firstCompile.Text.Should().Contain("CS0104",
				because: "the compile answer carries the compiler's own error code");
			firstCompile.Text.Should().Contain("in the code of process",
				because: "the error sits in this process's generated file, and the answer must say so");
			aliasLanded.Should().BeTrue(because: "an alias on a non-default type is a valid using: {0}", aliased);
			secondCompile.Succeeded.Should().BeTrue(
				because: "the alias names the one SysSettings the body means: {0}", secondCompile.Text);
			if (restart is not null) {
				restart.Should().Contain(ExitCodeZero,
					because: "a .NET host runs the newly compiled code only after a restart: {0}", restart);
			}
			AssertRunReturnsTheComputedValues(ran);
		} finally {
			// A compile whose outcome is unknown may still be running, and deleting a schema under it is not safe;
			// otherwise the process is deleted unless it reached the successful compile, since a process whose
			// code does not compile breaks every later compile of its package.
			if (compileMayBeRunning) {
				await TestContext.Error.WriteLineAsync($"The compile outcome is unknown; retained '{processName}'. "
					+ "Delete it once the compile has stopped, or every later process-name compile of its package "
					+ "may fail on it.");
			} else if (!aliasLanded || secondCompile?.Succeeded != true) {
				await DeleteProcessAsync(context.EnvironmentName, processName);
			}
		}
	}

	[Test]
	[Description("A new version of a script-task process, compiled with process-name and THEN activated, runs its new code with no second compile: activation re-saves the version family but changes no code, and its warning says the earlier compile still covers the version. The order the guidance gives an agent - compile the version, activate it, then verify it on a run - end to end; an answer that demanded another compile here sent an agent to reload the stand for nothing (measured on a .NET Framework stand, 2026-10-02).")]
	[AllureTag(SetActiveProcessVersionTool.SetActiveProcessVersionToolName)]
	[AllureName("A version compiled before its activation runs its new code without a second compile")]
	public async Task NewVersion_CompiledBeforeActivation_Should_RunItsNewCode_WithoutASecondCompile() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"This fixture compiles the Custom package, reloading the runtime for every user of the stand. "
			+ "Run it by hand against an owned stand.");
		if (!TestConfiguration.Load().AllowDestructiveMcpTests) {
			Assert.Ignore("Opt in with McpE2E__AllowDestructiveMcpTests for this local compile test.");
		}

		// Arrange
		await using ProcessDesignerArrangeContext context = await ProcessDesignerE2EArrange.StartAsync(
			"ScriptTask version activation", MinimumActivationCoversCompilePackageVersion,
			sessionTimeout: TimeSpan.FromMinutes(15));
		string processName = $"UsrClioBpActivateCompiledE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateBusinessProcessTool.CreateBusinessProcessToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildMultiplyDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");
		string versionName = ProcessDesignerE2EArrange.CreatedVersionName(await ProcessDesignerE2EArrange.CallToolAsync(
			context, ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["package-name"] = "Custom",
				["operations"] = """[{"op":"setElement","elementName":"Compute","elementUpdate":{"scriptTask":{"body":"Set(\"Total\", Get<int>(\"Amount\") * 3);\nreturn true;"}}}]"""
			}));

		bool compileMayBeRunning = false;
		try {
			// Act
			compileMayBeRunning = true;
			CompileOutcome compiled = await CompileAndWaitAsync(context, versionName);
			compileMayBeRunning = false;
			string? restart = compiled.Succeeded ? await RestartOnNetCoreHostAsync(context) : null;
			string activated = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
				SetActiveProcessVersionTool.SetActiveProcessVersionToolName, new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["version-name"] = versionName
				}));
			CallToolResult ran = await ProcessDesignerE2EArrange.CallToolAsync(context, RunProcessTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = versionName,
					["parameters"] = new Dictionary<string, object?> { ["Amount"] = 7 },
					["result-parameters"] = new[] { "Total" }
				});

			// Assert
			compiled.Succeeded.Should().BeTrue(because: "the new version's body is valid C#: {0}", compiled.Text);
			if (restart is not null) {
				restart.Should().Contain(ExitCodeZero,
					because: "a .NET host runs the newly compiled code only after a restart: {0}", restart);
			}
			activated.Should().Contain(ExitCodeZero, because: "the activation itself succeeds: {0}", activated);
			activated.Should().Contain("owes no second compile",
				because: "the warning must say that the compile made before the activation still covers the version, "
					+ "or an agent asks the user for a reload the run below shows is not needed: {0}", activated);
			using JsonDocument run = JsonDocument.Parse(ran.Content.OfType<TextContentBlock>().First().Text);
			run.RootElement.GetProperty("status").GetString().Should().Be("completed",
				because: "the activated version was compiled before its activation and needs no other compile: {0}",
				run.RootElement.GetRawText());
			run.RootElement.GetProperty("resultParameterValues").GetProperty("Total").ToString().Should().Be("21",
				because: "the run executes the NEW version's body, which triples the amount; the first version's "
					+ "doubles it (14)");
		} finally {
			// Like the other test: a process that reached a compile stays, under its unique name; one whose compile
			// outcome is unknown stays too, since deleting a schema under a running compile is not safe.
			if (compileMayBeRunning) {
				await TestContext.Error.WriteLineAsync($"The compile outcome is unknown; retained '{processName}' and "
					+ $"'{versionName}'. Delete them once the compile has stopped.");
			}
		}
	}

	/// <summary>
	/// Restarts the application when the stand is a .NET host, where newly compiled code runs only after a restart,
	/// and returns the restart's answer; <c>null</c> on .NET Framework, where the compile's own reload is enough.
	/// </summary>
	/// <remarks>
	/// The wait stays under the ~150 s MCP response deadline: past it the tool answers "in progress" with exit-code
	/// 0, which would let a following run start against an application still warming up.
	/// </remarks>
	private static async Task<string?> RestartOnNetCoreHostAsync(ProcessDesignerArrangeContext context) {
		if (!await IsNetCoreHostAsync(context)) {
			return null;
		}
		return JsonSerializer.Serialize(await context.Session.CallToolAsync(
			RestartTool.RestartByEnvironmentNameToolName,
			new Dictionary<string, object?> {
				["environmentName"] = context.EnvironmentName,
				["waitReady"] = true,
				["waitTimeoutSeconds"] = 120
			},
			context.CancellationTokenSource.Token));
	}

	// Measured on a .NET 8 stand (2026-09-28): after a clean process-name compile the process kept answering
	// "Publish ... before starting it" until the application restarted. On .NET Framework the compile's own reload
	// is enough, and not restarting there is what keeps this fixture proving it.
	private static async Task<bool> IsNetCoreHostAsync(ProcessDesignerArrangeContext context) {
		CallToolResult listed = await context.Session.CallToolAsync(ShowWebAppListTool.ShowWebAppListToolName,
			new Dictionary<string, object?>(), context.CancellationTokenSource.Token);
		using JsonDocument payload = JsonDocument.Parse(listed.Content.OfType<TextContentBlock>().First().Text);
		JsonElement environment = payload.RootElement.GetProperty("environments").EnumerateArray().First(item =>
			string.Equals(item.GetProperty("name").GetString(), context.EnvironmentName, StringComparison.OrdinalIgnoreCase));
		return environment.GetProperty("isNetCore").GetBoolean();
	}

	private static void AssertRunReturnsTheComputedValues(CallToolResult ran) {
		using JsonDocument run = JsonDocument.Parse(ran.Content.OfType<TextContentBlock>().First().Text);
		run.RootElement.GetProperty("status").GetString().Should().Be("completed",
			because: "the compiled process must start and finish: {0}", run.RootElement.GetRawText());
		JsonElement values = run.RootElement.GetProperty("resultParameterValues");
		values.GetProperty("Total").ToString().Should().Be("42",
			because: "the process runs the code it was compiled from, which doubles the amount");
		values.GetProperty("Currency").ToString().Should().NotBeNullOrWhiteSpace(
			because: "the aliased SysSettings resolves the primary currency at run time");
	}

	/// <summary>
	/// Starts a process-name compile and returns its outcome: the tool's own answer when it is final, otherwise
	/// the state of THAT operation read through compile-status - the path an agent takes once the MCP response
	/// deadline returns the compile as still in progress.
	/// </summary>
	/// <remarks>
	/// The final answer is preferred because it carries every compiler error line the server sent, the
	/// process's own first, while compile-status keeps only the tail of the output. Polling names the
	/// operation-id so a compile that was refused before it started cannot read as the previous one's result.
	/// </remarks>
	private static async Task<CompileOutcome> CompileAndWaitAsync(ProcessDesignerArrangeContext context,
			string processName) {
		CallToolResult compiled = await ProcessDesignerE2EArrange.CallToolAsync(context,
			CompileCreatioTool.CompileCreatioToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			});
		string answer = compiled.Content.OfType<TextContentBlock>().First().Text;
		Match inProgress = OperationId.Match(answer);
		if (!inProgress.Success) {
			return new CompileOutcome(answer.Contains("\"exit-code\":0", StringComparison.Ordinal), answer);
		}
		while (true) {
			CallToolResult status = await context.Session.CallToolAsync(CompileStatusTool.CompileStatusToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = context.EnvironmentName,
						["operation-id"] = inProgress.Groups[1].Value
					}
				},
				context.CancellationTokenSource.Token);
			string text = status.Content.OfType<TextContentBlock>().First().Text;
			using JsonDocument payload = JsonDocument.Parse(text);
			string? state = payload.RootElement.GetProperty("status").GetString();
			if (state != "running") {
				return new CompileOutcome(state == "succeeded", text);
			}
			await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationTokenSource.Token);
		}
	}

	// The in-progress notice names the operation as "(operation-id '<id>')"; System.Text.Json writes the
	// apostrophes as \u0027, so both spellings are accepted.
	private static readonly Regex OperationId = new(@"operation-id (?:'|\\u0027)([0-9a-fA-F-]+)(?:'|\\u0027)",
		RegexOptions.CultureInvariant);

	private static async Task DeleteProcessAsync(string environmentName, string processName) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(3));
		ClioCliCommandResult deleted = await ClioCliCommandRunner.RunAsync(settings,
			["delete-schema", processName, "--remote", "-e", environmentName], cancellationToken: cleanup.Token);
		if (deleted.ExitCode != 0) {
			await TestContext.Error.WriteLineAsync($"Could not delete '{processName}', which does not compile and "
				+ $"will fail every later process-name compile of its package until it is deleted: "
				+ $"{deleted.StandardOutput} {deleted.StandardError}");
		}
	}

	/// <summary>What one compile ended with, and the text that says so.</summary>
	private sealed record CompileOutcome(bool Succeeded, string Text);

	private static string BuildMultiplyDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Activate Compiled E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "Amount", "type": "Integer", "direction": "In" },
		    { "name": "Total", "type": "Integer", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Compute", "type": "scriptTask", "caption": "Compute",
		      "scriptTask": { "body": "Set(\"Total\", Get<int>(\"Amount\") * 2);\nreturn true;" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Compute" },
		    { "source": "Compute", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Compile Lifecycle E2E",
		  "packageName": "Custom",
		  "usings": [ { "namespace": "Terrasoft.Configuration" } ],
		  "parameters": [
		    { "name": "Amount", "type": "Integer", "direction": "In" },
		    { "name": "Total", "type": "Integer", "direction": "Out" },
		    { "name": "Currency", "type": "Text", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Compute", "type": "scriptTask", "caption": "Compute",
		      "scriptTask": { "body": "object currency = SysSettings.GetValue(UserConnection, \"PrimaryCurrency\");\nSet(\"Total\", Get<int>(\"Amount\") * 2);\nSet(\"Currency\", currency == null ? string.Empty : currency.ToString());\nreturn true;" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Compute" },
		    { "source": "Compute", "target": "EndEvent1" }
		  ]
		}
		""";
}
