using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using FluentAssertions;
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
	[Description("Nothing the server reports is repeated: an implicit split (R12, a build notice), a plain flow off a deciding gateway (R7, normalised with a notice), an omitted condition (R13, refused by FlowKindRules), an inclusive gateway (UNBUILDABLE, refused as unsupported) and every ERROR (here R1 and R15) produce no pre-flight line.")]
	public void CheckCreateDescriptor_ShouldStaySilent_WhenEveryFindingIsOneTheServerReports() {
		// Arrange
		JsonObject descriptor = Descriptor("""
			{"elements":[
			  {"name":"Start1","type":"startEvent"},{"name":"Split","type":"performTask"},
			  {"name":"A","type":"performTask"},{"name":"B","type":"performTask"},
			  {"name":"Decide","type":"exclusiveGateway"},{"name":"Or1","type":"inclusiveGateway"},
			  {"name":"End1","type":"endEvent"}],
			 "flows":[
			  {"source":"Start1","target":"Split"},{"source":"Start1","target":"End1"},
			  {"source":"Split","target":"A"},{"source":"Split","target":"B"},
			  {"source":"A","target":"Decide"},
			  {"source":"Decide","target":"Or1","kind":"conditional"},
			  {"source":"Decide","target":"End1"},
			  {"source":"B","target":"Nowhere"},
			  {"source":"Or1","target":"End1"}]}
			""");

		// Act
		IReadOnlyList<string> warnings = _preflight.CheckCreateDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(
			because: "each of these is refused or noticed by the server itself, and a pre-flight line in front of "
				+ "the server's own message would be the same problem said twice");
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

	[Test]
	[Description("A descriptor the pre-flight cannot read the way the server does produces no lines at all: an unknown flow kind, a condition that is not a string, results that are not strings, flows that are not an array, an element that is not an object, or no elements. Validating a re-interpreted graph would answer about a process the caller did not describe, and the server refuses each of these with its own message.")]
	[TestCase("""{"elements":[{"name":"S","type":"startEvent"},{"name":"E","type":"endEvent"}],"flows":[{"source":"S","target":"E","kind":"conditionnal"}]}""")]
	[TestCase("""{"elements":[{"name":"S","type":"startEvent"},{"name":"E","type":"endEvent"}],"flows":[{"source":"S","target":"E","kind":"conditional","condition":42}]}""")]
	[TestCase("""{"elements":[{"name":"S","type":"startEvent"},{"name":"E","type":"endEvent"}],"flows":[{"source":"S","target":"E","kind":"conditional","results":[1]}]}""")]
	[TestCase("""{"elements":[{"name":"S","type":"startEvent"},{"name":"E","type":"endEvent"}],"flows":{"source":"S"}}""")]
	[TestCase("""{"elements":["startEvent"],"flows":[]}""")]
	[TestCase("""{"elements":[{"name":7,"type":"startEvent"}]}""")]
	[TestCase("""{"elements":[]}""")]
	[TestCase("""{"name":"UsrNoGraph"}""")]
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
