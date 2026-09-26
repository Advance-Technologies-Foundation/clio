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
/// <c>McpE2E__AllowDestructiveMcpTests=true</c>.</para>
/// <para>A process that reached the successful compile is left on the stand under a unique
/// <c>UsrClioBpCompileLifecycleE2e*</c> name, like the other process-designer fixtures leave theirs: deleting a
/// process whose code was compiled into the shared assembly would owe yet another compile. A run that stops
/// BEFORE the alias lands deletes its process instead: a script task that does not compile, left in
/// <c>Custom</c>, would fail every later process-name compile of that package with CS0104 - this test's next run
/// included. Its failed compile never reached the assembly, so the delete owes nothing.</para>
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

	[Test]
	[Description("A script task that references SysSettings under a Terrasoft.Configuration import fails the process-name compile with CS0104 flagged as the process's own error; an aliased using added through modify-business-process makes the second compile succeed, and the run returns the values the code computes - the path an agent takes to repair a colleague's process, end to end.")]
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
		try {
			// Act
			CompileOutcome firstCompile = await CompileAndWaitAsync(context, processName);
			string aliased = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
				ModifyBusinessProcessTool.ModifyBusinessProcessToolName, new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["operations"] = """[{"op":"addUsing","using":{"namespace":"Terrasoft.Core.Configuration.SysSettings","alias":"SysSettings"}}]"""
				}));
			aliasLanded = aliased.Contains(ExitCodeZero, StringComparison.Ordinal);
			CompileOutcome secondCompile = await CompileAndWaitAsync(context, processName);
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
			AssertRunReturnsTheComputedValues(ran);
		} finally {
			if (!aliasLanded) {
				await DeleteProcessAsync(context.EnvironmentName, processName);
			}
		}
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
