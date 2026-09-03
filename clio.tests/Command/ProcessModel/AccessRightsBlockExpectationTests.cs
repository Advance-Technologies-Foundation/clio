using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="AccessRightsBlockExpectation"/> — the silent-drop guard for the Change access
/// rights element. A CrtProcessBuilder that predates the element discards the block and still answers
/// success, and the element has no output parameters, so a revoke that never landed would otherwise leave
/// permissions in place with every visible signal reporting success.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class AccessRightsBlockExpectationTests {

	private static DescribedElement Element(string name, string accessRightsJson, string uid = null) {
		DescribedElement element = new() { Name = name, Uid = uid };
		if (accessRightsJson is not null) {
			element.AdditionalData = new Dictionary<string, JsonElement> {
				["accessRights"] = JsonDocument.Parse(accessRightsJson).RootElement.Clone()
			};
		}
		return element;
	}

	private static DescribeProcessResult Described(params DescribedElement[] elements) =>
		new() { Elements = [.. elements] };

	[Test]
	[Description("Collects the element names a build descriptor asks to configure with access rights.")]
	public void FromDescriptor_ShouldCollectElementsCarryingTheBlock() {
		// Arrange
		const string descriptor =
			"{\"elements\":[{\"name\":\"Grant\",\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}},"
			+ "{\"name\":\"Plain\",\"type\":\"performTask\"}]}";

		// Act
		IReadOnlyList<string> expected = AccessRightsBlockExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().Equal(new[] { "Grant" },
			because: "only the element carrying an accessRights block needs its outcome verified");
	}

	[Test]
	[Description("Collects setElement targets but NOT addElement, whose accessRights the server ignores by design.")]
	public void FromOperations_ShouldCollectSetElementOnly() {
		// Arrange
		const string operations =
			"[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}},"
			+ "{\"op\":\"addElement\",\"element\":{\"name\":\"New\",\"type\":\"changeAccessRights\","
			+ "\"accessRights\":{\"object\":\"Order\"}}}]";

		// Act
		IReadOnlyList<string> expected = AccessRightsBlockExpectation.FromOperations(operations);

		// Assert
		expected.Should().Equal(new[] { "Grant" },
			because: "the server's addElement applies only the email and performer blocks, so reporting an "
				+ "ignored-by-design accessRights block as a silent drop would be a false alarm");
	}

	[Test]
	[Description("Names an element once when several operations configure it, so the warning's subject and plural stay correct.")]
	public void FromOperations_ShouldNameAnElementOnce_WhenSeveralOperationsConfigureIt() {
		// Arrange - the multi-step flow the tool descriptions prescribe: set the object, then the entries.
		const string operations =
			"[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"object\":\"Order\"}}},"
			+ "{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]";

		// Act
		IReadOnlyList<string> expected = AccessRightsBlockExpectation.FromOperations(operations);

		// Assert
		expected.Should().Equal(new[] { "Grant" },
			because: "a warning that renders \"the elements 'Grant', 'Grant'\" cannot count its own subjects, "
				+ "and this is the only machine-readable signal a caller gets for a grant or revoke");
	}

	[Test]
	[Description("Reports an element whose read-back carries no accessRights block as dropped.")]
	public void Missing_ShouldReportAnElementWithoutTheBlock() {
		// Act
		IReadOnlyList<string> missing = AccessRightsBlockExpectation.Missing(
			Described(Element("Grant", null)), ["Grant"]);

		// Assert
		missing.Should().Equal(new[] { "Grant" },
			because: "a server that supports the element reports the block, so its absence is the drop signal");
	}

	[Test]
	[Description("Stays silent when the block IS reported, including a collection that read back empty.")]
	public void Missing_ShouldStaySilent_WhenTheBlockIsReportedEvenWithEmptyCollections() {
		// Act
		IReadOnlyList<string> missing = AccessRightsBlockExpectation.Missing(
			Described(Element("Grant", "{\"object\":\"Order\",\"add\":[],\"remove\":[]}")), ["Grant"]);

		// Assert
		missing.Should().BeEmpty(
			because: "describe reports a stored-but-undecodable collection as an empty array, so keying on "
				+ "entry counts would accuse the server about elements that are configured perfectly well");
	}

	[Test]
	[Description("Matches an element the caller identified by UId, in both directions.")]
	public void Missing_ShouldMatchByUid_WhenTheCallerIdentifiedTheElementThatWay() {
		// Arrange
		const string uid = "b1a7f0c2-3d4e-4f5a-8b6c-7d8e9f0a1b2c";

		// Act
		IReadOnlyList<string> reported = AccessRightsBlockExpectation.Missing(
			Described(Element("Grant", "{\"object\":\"Order\"}", uid)), [uid]);
		IReadOnlyList<string> dropped = AccessRightsBlockExpectation.Missing(
			Described(Element("Grant", null, uid)), [uid]);

		// Assert
		reported.Should().BeEmpty(
			because: "setElement identifies an element by name OR UId, so a caller who passed a UId must not be "
				+ "told its configuration was discarded when the edit applied cleanly");
		dropped.Should().Equal(new[] { uid },
			because: "the UId arm must detect a real drop too, not merely avoid false alarms");
	}

	[Test]
	[Description("Detects the block whatever casing the server used for the extension-bag key.")]
	public void Missing_ShouldStaySilent_WhenTheServerSpellsTheKeyDifferently() {
		// Arrange
		DescribedElement element = new() {
			Name = "Grant",
			AdditionalData = new Dictionary<string, JsonElement> {
				["AccessRights"] = JsonDocument.Parse("{\"object\":\"Order\"}").RootElement.Clone()
			}
		};

		// Act
		IReadOnlyList<string> missing = AccessRightsBlockExpectation.Missing(Described(element), ["Grant"]);

		// Assert
		missing.Should().BeEmpty(
			because: "[JsonExtensionData] stores the server's exact property name and the bag comparer is "
				+ "ordinal, so an exact-match lookup would turn a casing difference into a permanent false "
				+ "alarm telling callers their permissions were discarded when they were not");
	}

	[Test]
	[Description("Stays silent when the element is absent from the read-back — absence is not evidence.")]
	public void Missing_ShouldStaySilent_WhenTheElementIsNotInTheReadBack() {
		// Act
		IReadOnlyList<string> missing = AccessRightsBlockExpectation.Missing(
			Described(Element("Other", "{\"object\":\"Order\"}")), ["Grant"]);

		// Assert
		missing.Should().BeEmpty(
			because: "the read-back is the only evidence this check has, so an identifier it cannot resolve "
				+ "is a reason to stay quiet rather than to accuse the server");
	}

	[Test]
	[Description("Reports an element saved with NO record filter: the runtime never enters its filter block, so the query runs unfiltered and the element acts on EVERY record of the object - the widest configuration it can be in, and one nothing refuses.")]
	public void WithoutRecordFilter_ShouldReportAnElementWithNoFilter() {
		// Arrange
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");

		// Act
		IReadOnlyList<string> unfiltered = AccessRightsBlockExpectation.WithoutRecordFilter(
			Described(element), ["Grant"]);

		// Assert
		unfiltered.Should().Equal(new[] { "Grant" },
			because: "the record filter decides WHICH records the element acts on, so without one the run "
				+ "changes no permissions and the element has no output parameter to say so");
	}

	[Test]
	[Description("Reports a filter that narrows nothing. It is NOT the same state as an absent filter - a conditionless filter takes the runtime's 'filters empty' exit and changes nothing, while an ABSENT one acts on every record - but both need reporting, so both appear here and the WARNING is what distinguishes them.")]
	public void WithoutRecordFilter_ShouldAlsoReportAConditionlessFilter() {
		// Arrange
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");
		element.Filter = new DescribedFilter { Object = "Order" };

		// Act
		IReadOnlyList<string> unfiltered = AccessRightsBlockExpectation.WithoutRecordFilter(
			Described(element), ["Grant"]);

		// Assert
		unfiltered.Should().Equal(new[] { "Grant" },
			because: "a filter object carrying neither conditions nor groups narrows nothing, so reporting it "
				+ "as present would let the widest possible configuration pass as configured");
	}

	[Test]
	[Description("Stays silent when the element carries a real filter condition.")]
	public void WithoutRecordFilter_ShouldStaySilent_WhenAConditionIsPresent() {
		// Arrange
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");
		element.Filter = new DescribedFilter {
			Object = "Order",
			Conditions = [new DescribedFilterCondition { Column = "Id" }]
		};

		// Act
		IReadOnlyList<string> unfiltered = AccessRightsBlockExpectation.WithoutRecordFilter(
			Described(element), ["Grant"]);

		// Assert
		unfiltered.Should().BeEmpty(
			because: "a configured element must not be warned about, or the warning stops being read");
	}

	[Test]
	[Description("Does not accuse an element the read-back never returned: Unresolved reports that case, so reporting it here too would warn twice about one element.")]
	public void WithoutRecordFilter_ShouldIgnoreAnElementAbsentFromTheReadBack() {
		// Act
		IReadOnlyList<string> unfiltered = AccessRightsBlockExpectation.WithoutRecordFilter(
			Described(Element("SomethingElse", "{\"object\":\"Order\"}")), ["Grant"]);

		// Assert
		unfiltered.Should().BeEmpty(
			because: "an element that is not in the read-back is reported by Unresolved, and one finding per "
				+ "problem is what keeps the warnings actionable");
	}

	[Test]
	[Description("An element with NO filter is reported as matching no records.")]
	public void BuildNoFilterWarning_ShouldSayNoRecords_WhenTheFilterIsAbsent() {
		// Arrange
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");

		// Act
		string warning = AccessRightsBlockExpectation.BuildNoFilterWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().Contain("'Grant'", because: "the caller needs to know which element is affected");
		warning.Should().Contain("NO record filter at all",
			because: "naming the state distinguishes it from the conditionless one, which behaves oppositely");
		warning.Should().Contain("EVERY record of the target object",
			because: "an ABSENT filter never enters the runtime's filter block, so the query runs UNFILTERED. "
				+ "This warning previously said the element would match NO records - telling a caller that the "
				+ "widest configuration the feature can produce is harmless");
		warning.Should().Contain("setFilter",
			because: "the warning must carry the operation that fixes it");
	}

	[Test]
	[Description("Only clearFilter is collected. It carries no accessRights block, so every other check skips it - yet it is what moves an element from narrowing to acting on EVERY record, and the guard used to return before reading anything back. setFilter is deliberately NOT collected: it always supplies an object, so it can only leave the element narrowing, or conditionless which the package refuses at build - checking it would put an extra whole-schema describe on the most common modify shape to look for a state that cannot occur.")]
	public void FilterTouched_ShouldNameOnlyElementsWhoseFilterWasCleared() {
		// Arrange
		const string operations = """
			[ { "op": "clearFilter", "elementName": "GrantRights" },
			  { "op": "setFilter", "elementName": "Other", "filter": { "object": "Order" } },
			  { "op": "setElement", "elementName": "Unrelated", "elementUpdate": { "caption": "x" } } ]
			""";

		// Act
		IReadOnlyList<string> touched = AccessRightsBlockExpectation.FilterTouched(operations);

		// Assert
		touched.Should().BeEquivalentTo(["GrantRights"],
			because: "clearFilter is the only operation that can leave an element with NO record filter, which is "
				+ "the state that acts on every record. The setFilter supplied an object so its element still "
				+ "narrows, and the setElement carrying no accessRights block is not this check's business");
	}

	[Test]
	[Description("FilterTouched is empty for a batch that touches no filter, so the guard keeps its early return and an ordinary edit pays no extra describe.")]
	public void FilterTouched_ShouldBeEmpty_WhenNoFilterOperationIsPresent() {
		// Act
		IReadOnlyList<string> touched = AccessRightsBlockExpectation.FilterTouched(
			"""[ { "op": "setElement", "elementName": "GrantRights", "elementUpdate": { "caption": "x" } } ]""");

		// Assert
		touched.Should().BeEmpty(because: "no filter operation means nothing extra to read back");
	}

	[Test]
	[Description("A filter whose only content is an EMPTY nested group narrows nothing, so it classifies as conditionless like a bare one. Counting Groups was enough to call it narrowing, which let the shape escape the guard entirely.")]
	public void BuildNoFilterWarning_ShouldTreatANestedEmptyGroup_AsConditionless() {
		// Arrange — groups:[{conditions:[]}]: non-empty Groups, but nothing that narrows.
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");
		element.Filter = new DescribedFilter {
			Object = "Order",
			Groups = [new DescribedFilterGroup { Conditions = [] }]
		};

		// Act
		string warning = AccessRightsBlockExpectation.BuildNoFilterWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().NotBeNull(
			because: "an empty sub-group narrows nothing, so this element is in the conditionless state and must "
				+ "be reported - a Groups.Count check alone silently classified it as narrowing");
		warning.Should().Contain("changes nothing",
			because: "it is the no-op state, reported with the conditionless wording");
	}

	[Test]
	[Description("An element whose filter carries no conditions is reported as INERT - the runtime takes its 'filters empty' exit. The opposite blast radius from an absent filter, and the reason the two states cannot share one wording.")]
	public void BuildNoFilterWarning_ShouldSayItChangesNothing_WhenTheFilterHasNoConditions() {
		// Arrange
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");
		element.Filter = new DescribedFilter { Object = "Order" };

		// Act
		string warning = AccessRightsBlockExpectation.BuildNoFilterWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().Contain("changes nothing",
			because: "a filter that IS present but conditionless hits the runtime's \"filters empty\" exit; "
				+ "this state is the no-op, not the wide one");
		warning.Should().NotContain("EVERY record of the target object",
			because: "that is the ABSENT filter's consequence, and the two must never share a wording - they "
				+ "were swapped in the shipped text, which is how the inversion survived four surfaces");
	}

	[Test]
	[Description("The warning names the elements and tells the caller not to treat a revoke as applied.")]
	public void BuildWarning_ShouldNameTheElementsAndTheRevokeConsequence() {
		// Act
		string warning = AccessRightsBlockExpectation.BuildWarning(["Grant"]);

		// Assert
		warning.Should().Contain("'Grant'", because: "the caller needs to know which element was affected");
		warning.Should().Contain("REVOKE",
			because: "an unapplied revoke leaves permissions in place, which is the dangerous direction");
		warning.Should().Contain("install-process-builder",
			because: "the warning must carry the one action that fixes it");
	}

	[Test]
	[Description("Returns null when nothing was dropped, so callers can treat null as 'no warning'.")]
	public void BuildWarning_ShouldReturnNull_WhenNothingWasDropped() {
		// Arrange
		IReadOnlyList<string> nothingDropped = [];

		// Act
		string warning = AccessRightsBlockExpectation.BuildWarning(nothingDropped);

		// Assert
		warning.Should().BeNull(
			because: "null is the caller's 'no warning to emit' contract; an empty string would print a blank "
				+ "warning line on every successful operation");
	}

	[Test]
	[Description("An unparseable payload skips verification instead of masking the real failure.")]
	public void FromDescriptor_ShouldReturnEmpty_WhenPayloadIsNotJson() {
		// Arrange
		const string notJson = "not json";

		// Act
		IReadOnlyList<string> fromDescriptor = AccessRightsBlockExpectation.FromDescriptor(notJson);
		IReadOnlyList<string> fromOperations = AccessRightsBlockExpectation.FromOperations(notJson);

		// Assert
		fromDescriptor.Should().BeEmpty(
			because: "an unparseable descriptor is the command's problem to report through its normal error "
				+ "path, so this check skips rather than fabricating a drop warning on top of it");
		fromOperations.Should().BeEmpty(
			because: "the operations path must degrade the same way, for the same reason");
	}
}
