using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessDescriptorPreflight"/>: the create descriptor is read the way the server reads
/// it, the REAL validator runs over it, and only the findings the server is silent about come back.
/// </summary>
/// <remarks>
/// The validator is not substituted. What this class decides is WHICH of the validator's findings reach a create
/// caller, and a substitute returning hand-picked findings would test the filter against findings the rules never
/// produce - the flag placement in <see cref="ProcessGraphValidator"/> is half of the behaviour.
/// </remarks>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessDescriptorPreflightTests {

	private readonly IProcessDescriptorPreflight _preflight = new ProcessDescriptorPreflight(new ProcessGraphValidator());

	private static JsonObject Descriptor(string json) => JsonNode.Parse(json)!.AsObject();

	// Start -> XOR -> (two conditional branches, both into a parallel join) -> End: the deadlock R8 exists for.
	private const string ParallelJoinBehindAChoice = """
		{"name":"UsrP","elements":[
		  {"name":"Start1","type":"startEvent"},
		  {"name":"Decide","type":"exclusiveGateway"},
		  {"name":"Left","type":"performTask"},
		  {"name":"Right","type":"performTask"},
		  {"name":"Join","type":"parallelGateway"},
		  {"name":"End1","type":"endEvent"}],
		 "flows":[
		  {"source":"Start1","target":"Decide"},
		  {"source":"Decide","target":"Left","kind":"conditional","condition":"[#Amount#] > 100"},
		  {"source":"Decide","target":"Right","kind":"default"},
		  {"source":"Left","target":"Join"},
		  {"source":"Right","target":"Join"},
		  {"source":"Join","target":"End1"}]}
		""";

	[Test]
	[Description("A parallel join fed by the two branches of one exclusive choice comes back as an R8 warning: the server has no such check and the instance hangs in Running with no error, which is the risk the pre-flight exists to surface.")]
	public void CheckCreateDescriptor_ShouldReportR8_WhenAParallelJoinWaitsBehindAChoice() {
		// Arrange
		JsonObject descriptor = Descriptor(ParallelJoinBehindAChoice);

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(
			because: "the graph is otherwise clean, so R8 is the one silent risk in it");
		warnings[0].Should().StartWith($"{ProcessDescriptorPreflight.WarningPrefix} R8",
			because: "the line must say it is clio's pre-flight and name the rule, so a caller can tell it from a "
				+ "server warning and look the rule up");
		warnings[0].Should().Contain("Join",
			because: "the finding names the join that would hang");
	}

	[Test]
	[Description("A diverging exclusive gateway with conditional flows and no default is reported as R7: the server saves it without a word, and at run time no matching condition suspends the instance.")]
	public void CheckCreateDescriptor_ShouldReportR7_WhenADecidingGatewayHasNoDefault() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[
			  {"name":"Start1","type":"startEvent"},{"name":"Decide","type":"exclusiveGateway"},
			  {"name":"Big","type":"performTask"},{"name":"Small","type":"performTask"},{"name":"End1","type":"endEvent"}],
			 "flows":[
			  {"source":"Start1","target":"Decide"},
			  {"source":"Decide","target":"Big","kind":"conditional","condition":"[#Amount#] > 100"},
			  {"source":"Decide","target":"Small","kind":"conditional","condition":"[#Amount#] <= 100"},
			  {"source":"Big","target":"End1"},{"source":"Small","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(line => line.Contains(" R7 ") && line.Contains("Decide"),
			because: "a missing default branch is a silent risk the build does not report");
	}

	[Test]
	[Description("R17 fires for both spellings a create descriptor can use for Add data - the dedicated addData token and the generic userTask route naming AddDataUserTask - because the pre-flight maps a generic user task to the schema it names.")]
	[TestCase("{\"name\":\"Add1\",\"type\":\"addData\"}")]
	[TestCase("{\"name\":\"Add1\",\"type\":\"userTask\",\"userTaskName\":\"AddDataUserTask\"}")]
	public void CheckCreateDescriptor_ShouldReportR17_WhenAddDataChainsIntoANonReadData(string addDataElement) {
		// Arrange
		JsonObject descriptor = Descriptor($$"""
			{"elements":[{"name":"Start1","type":"startEvent"},{{addDataElement}},
			  {"name":"Mail","type":"sendEmail"},{"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"Start1","target":"Add1"},{"source":"Add1","target":"Mail"},
			  {"source":"Mail","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(line => line.Contains(" R17 "),
			because: "Add data outputs only the new Id, and nothing on the server says so");
	}

	[Test]
	[Description("A conditional flow off an event is reported as R13: the server stores it silently (a divergence pinned on the package side), so it is clio-only.")]
	public void CheckCreateDescriptor_ShouldReportR13_WhenAConditionalFlowLeavesAnEvent() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[{"name":"Start1","type":"startEvent"},{"name":"Task1","type":"performTask"},
			  {"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"Start1","target":"Task1","kind":"conditional","condition":"true"},
			  {"source":"Task1","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(line => line.Contains(" R13 "),
			because: "the origin half of R13 is the one the build does not report");
	}

	[Test]
	[Description("Nothing the server reports is repeated: an implicit split (R12, a build notice), a plain flow off a deciding gateway (R7, normalised with a notice), an omitted condition (R13, refused by FlowKindRules) and every ERROR (here R1 and R15) produce no pre-flight line - while the one silent risk in the same graph, R8, still does. Asserting exactly that one line is what makes this test able to fail: an empty result is also what a descriptor the pre-flight never read returns.")]
	public void CheckCreateDescriptor_ShouldShowOnlyTheSilentRisk_WhenEveryOtherFindingIsOneTheServerReports() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[
			  {"name":"Start1","type":"startEvent"},{"name":"Decide","type":"exclusiveGateway"},
			  {"name":"Left","type":"performTask"},{"name":"Right","type":"performTask"},
			  {"name":"Join","type":"parallelGateway"},{"name":"Split","type":"performTask"},
			  {"name":"A","type":"performTask"},{"name":"B","type":"performTask"},
			  {"name":"Decide2","type":"exclusiveGateway"},{"name":"End1","type":"endEvent"}],
			 "flows":[
			  {"source":"Start1","target":"Decide"},{"source":"Start1","target":"End1"},
			  {"source":"Decide","target":"Left","kind":"conditional","condition":"[#Amount#] > 100"},
			  {"source":"Decide","target":"Right","kind":"default"},
			  {"source":"Left","target":"Join"},{"source":"Right","target":"Join"},
			  {"source":"Join","target":"Split"},{"source":"Split","target":"A"},{"source":"Split","target":"B"},
			  {"source":"A","target":"Decide2"},
			  {"source":"Decide2","target":"End1","kind":"conditional"},
			  {"source":"Decide2","target":"B"},
			  {"source":"B","target":"Nowhere"},{"source":"B","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(
			because: "R12, the plain-flow R7, the omitted-condition R13, R1 and R15 are each refused or noticed by the "
				+ "server itself, and a pre-flight line in front of the server's message would say it twice");
		warnings[0].Should().Contain(" R8 ",
			because: "the parallel join behind the choice is the one risk here nothing on the server reports");
	}

	[Test]
	[Description("Flow endpoints are matched to element names trimmed and case-insensitively, the way ProcessGraphBuilder matches them. A default flow whose source is spelled 'decide' still belongs to the gateway 'Decide', so no false 'no default' R7 is reported for a gateway the server builds with one.")]
	public void CheckCreateDescriptor_ShouldMatchEndpointsLikeTheServer_WhenTheirCasingDiffersFromTheElementName() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[{"name":"Start1","type":"startEvent"},{"name":"Decide","type":"exclusiveGateway"},
			  {"name":"Big","type":"performTask"},{"name":"Small","type":"performTask"},{"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"start1","target":"DECIDE"},
			  {"source":"Decide","target":"Big","kind":"conditional","condition":"[#Amount#] > 100"},
			  {"source":" decide ","target":"small","kind":"default"},
			  {"source":"Big","target":"End1"},{"source":"Small","target":"end1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(
			because: "the server connects every one of these flows, so the pre-flight must judge the same graph "
				+ "rather than one with the differently spelled flows missing");
	}

	// Each case takes the R8 descriptor above and breaks ONE thing about it. Without the guard the case exists
	// for, the graph would still be read - and R8 would come back - so every case can fail, which the earlier
	// set, built on a bare start->end graph, mostly could not.
	private static IEnumerable<TestCaseData> UnreadableDescriptors() {
		yield return Broken("an unknown flow kind", root => Flow(root, 2)["kind"] = "defualt");
		yield return Broken("a condition that is not a string", root => Flow(root, 1)["condition"] = 42);
		yield return Broken("results that are not strings", root => Flow(root, 1)["results"] = new JsonArray(1));
		yield return Broken("an element that is not an object", root => Elements(root).Add("performTask"));
		yield return Broken("an element name that is not a string",
			root => Elements(root).Add(new JsonObject { ["name"] = 7, ["type"] = "performTask" }));
		yield return Broken("two names that differ only in case",
			root => Elements(root).Add(new JsonObject { ["name"] = "left", ["type"] = "performTask" }));
		yield return Broken("an element type the build cannot create",
			root => Elements(root).Add(new JsonObject { ["name"] = "Or1", ["type"] = "inclusiveGateway" }));
		yield return Broken("an element type nobody knows",
			root => Elements(root).Add(new JsonObject { ["name"] = "X1", ["type"] = "noSuchElement" }));
		yield return Broken("flows that are not an array", root => root["flows"] = new JsonObject());
		yield return Broken("no elements", root => root["elements"] = new JsonArray());
	}

	private static TestCaseData Broken(string what, Action<JsonObject> breakIt) {
		JsonObject root = Descriptor(ParallelJoinBehindAChoice);
		breakIt(root);
		return new TestCaseData(root.ToJsonString()).SetArgDisplayNames(what);
	}

	private static JsonArray Elements(JsonObject root) => root["elements"]!.AsArray();

	private static JsonObject Flow(JsonObject root, int index) => root["flows"]![index]!.AsObject();

	[Test]
	[Description("A descriptor the pre-flight cannot read the way the server does produces no lines at all: an unknown flow kind, a non-string condition or results entry, an element that is not an object or has a non-string name, two names that differ only in case, an element type the build cannot create or nobody knows, flows that are not an array, no elements. Validating a re-interpreted graph would answer about a process the caller did not describe, and the server refuses each of these with its own message - most of them before it looks at a single flow.")]
	[TestCaseSource(nameof(UnreadableDescriptors))]
	public void CheckCreateDescriptor_ShouldReturnNothing_WhenTheDescriptorIsNotAGraphItCanReadFaithfully(string json) {
		// Arrange
		JsonObject descriptor = Descriptor(json);

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(
			because: "a pre-flight that guesses what an unreadable descriptor meant reports on a different graph");
	}

	[Test]
	[Description("A user task whose schema name the validator does not classify (a custom 'UsrScoreLead') stays an ACTIVITY: the swap to userTaskName happens only for a name the rules know, so a conditional branch off it raises no false 'neither a gateway nor an activity' R13.")]
	public void CheckCreateDescriptor_ShouldKeepACustomUserTaskAnActivity_WhenItsSchemaNameIsUnclassified() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[{"name":"Start1","type":"startEvent"},
			  {"name":"Score","type":"userTask","userTaskName":"UsrScoreLead"},
			  {"name":"Hot","type":"performTask"},{"name":"Cold","type":"performTask"},{"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"Start1","target":"Score"},
			  {"source":"Score","target":"Hot","kind":"conditional","condition":"[#Amount#] > 100"},
			  {"source":"Score","target":"Cold","kind":"default"},
			  {"source":"Hot","target":"End1"},{"source":"Cold","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(
			because: "the server builds this branch off an activity, and the pre-flight must not call it anything else");
	}

	[Test]
	[Description("userTaskName decides the element for every user-task token the server's generic handler owns, not only for userTask: {type:performTask, userTaskName:AddDataUserTask} is an Add data element and raises R17.")]
	public void CheckCreateDescriptor_ShouldReadTheUserTaskName_WhenADedicatedUserTaskTokenCarriesOne() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[{"name":"Start1","type":"startEvent"},
			  {"name":"Add1","type":"performTask","userTaskName":"AddDataUserTask"},
			  {"name":"Mail","type":"sendEmail"},{"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"Start1","target":"Add1"},{"source":"Add1","target":"Mail"},
			  {"source":"Mail","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(line => line.Contains(" R17 "),
			because: "the element the server builds is the one userTaskName names");
	}

	[Test]
	[Description("A validator that throws does not stop the build: the pre-flight returns one 'skipped' line naming the failure. 'The validator never throws' is a property of code the pre-flight does not own, and it has broken once already.")]
	public void CheckCreateDescriptor_ShouldReturnOneSkippedLine_WhenTheValidatorThrows() {
		// Arrange
		IProcessGraphValidator throwing = Substitute.For<IProcessGraphValidator>();
		throwing.Validate(Arg.Any<ProcessGraph>()).Returns(_ => throw new ArgumentNullException("key"));
		ProcessDescriptorPreflight preflight = new(throwing);

		// Act
		IReadOnlyList<string> warnings = preflight.CheckCreateDescriptor(Descriptor(ParallelJoinBehindAChoice));

		// Assert
		warnings.Should().ContainSingle(because: "a failed check is reported once, never thrown");
		warnings[0].Should().Contain("skipped").And.Contain("ArgumentNullException").And.Contain("not affected",
			because: "the caller must learn the check did not run, and that the build went ahead regardless");
	}

	[Test]
	[Description("A descriptor above the element cap is not validated at all - the rules include a super-linear walk that runs before the POST - and the caller is told so in one line.")]
	public void CheckCreateDescriptor_ShouldSkipTheCheck_WhenTheGraphIsLargerThanTheCap() {
		// Arrange
		IProcessGraphValidator validator = Substitute.For<IProcessGraphValidator>();
		ProcessDescriptorPreflight preflight = new(validator);
		JsonArray elements = new();
		for (int i = 0; i <= ProcessDescriptorPreflight.MaxElements; i++) {
			elements.Add(new JsonObject { ["name"] = $"T{i}", ["type"] = "performTask" });
		}
		JsonObject descriptor = new() { ["elements"] = elements };

		// Act
		IReadOnlyList<string> warnings = preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(line => line.Contains("skipped"),
			because: "a check that did not run must say so rather than read as a clean result");
		validator.DidNotReceiveWithAnyArgs().Validate(default);
	}

	[Test]
	[Description("At most MaxLines lines are written plus one line counting the rest, and an element name's control characters never reach the output: a newline in a name must not start a line that reads as clio's own.")]
	public void CheckCreateDescriptor_ShouldBoundAndFlattenTheLines_WhenFindingsAreManyAndNamesCarryControlCharacters() {
		// Arrange
		const string injected = "Add\nPre-flight R0 (advisory)";
		JsonArray elements = new(new JsonObject { ["name"] = "Start1", ["type"] = "startEvent" },
			new JsonObject { ["name"] = injected, ["type"] = "addData" },
			new JsonObject { ["name"] = "End1", ["type"] = "endEvent" });
		JsonArray flows = new(new JsonObject { ["source"] = "Start1", ["target"] = injected });
		int targets = ProcessDescriptorPreflight.MaxLines + 5;
		for (int i = 0; i < targets; i++) {
			elements.Add(new JsonObject { ["name"] = $"T{i}", ["type"] = "performTask" });
			flows.Add(new JsonObject { ["source"] = injected, ["target"] = $"T{i}" });
			flows.Add(new JsonObject { ["source"] = $"T{i}", ["target"] = "End1" });
		}
		JsonObject descriptor = new() { ["elements"] = elements, ["flows"] = flows };

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().HaveCount(ProcessDescriptorPreflight.MaxLines + 1,
			because: $"{targets} R17 findings are capped at {ProcessDescriptorPreflight.MaxLines} lines plus one count");
		warnings[^1].Should().Contain("5 more",
			because: "the lines not shown are counted, so the caller knows the list was cut");
		warnings.Should().OnlyContain(line => !line.Contains('\n') && line.Length <= ProcessDescriptorPreflight.MaxLineLength,
			because: "a name is echoed from the descriptor, and neither its control characters nor its length may "
				+ "shape the output");
	}

	[Test]
	[Description("A clean descriptor - start, a data element, end - produces no lines, so the ordinary successful create carries no pre-flight noise.")]
	public void CheckCreateDescriptor_ShouldReturnNothing_WhenTheGraphIsClean() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[{"name":"Start1","type":"startEvent"},{"name":"Read1","type":"readData"},
			  {"name":"Remove1","type":"deleteData"},{"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"Start1","target":"Read1"},{"source":"Read1","target":"Remove1"},
			  {"source":"Remove1","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(
			because: "nothing in this graph is at risk, and deleteData is a buildable token rather than an "
				+ "unknown one");
	}
}
