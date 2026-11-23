using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Shared arrange and read steps of the Timer start e2e cases (ENG-91849) in <c>CreateBusinessProcessToolE2ETests</c>
/// and <c>ModifyBusinessProcessToolE2ETests</c>.
/// </summary>
/// <remarks>
/// <para>Every timer these cases save registers a REAL Quartz job on the stand, so each one gets a far-future window:
/// a recurring kind starts at <see cref="FarFutureStart"/> and a once timer fires at <see cref="FarFutureOnce"/>. Both
/// lie inside the package's never-fires horizon (2099), so the job exists and <c>scheduled.nextFireTimeUtc</c> can be
/// asserted, while no run can happen even if a cleanup is lost. A case that needs a past or near date is a refusal
/// case and creates nothing.</para>
/// <para>Every process a case builds is recorded BEFORE the call and removed in <c>finally</c>, newest first. The
/// cases run strictly in sequence: schema writes in a parallel burst take a .NET Framework stand's app pool down.</para>
/// </remarks>
internal static class TimerStartE2ESupport {

	/// <summary>
	/// The CrtProcessBuilder the timer cases are written against: the first cut that ships the <c>timerStart</c>
	/// element, its <c>timer</c> block and <c>setTimer</c> - also the create and modify floor. Below it every case is
	/// Ignored.
	/// </summary>
	internal const string MinimumPackageVersion = "1.6.6.187";

	/// <summary>What the skip message calls these tests.</summary>
	internal const string Subject = "Timer start element";

	/// <summary>The build token describe reports for a top-level timer start.</summary>
	internal const string TimerBuildType = "timerstart";

	/// <summary>The window start of every recurring e2e timer (wall clock in the timer's zone).</summary>
	internal const string FarFutureStart = "2090-01-01T00:00";

	/// <summary>The instant of every once e2e timer (wall clock in the timer's zone).</summary>
	internal const string FarFutureOnce = "2090-06-15T09:30";

	/// <summary>The zone the deterministic cases name, so their cron and fire times do not depend on the stand.</summary>
	internal const string Zone = "UTC";

	/// <summary>
	/// The reason the package gives for every "every N weeks" refusal (Q12) - on <c>week</c> with <c>every</c>, on a
	/// <c>day</c> step that is a multiple of 7 and on a <c>cron</c> day-of-month step that is one.
	/// </summary>
	internal const string EveryWeeksReason = "every N weeks is not supported by the platform scheduler";

	/// <summary>The default-zone refusal of a stand where neither the profile nor DefaultTimeZone names a zone (Q2).</summary>
	internal const string NoDefaultZoneReason = "neither your profile nor the";

	/// <summary>The scheduler state of a timer the platform will run as saved.</summary>
	internal const string ScheduledState = "scheduled";

	/// <summary>
	/// The scheduler state of a stand whose scheduler read failed: whether the job exists is unknown there, so a case
	/// asserting the job is Ignored rather than failed.
	/// </summary>
	internal const string SchedulerUnavailableState = "schedulerUnavailable";

	/// <summary>
	/// What run-process answers when the platform refuses a manual start (<c>ProcessCannotBeManuallyStartedException</c>):
	/// the sentence only that refusal carries, so any other launch failure does not satisfy it.
	/// </summary>
	internal const string ManualStartRefusal = "has no manual entry point";

	/// <summary>The run-process status of a launch the platform refused before anything ran.</summary>
	internal const string NotStartedStatus = "not-started";

