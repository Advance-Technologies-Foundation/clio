using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessDescriptorKeyValidator"/> over the REAL shipped schema - the keys it reports
/// and the hint for each (ENG-95244, test plan TC-U-03..TC-U-08, TC-U-12).
/// </summary>
/// <remarks>
/// The measured cases come from the 2026-09-24 stand probe (CrtProcessBuilder 1.6.6.22): every control landed,
/// every typo and wrong-case key was dropped in silence. The controls must pass this check and the others
/// must not, or the check refuses what works or passes what vanishes.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessDescriptorKeyValidatorTests {

	private readonly IProcessDescriptorKeyValidator _validator = new ProcessDescriptorKeyValidator();

	private IReadOnlyList<UnknownDescriptorKey> Create(string json) =>
		_validator.FindUnknownKeys(JsonNode.Parse(json), ProcessWritePayload.CreateDescriptor).Listed;

	private IReadOnlyList<UnknownDescriptorKey> Modify(string json) =>
		_validator.FindUnknownKeys(JsonNode.Parse(json), ProcessWritePayload.ModifyOperations).Listed;

	[Test]
	[Description("The measured CONTROLS are accepted: flows[].label, elements[].readData.sort and parameters on create, label on a setFlow operation. They landed on the stand, so refusing them would be a false refusal of a working call.")]
	public void FindUnknownKeys_ShouldAcceptTheMeasuredControls_WhenEveryKeyIsDeclared() {
		// Arrange
		const string descriptor = """
			{"name":"UsrP","caption":"P","packageName":"Custom",
			 "elements":[{"name":"S","type":"startEvent"},
			   {"name":"R","type":"readData","readData":{"source":"Account","sort":[{"column":"Name","direction":"desc"}]}},
			   {"name":"E","type":"endEvent"}],
			 "flows":[{"source":"S","target":"R","label":"Go"},{"source":"R","target":"E"}],
			 "parameters":[{"name":"Amount","type":"Integer"}]}
			""";
		const string operations = """[{"op":"setFlow","source":"S","target":"R","kind":"sequence","label":"Go"}]""";

		// Act
		IReadOnlyList<UnknownDescriptorKey> create = Create(descriptor);
		IReadOnlyList<UnknownDescriptorKey> modify = Modify(operations);

		// Assert
		create.Should().BeEmpty(because: "every key in the descriptor is a declared contract member");
		modify.Should().BeEmpty(because: "every key in the operation is a declared contract member");
	}

	[Test]
	[Description("The measured TYPOS are reported with their JSON path and the key they were meant to be: lable -> label on a flow, sortt -> sort inside a readData block, parametres -> parameters at the root. The setFlow case has its own test, on the operations root.")]
	[TestCase("""{"flows":[{"source":"S","target":"E","lable":"Go"}]}""", "flows[0].lable", "'label'")]
	[TestCase("""{"elements":[{"name":"S"},{"name":"R","readData":{"sortt":[]}}]}""", "elements[1].readData.sortt", "'sort'")]
	[TestCase("""{"parametres":[]}""", "parametres", "'parameters'")]
	public void FindUnknownKeys_ShouldNameThePathAndTheIntendedKey_WhenACreateKeyIsMisspelled(string json,
			string expectedPath, string expectedHint) {
		// Arrange
		string descriptor = json;

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Create(descriptor);

		// Assert
		unknown.Should().ContainSingle(because: "the descriptor carries exactly one key the server would drop");
		unknown[0].Path.Should().Be(expectedPath, because: "the caller has to find the key to fix it");
		unknown[0].Hint.Should().Contain(expectedHint, because: "a one-edit typo names the key it was meant to be");
	}

	[Test]
	[Description("The measured WRONG-CASE keys are reported, and the hint names the exact key and says keys are case-sensitive - the mistake a reader least suspects, because most JSON tooling ignores case and the server does not.")]
	[TestCase("""{"flows":[{"source":"S","target":"E","Label":"Go"}]}""", "flows[0].Label", "'label'")]
	[TestCase("""{"elements":[{"name":"R","readData":{"Sort":[]}}]}""", "elements[0].readData.Sort", "'sort'")]
	public void FindUnknownKeys_ShouldSayKeysAreCaseSensitive_WhenAKeyDiffersOnlyInCase(string json,
			string expectedPath, string expectedKey) {
		// Arrange
		string descriptor = json;

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Create(descriptor);

		// Assert
		unknown.Should().ContainSingle(because: "a wrong-case key is dropped exactly as a typo is");
		unknown[0].Path.Should().Be(expectedPath, because: "the path is spelled the way the caller wrote it");
		unknown[0].Hint.Should().Contain(expectedKey).And.Contain("case-sensitive",
			because: "the fix is the casing, and the reader has to be told that casing matters here");
	}

	[Test]
	[Description("A key nested inside a modify operation's blocks is reported with the full path - on setFlow itself, inside elementUpdate.email, and inside a filter condition - so the walk reaches every level an operation can carry.")]
	public void FindUnknownKeys_ShouldReportNestedOperationKeys_WithTheFullPath() {
		// Arrange
		const string operations = """
			[{"op":"setFlow","source":"S","target":"E","lable":"Go"},
			 {"op":"setElement","elementName":"Mail","elementUpdate":{"email":{"subjct":"Hi"}}},
			 {"op":"setFilter","elementName":"Start1","filter":{"object":"Account","conditions":[{"colum":"Name"}]}}]
			""";

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Modify(operations);

		// Assert
		unknown.Select(key => key.Path).Should().Equal(
			["operations[0].lable", "operations[1].elementUpdate.email.subjct", "operations[2].filter.conditions[0].colum"],
			because: "each of these is dropped by the server, at any depth, and the path is what the caller fixes");
	}

	[Test]
	[Description("A key with no near match gets the valid keys at that level instead of a guess, bounded to that one contract's keys.")]
	public void FindUnknownKeys_ShouldListTheValidKeys_WhenNoKeyIsNear() {
		// Arrange
		const string descriptor = """{"flows":[{"source":"S","target":"E","priority":1}]}""";

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Create(descriptor);

		// Assert
		unknown.Should().ContainSingle(because: "'priority' is not a flow key - branch precedence is array order");
		unknown[0].Hint.Should().Be("valid keys here: condition, kind, label, results, source, target",
			because: "with nothing near, the caller needs the full set to pick from, and only that level's set");
	}

	[Test]
	[Description("Values are not descended into when they are not objects: a JSON null for a contract member, a string where a block is declared, and the entries of a string array produce no key finding - those are value problems the server refuses loudly, or no problem at all.")]
	public void FindUnknownKeys_ShouldIgnoreValuesThatAreNotObjects_WhenAContractMemberHoldsOne() {
		// Arrange
		const string descriptor = """
			{"elements":[{"name":"R","readData":null,"filter":"not an object","signal":{"changedColumns":["Lable"]}}],
			 "flows":[{"source":"S","target":"E","kind":"conditional","results":["Positive"]}]}
			""";

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Create(descriptor);

		// Assert
		unknown.Should().BeEmpty(
			because: "only KEYS are checked; a null, a wrongly typed value and string-array entries are values");
	}

	[Test]
	[Description("A FilterDescriptor accepts the keys of its base FilterGroupDescriptor - the one inheritance in the contracts - so a filter's conditions and groups are not refused.")]
	public void FindUnknownKeys_ShouldAcceptInheritedFilterKeys_WhenAFilterCarriesThem() {
		// Arrange
		const string descriptor = """
			{"elements":[{"name":"S","type":"signalStart","filter":{"object":"Account","logicalOperation":"and",
			  "conditions":[{"column":"Name","comparison":"equal","value":"A"}],
			  "groups":[{"logicalOperation":"or","conditions":[{"column":"Code","comparison":"equal","value":"B"}]}]}}]}
			""";

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Create(descriptor);

		// Assert
		unknown.Should().BeEmpty(because: "logicalOperation, conditions and groups are declared on the base class");
	}

	[Test]
	[Description("The measured modify typo - setFlow carrying lable, which answered '1 operation(s) applied' and changed nothing - is reported on the operations root with the hint 'label'.")]
	public void FindUnknownKeys_ShouldNameTheIntendedKey_WhenAnOperationKeyIsMisspelled() {
		// Arrange
		const string operations = """[{"op":"setFlow","source":"S","target":"E","kind":"sequence","lable":"Go"}]""";

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown = Modify(operations);

		// Assert
		unknown.Should().ContainSingle(because: "the operation carries one key the server would drop");
		unknown[0].Path.Should().Be("operations[0].lable", because: "the path is rooted at the operations array");
		unknown[0].Hint.Should().Contain("'label'", because: "a one-edit typo names the key it was meant to be");
	}

	[Test]
	[Description("A representative descriptor - signal start with a changed-columns trigger and a record filter with groups, readData with sort and a filter, changeData and addData values, a sendEmail block with recipients, an approval block, openEditPage with completion, a formula task, a multi-instance sub-process, parameters with a default and a mapping, and flows of all three kinds with a label and results - is accepted in full (TC-U-04). A false refusal of a working call shows here first.")]
	public void FindUnknownKeys_ShouldAcceptARepresentativeDescriptor_WhenEveryBlockUsesDeclaredKeys() {
		// Arrange
		const string descriptor = """
			{"name":"UsrRep","caption":"Rep","packageName":"Custom",
			 "parameters":[{"name":"Amount","type":"Integer","direction":"In","caption":"Amount","value":"5"}],
			 "elements":[
			  {"name":"Start1","type":"signalStart","signal":{"entity":"Account","on":"modified","changedColumns":["Name"]},
			   "filter":{"object":"Account","logicalOperation":"and","conditions":[{"column":"Name","comparison":"equal","value":"A"}],
			     "groups":[{"logicalOperation":"or","conditions":[{"column":"Code","comparison":"equal","processParameter":"Amount"}]}]}},
			  {"name":"Read1","type":"readData","readData":{"source":"Contact","mode":"first","columns":["Name"],
			   "sort":[{"column":"Name","direction":"desc"}]},"filter":{"object":"Contact","conditions":[]}},
			  {"name":"Change1","type":"changeData","changeData":{"source":"Contact","values":[{"column":"Name","value":"X"}]}},
			  {"name":"Add1","type":"addData","addData":{"source":"Contact","mode":"one","values":[{"column":"Name","value":"Y"}]}},
			  {"name":"Mail","type":"sendEmail","email":{"mode":"auto","sender":"a@b.c","to":[{"value":"x@y.z"}],
			   "subject":"Hi","body":"<p>Hi</p>","bodyFormat":"html","importance":"normal","ignoreErrors":true}},
			  {"name":"Approve","type":"approval","approval":{"object":"Contact","purpose":"Check",
			   "approver":{"type":"manager"},"allowDelegation":true}},
			  {"name":"Page","type":"openEditPage","openEditPage":{"page":"ContactPageV2","editMode":"add",
			   "completion":{"mode":"onConditions"}},"filter":{"object":"Contact","conditions":[]}},
			  {"name":"Calc","type":"formulaTask","formula":{"body":"1 + 1","target":{"resultProcessParameter":"Amount"}}},
			  {"name":"Call","type":"subProcess","subProcess":{"processName":"UsrOther",
			   "multiInstanceOptions":{"enabled":true,"executionMode":"sequential","ignoreErrors":false}}},
			  {"name":"Decide","type":"exclusiveGateway"},
			  {"name":"End1","type":"endEvent"}],
			 "flows":[{"source":"Start1","target":"Read1","label":"Go"},
			  {"source":"Read1","target":"Decide"},
			  {"source":"Decide","target":"Change1","kind":"conditional","condition":"[#Amount#] > 1"},
			  {"source":"Decide","target":"Add1","kind":"default"},
			  {"source":"Approve","target":"End1","kind":"conditional","results":["Positive"]}],
			 "mappings":[{"elementName":"Mail","elementParameter":"Subject","processParameter":"Amount"}]}
			""";

		// Act
		DescriptorKeyReport report = _validator.FindUnknownKeys(JsonNode.Parse(descriptor),
			ProcessWritePayload.CreateDescriptor);

		// Assert
		report.Listed.Select(key => key.Path).Should().BeEmpty(
			because: "every key in this descriptor is a declared member of the contract at its level");
	}

	[Test]
	[Description("The cost of a hostile payload is bounded: a single enormous key gets no near-match search, and of thousands of unknown keys only MaxListedKeys carry a hint while all of them are counted. The hint is an edit distance against every valid key at that level, and the payload is the caller's.")]
	public void FindUnknownKeys_ShouldBoundTheWork_WhenThePayloadCarriesHugeOrManyUnknownKeys() {
		// Arrange
		JsonObject element = new() { ["name"] = "E1" };
		element[new string('x', 100_000)] = 1;
		for (int i = 0; i < 5_000; i++) {
			element[$"k{i}"] = i;
		}
		JsonObject descriptor = new() { ["elements"] = new JsonArray(element) };

		// Act
		DescriptorKeyReport report = _validator.FindUnknownKeys(descriptor, ProcessWritePayload.CreateDescriptor);

		// Assert
		report.Total.Should().Be(5_001, because: "every unknown key is counted");
		report.Listed.Should().HaveCount(ProcessDescriptorKeyValidator.MaxListedKeys,
			because: "only the first keys are listed with a hint");
		report.Listed[0].Path.Length.Should().BeLessOrEqualTo(ProcessDescriptorKeyValidator.MaxPathLength,
			because: "a path echoed into a message is cut, whatever the caller sent");
		report.Listed[0].Hint.Should().StartWith("valid keys here:",
			because: "a key that long is no typo of a member, so it is not searched for one");
	}

	[Test]
	[Description("A key carrying a newline or an escape character is reported ENCODED, so it cannot forge a line of clio's own refusal or push a terminal escape sequence into the log.")]
	public void FindUnknownKeys_ShouldEncodeControlCharacters_WhenAKeyCarriesThem() {
		// Arrange
		JsonObject flow = new() { ["source"] = "S", ["target"] = "E" };
		flow["x" + '\n' + "- flows[0].label: ok" + '\u001b' + "[2J"] = 1;
		JsonObject descriptor = new() { ["flows"] = new JsonArray(flow) };

		// Act
		IReadOnlyList<UnknownDescriptorKey> unknown =
			_validator.FindUnknownKeys(descriptor, ProcessWritePayload.CreateDescriptor).Listed;

		// Assert
		unknown.Should().ContainSingle(because: "the key is unknown");
		unknown[0].Path.Should().NotContain("\n").And.NotContain("\u001b",
			because: "raw control characters in the path would reach the caller's output unescaped");
		unknown[0].Path.Should().Contain(@"\n", because: "the newline is written the way JSON writes it");
	}
}
