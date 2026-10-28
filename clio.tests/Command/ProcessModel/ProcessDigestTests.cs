using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// The compact views of a described process (ENG-103023), over two describe results captured from CAADT process
/// builds (green build 16089398): <c>RequestHandling</c>, an agent-built request-handling process of 17 elements
/// whose compact graph is ~29 K characters - the size class that was spilled to a file on every describe - and
/// <c>ReviewChain</c>, a 5-element process that fits one reply in full.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessDigestTests {

	internal const string LargeFixture = "RequestHandling.describe.json";
	internal const string SmallFixture = "ReviewChain.describe.json";

	private static readonly JsonSerializerOptions WireOptions = new() { PropertyNameCaseInsensitive = true };

	/// <summary>Loads a captured describe result the way the describer binds the wire.</summary>
	internal static DescribeProcessResult LoadFixture(string name) {
		string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", "ProcessDescribe", name);
		return JsonSerializer.Deserialize<DescribeProcessResult>(File.ReadAllText(path), WireOptions);
	}

	private static string FullGraph(DescribeProcessResult process) =>
		JsonSerializer.Serialize(DescribeProcessCommand.OmitDecodedFilterPayloads(process),
			DescribeProcessCommand.OutputOptions);

	[TestCase(LargeFixture)]
	[TestCase(SmallFixture)]
	[Description("Holds the summary of both captured processes within its ~3 KB budget, and reports the before/after sizes the PR quotes.")]
	public void BuildSummary_ShouldStayWithinBudget_OnCapturedProcesses(string fixture) {
		// Arrange
		DescribeProcessResult process = LoadFixture(fixture);
		int fullChars = FullGraph(process).Length;

		// Act
		JsonObject summary = ProcessDigest.BuildSummary(process, ProcessDigest.Checksum(process));
		int summaryChars = ProcessDigest.Size(summary);
		TestContext.Out.WriteLine($"{fixture}: full {fullChars} chars, summary {summaryChars} chars");

		// Assert
		summaryChars.Should().BeLessThanOrEqualTo(ProcessDigest.SummaryBudgetBytes,
			because: "the summary exists to stay far below the ~20 KB line past which Copilot CLI spills a reply");
		summary["elements"]!.AsArray().Should().HaveCount(process.Elements.Count,
			because: "a process of this size fits whole, so nothing may be cut");
		summary["flows"]!.AsArray().Should().HaveCount(process.Flows.Count,
			because: "every flow fits as well");
		summary["omitted"].Should().BeNull(because: "nothing was cut");
	}

	[Test]
	[Description("Carries what the summary promises for the large captured process: type + caption per element, the two start signals with entity and trigger, typed parameters, the version standing and the checksum.")]
	public void BuildSummary_ShouldCarryElementsSignalsParametersAndVersion() {
		// Arrange
		DescribeProcessResult process = LoadFixture(LargeFixture);

		// Act
		JsonObject summary = ProcessDigest.BuildSummary(process, "abc123");

		// Assert
		JsonObject read = summary["elements"]!.AsArray().OfType<JsonObject>()
			.Single(element => element["name"]!.GetValue<string>() == "ReadFiledRequest");
		read["type"]!.GetValue<string>().Should().Be("ReadDataUserTask",
			because: "a user task is typed by its user-task schema, the name a caller writes back");
		read["caption"]?.GetValue<string>().Should().Be("Read the filed request",
			because: "a caption differing from the name is kept");
		summary["startSignals"]!.AsArray().Select(signal => signal!["on"]!.GetValue<string>())
			.Should().BeEquivalentTo(["added", "modified"], because: "both signal starts are reported with their trigger");
		summary["parameters"]!.AsArray().OfType<JsonObject>()
			.Single(parameter => parameter["name"]!.GetValue<string>() == "EmployeeIdParameter")["type"]!
			.GetValue<string>().Should().Be("Lookup(Contact)", because: "a lookup parameter names its object");
		summary["isActiveVersion"]!.GetValue<bool>().Should().BeFalse(
			because: "this capture is the family root while version 1 runs");
		summary["activeVersionName"]!.GetValue<string>().Should().Be("UsrReqHnd0925170438ReqHandlingCustom1",
			because: "a summary of a graph that does not run must name the one that does");
		summary["checksum"]!.GetValue<string>().Should().Be("abc123", because: "the checksum is carried through");
	}

	[Test]
	[Description("Cuts a process too large for the budget down to it - captions first, then list tails - and says how much it left out instead of exceeding the budget.")]
	public void BuildSummary_ShouldTruncateAndReportOmissions_WhenTheProcessIsTooLarge() {
		// Arrange
		DescribeProcessResult process = LoadFixture(LargeFixture);
		List<DescribedElement> many = [];
		List<DescribedFlow> flows = [];
		for (int index = 0; index < 120; index++) {
			many.Add(new DescribedElement {
				Name = $"FormulaStepNumber{index}", Caption = $"Compute the value of step {index}",
				BuildType = "formulatask"
			});
			flows.Add(new DescribedFlow { Source = $"FormulaStepNumber{index}", Target = $"FormulaStepNumber{index + 1}" });
		}
		process.Elements = many;
		process.Flows = flows;
		process.Parameters = [.. Enumerable.Range(0, 150).Select(index =>
			new DescribedParameter { Name = $"ProcessParameterNumber{index}", Type = "Text", Direction = "Variable" })];

		// Act
		JsonObject summary = ProcessDigest.BuildSummary(process, "abc123");

		// Assert
		ProcessDigest.Size(summary).Should().BeLessThanOrEqualTo(ProcessDigest.SummaryBudgetBytes,
			because: "the budget holds whatever the process size");
		int omittedElements = summary["omitted"]!["elements"]!.GetValue<int>();
		int omittedFlows = summary["omitted"]!["flows"]!.GetValue<int>();
		(summary["elements"]!.AsArray().Count + omittedElements).Should().Be(120,
			because: "what is listed plus what is reported omitted is the whole element list");
		(summary["flows"]!.AsArray().Count + omittedFlows).Should().Be(120,
			because: "the same accounting holds for flows");
		(summary["parameters"]!.AsArray().Count + summary["omitted"]!["parameters"]!.GetValue<int>()).Should().Be(150,
			because: "parameters are cut and accounted for like the other lists");
		summary["elements"]!.AsArray().Should().NotBeEmpty(
			because: "every list keeps the same share, so a long parameter list cannot empty the element list");
		summary["elements"]!.AsArray().OfType<JsonObject>().Should().OnlyContain(element => element["caption"] == null,
			because: "captions are the first thing a too-large summary gives up");
	}

	[Test]
	[Description("Keeps the checksum stable across a layout change, a different version standing, a tracing switch and a caption-only edit (culture-resolved text is left out), and moves it when the structure changes.")]
	public void Checksum_ShouldIgnoreLayoutAndVersionFacts_AndTrackContent() {
		// Arrange
		DescribeProcessResult original = LoadFixture(LargeFixture);
		DescribeProcessResult relaidOut = LoadFixture(LargeFixture);
		relaidOut.Elements[0].Position = "999;999";
		relaidOut.Flows[0].Geometry = null;
		relaidOut.IsActiveVersion = true;
		relaidOut.Versions = [];
		relaidOut.Tracing = new DescribedProcessTracing { Enabled = true, TurnOffDate = "2026-10-20" };
		DescribeProcessResult edited = LoadFixture(LargeFixture);
		edited.Elements.RemoveAll(element => element.Name == "AskEmployeeToDoBetter");
		DescribeProcessResult recaptioned = LoadFixture(LargeFixture);
		recaptioned.Elements.First(element => element.Name == "ComposeTaskTitle").Caption = "Compose another title";

		// Act
		string before = ProcessDigest.Checksum(original);
		string afterLayout = ProcessDigest.Checksum(relaidOut);
		string afterEdit = ProcessDigest.Checksum(edited);
		string afterCaption = ProcessDigest.Checksum(recaptioned);

		// Assert
		before.Should().MatchRegex("^[0-9a-f]{12}$", because: "the checksum is short lower-case hex");
		afterLayout.Should().Be(before,
			because: "the server re-derives layout on every structural edit, and version facts and tracing belong to the family");
		afterEdit.Should().NotBe(before, because: "a content change has to show in the checksum");
		afterCaption.Should().Be(before,
			because: "captions are culture-resolved text, left out so a profile-culture read and an en-US read agree");
	}

	[Test]
	[Description("Reports elements, flows and parameters added, removed and changed, ignores layout and element identities, and returns an empty object for two equal reads.")]
	public void BuildChanges_ShouldListWhatDiffers_AndNothingElse() {
		// Arrange
		DescribeProcessResult before = LoadFixture(LargeFixture);
		DescribeProcessResult after = LoadFixture(LargeFixture);
		after.Elements.RemoveAll(element => element.Name == "AskEmployeeToDoBetter");
		after.Elements.Add(new DescribedElement { Name = "NotifyManager", BuildType = "usertask" });
		after.Elements.First(element => element.Name == "ComposeTaskTitle").Caption = "Compose another title";
		after.Elements.First(element => element.Name == "ReadEmployee").Position = "1;1";
		after.Flows.RemoveAll(flow => flow.Target == "AskEmployeeToDoBetter");
		after.Flows.Add(new DescribedFlow { Source = "DaysLeftDecision", Target = "NotifyManager", Kind = "default" });
		after.Parameters.Add(new DescribedParameter { Name = "ManagerParameter", Type = "Lookup" });

		// Act
		JsonObject changes = ProcessDigest.BuildChanges(before, after);
		DescribeProcessResult copy = LoadFixture(LargeFixture);
		copy.Elements.ForEach(element => element.Uid = Guid.NewGuid().ToString());
		JsonObject none = ProcessDigest.BuildChanges(before, copy);

		// Assert
		Names(changes, "elementsAdded").Should().Equal(["NotifyManager"], because: "one element was added");
		Names(changes, "elementsRemoved").Should().Equal(["AskEmployeeToDoBetter"], because: "one was removed");
		Names(changes, "elementsChanged").Should().Equal(["ComposeTaskTitle"],
			because: "a caption edit is a change, while a moved element is not");
		Names(changes, "flowsAdded").Should().Equal(["DaysLeftDecision -> NotifyManager [default]"],
			because: "flows are named the way the summary writes them");
		Names(changes, "flowsRemoved").Should().ContainSingle(flow => flow.StartsWith("DaysLeftDecision -> AskEmployeeToDoBetter"),
			because: "the flow into the removed element went with it");
		Names(changes, "parametersAdded").Should().Equal(["ManagerParameter"], because: "a parameter was added");
		none.Should().BeEmpty(
			because: "two reads of the same content differ in nothing, even when a saved copy carries fresh element UIds");
	}

	[Test]
	[Description("Removing the first of two parallel conditional flows between the same elements reports that flow as removed, and does not report the surviving flow as changed.")]
	public void BuildChanges_ShouldPairParallelFlowsByContent() {
		// Arrange
		DescribeProcessResult before = new() {
			Flows = [
				new DescribedFlow { Source = "Gateway", Target = "Task", Kind = "conditional", Condition = "[#A#] > 1" },
				new DescribedFlow { Source = "Gateway", Target = "Task", Kind = "conditional", Condition = "[#B#] > 2" }
			]
		};
		DescribeProcessResult after = new() {
			Flows = [new DescribedFlow { Source = "Gateway", Target = "Task", Kind = "conditional", Condition = "[#B#] > 2" }]
		};

		// Act
		JsonObject changes = ProcessDigest.BuildChanges(before, after);

		// Assert
		Names(changes, "flowsRemoved").Should().Equal(["Gateway -> Task [conditional, condition]"],
			because: "exactly one of the two parallel flows was removed");
		changes.ContainsKey("flowsChanged").Should().BeFalse(because: "the surviving flow is unchanged");
		changes.ContainsKey("flowsAdded").Should().BeFalse(because: "nothing was added");
	}

	[Test]
	[Description("element=<name> prefers the element whose name matches exactly, and lists only that element's flows when another element's name differs only in case.")]
	public void BuildElement_ShouldPreferTheExactNameAndItsOwnFlows() {
		// Arrange
		DescribeProcessResult process = new() {
			Elements = [new DescribedElement { Name = "task" }, new DescribedElement { Name = "Task" }],
			Flows = [
				new DescribedFlow { Source = "Start", Target = "task" },
				new DescribedFlow { Source = "Start", Target = "Task" }
			]
		};

		// Act
		JsonObject view = ProcessDigest.BuildElement(process, "Task", "c");

		// Assert
		view["element"]!["name"]!.GetValue<string>().Should().Be("Task", because: "the exact name wins");
		view["flowsIn"]!.AsArray().Should().ContainSingle(because: "only the flow into this element is its own");
	}

	[TestCase(null, null, null, "A -> B")]
	[TestCase("sequence", null, null, "A -> B")]
	[TestCase("default", null, null, "A -> B [default]")]
	[TestCase("conditional", "[#X#] > 1", null, "A -> B [conditional, condition]")]
	[TestCase("conditional", null, true, "A -> B [conditional, results]")]
	[TestCase("conditional", null, false, "A -> B [conditional, NO condition]")]
	[TestCase("sequence", "[#X#] > 1", null, "A -> B [condition text, not evaluated]")]
	[Description("Writes a flow as one line that says, for a conditional branch, what decides it - including the case where nothing does.")]
	public void FlowLine_ShouldNameKindAndWhatDecidesTheBranch(string kind, string condition, bool? onResults,
		string expected) {
		// Arrange
		DescribedFlow flow = new() {
			Source = "A", Target = "B", Kind = kind, Condition = condition, BranchesOnActivityResult = onResults
		};

		// Act
		string line = ProcessDigest.FlowLine(flow);

		// Assert
		line.Should().Be(expected, because: "the summary line is the only place a flow's kind shows");
	}

	[Test]
	[Description("Returns one element in full - its parameters with values included - with the flows that enter and leave it, and null for a name the process does not have.")]
	public void BuildElement_ShouldReturnTheElementInFullWithItsFlows() {
		// Arrange
		DescribeProcessResult process = LoadFixture(LargeFixture);

		// Act
		JsonObject view = ProcessDigest.BuildElement(process, "daysleftdecision", "abc123");
		JsonObject missing = ProcessDigest.BuildElement(process, "NoSuchElement", "abc123");

		// Assert
		view!["mode"]!.GetValue<string>().Should().Be("element", because: "the reply names its mode");
		view["element"]!["name"]!.GetValue<string>().Should().Be("DaysLeftDecision",
			because: "the name is matched case-insensitively");
		view["flowsOut"]!.AsArray().Should().HaveCount(2, because: "the gateway has a conditional and a default branch");
		view["flowsOut"]!.AsArray().OfType<JsonObject>().Should().Contain(flow => flow["condition"] != null,
			because: "the element view carries flows in full, conditions included");
		missing.Should().BeEmpty(because: "an unknown element is the caller's error to report");
	}

	private static IEnumerable<string> Names(JsonObject changes, string member) =>
		changes[member]?.AsArray().Select(node => node!.GetValue<string>()) ?? [];
}