	/// <summary>Opens a session, or ignores the test when the stand is missing, unreachable or behind the cut.</summary>
	internal static Task<ProcessDesignerArrangeContext> StartAsync() =>
		ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));

	/// <summary>A process code unique to this run, in the <c>UsrClioBpTimer&lt;Case&gt;E2e</c> family.</summary>
	internal static string NewProcessName(string caseName) => $"UsrClioBpTimer{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>A day timer at 02:30 in <see cref="Zone"/> whose window starts at <see cref="FarFutureStart"/>.</summary>
	internal static string DailyTimerJson(string at = "02:30") =>
		$$"""{ "schedule": "day", "at": "{{at}}", "start": "{{FarFutureStart}}", "timeZone": "{{Zone}}" }""";

	/// <summary>A <c>timerStart</c> element with the given <c>timer</c> block.</summary>
	internal static string TimerElementJson(string name, string timerJson) =>
		$$"""{ "name": "{{name}}", "type": "timerStart", "caption": "{{name}}", "timer": {{timerJson}} }""";

	/// <summary>
	/// A process whose given start elements each flow into one Terminate end, optionally beside a Simple start
	/// <c>Start1</c> that flows there too.
	/// </summary>
	internal static string Descriptor(string processName, IReadOnlyList<(string Name, string Json)> starts,
			bool withSimpleStart = false) {
		List<string> elements = [.. starts.Select(start => start.Json)];
		List<string> sources = [.. starts.Select(start => start.Name)];
		if (withSimpleStart) {
			elements.Insert(0, """{ "name": "Start1", "type": "startEvent" }""");
			sources.Insert(0, "Start1");
		}
		elements.Add("""{ "name": "End1", "type": "endEvent" }""");
		string flows = string.Join(", ", sources.Select(source => $$"""{ "source": "{{source}}", "target": "End1" }"""));
		return $$"""
			{ "name": "{{processName}}", "caption": "Clio BP Timer E2E", "packageName": "Custom",
			  "elements": [ {{string.Join(", ", elements)}} ], "flows": [ {{flows}} ] }
			""";
	}

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer never
	/// arrived must still be removed. Ignores the test when clio refused the call because the stand's CrtProcessBuilder
	/// is behind.
	/// </summary>
	internal static async Task<CallToolResult> CreateAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor) {
		created.Add(processName);
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateBusinessProcessTool.CreateBusinessProcessToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = descriptor
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>Builds a process for an ARRANGE step and fails the test on the spot unless it was created.</summary>
	internal static async Task ArrangeProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what) {
		CallToolResult result = await CreateAsync(context, created, processName, descriptor);
		ShouldHaveBuilt(result, $"{what} is an arrange step, so every assertion below is about the wrong failure if "
			+ "it was not built");
	}

	/// <summary>Applies a modify-business-process operations array to a process.</summary>
	internal static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyBusinessProcessTool.ModifyBusinessProcessToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>Reads a process back in full and returns its graph.</summary>
	internal static async Task<JsonObject> DescribeGraphAsync(ProcessDesignerArrangeContext context,
			string processName) =>
		DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

	/// <summary>The one element of a described graph with the given name.</summary>
	internal static JsonObject Element(JsonObject graph, string name) =>
		(graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Should().ContainSingle(element => Text(element, "name") == name,
				because: $"the process carries one element named {name}").Which;

	/// <summary>The described elements whose build token is <c>timerstart</c>.</summary>
	internal static IReadOnlyList<JsonObject> TimerStarts(JsonObject graph) =>
		[.. (graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Where(element => Text(element, "buildType") == TimerBuildType)];

	/// <summary>The described elements whose build token is <c>startevent</c>.</summary>
	internal static IReadOnlyList<JsonObject> SimpleStarts(JsonObject graph) =>
		[.. (graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Where(element => Text(element, "buildType") == "startevent")];

	/// <summary>The <c>timer</c> block of a described timer start; fails when describe reports none.</summary>
	internal static JsonObject TimerBlock(JsonObject element) {
		element["timer"].Should().BeOfType<JsonObject>(
			because: "a timer start must be described with its timer block; what was read: {0}", element.ToJsonString());
		return element["timer"]!.AsObject();
	}

	/// <summary>The <c>timer.scheduled</c> state of a described timer start; fails when describe reports none.</summary>
	internal static JsonObject Scheduled(JsonObject timer) {
		timer["scheduled"].Should().BeOfType<JsonObject>(
			because: "describe reads the scheduler for every top-level timer start; what was read: {0}",
			timer.ToJsonString());
		return timer["scheduled"]!.AsObject();
	}

	/// <summary>
	/// The write members of a described timer block, with the describe-only <c>storedCron</c>, <c>designerKind</c> and
	/// <c>scheduled</c> left out - what a rebuild must reproduce.
	/// </summary>
	internal static JsonObject WriteShape(JsonObject timer) {
		JsonObject copy = timer.DeepClone().AsObject();
		foreach (string readBack in new[] { "storedCron", "designerKind", "scheduled" }) {
			copy.Remove(readBack);
		}
		return copy;
	}

	/// <summary>
	/// Asserts a described timer is scheduled by the platform, with a next fire time in the far-future window. Ignores the
	/// test on a stand whose scheduler read reports <see cref="SchedulerUnavailableState"/>: the job cannot be checked
	/// there, which is the stand's state and not the feature's.
	/// </summary>
	internal static void ShouldBeScheduledInTheFarFuture(JsonObject timer, string what) {
		JsonObject scheduled = Scheduled(timer);
		if (Text(scheduled, "state") == SchedulerUnavailableState) {
			Assert.Ignore($"{what}: the stand's scheduler read reports {SchedulerUnavailableState} "
				+ $"({Text(scheduled, "detail") ?? "no detail"}), so whether the platform holds the job cannot be checked here.");
		}
		Text(scheduled, "state").Should().Be(ScheduledState,
			because: $"{what}: a new process is enabled and its own active version, so the save registers the job; "
				+ "scheduled reads: {0}", scheduled.ToJsonString());
		(Text(scheduled, "nextFireTimeUtc") ?? string.Empty).Should().StartWith("2090-",
			because: $"{what}: the window starts in 2090, so the first run is there and nothing runs before it");
	}

	/// <summary>Removes every process a test built, newest first; a failed delete is reported, not thrown.</summary>
	internal static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	/// <summary>The raw JSON text of a value, for an assertion message.</summary>
	internal static string Json(JsonNode? node) => node?.ToJsonString() ?? "null";

}
