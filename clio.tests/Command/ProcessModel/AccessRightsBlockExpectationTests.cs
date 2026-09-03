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

	// A user-task element as the read-back returns one: the accessRights block is optional (a CrtProcessBuilder
	// predating the element omits it) but userTaskName is not - it is emitted for every user task regardless.
	private static DescribedElement UserTask(string name, string userTaskName, string accessRightsJson = null) {
		DescribedElement element = Element(name, accessRightsJson);
		element.UserTaskName = userTaskName;
		return element;
	}

	[Test]
	[Description("A re-filtered Change access rights element whose read-back reports NO accessRights block is warned about. This is the only check that speaks for it: it sent no block, so Missing() and the lossy-read check both skip it by design, and the environments that cannot report the block are exactly the ones running a CrtProcessBuilder that predates the element.")]
	public void BuildUnreportableFilterWarning_ShouldFire_WhenTheBlockCannotBeReported() {
		// Arrange
		DescribedElement element = UserTask("Grant", "ChangeAdminRightsUserTask");

		// Act
		string warning = AccessRightsBlockExpectation.BuildUnreportableFilterWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().NotBeNull(
			because: "the batch changed this element's record filter and nothing else here can check the result - "
				+ "silence would be indistinguishable from a verified success on a live permission change");
		warning.Should().Contain("EVERY record of its object",
			because: "the consequence the caller has to act on is the widening one, and it must be stated in the "
				+ "same direction as every other surface");
	}

	[Test]
	[Description("A clearFilter is equally legal on readData/changeData/signalStart, and those elements hold no access-rights state at all. Warning about THEIR access-rights state is a false accusation, and the kind that teaches a caller to ignore the message on the one element type that can actually widen.")]
	public void BuildUnreportableFilterWarning_ShouldStaySilent_ForAnElementThatIsNotChangeAccessRights() {
		// Arrange
		DescribedElement element = UserTask("ReadOrders", "ReadDataUserTask");

		// Act
		string warning = AccessRightsBlockExpectation.BuildUnreportableFilterWarning(
			Described(element), ["ReadOrders"]);

		// Assert
		warning.Should().BeNull(
			because: "a readData element has no access-rights state to be unreportable, so the absence of a block "
				+ "on it is the normal case rather than a gap in verification");
	}

	[Test]
	[Description("When the environment DOES report the block there is nothing unverified, so the warning must not fire - the other checks own that element from there on, and a second message about the same healthy write would be noise.")]
	public void BuildUnreportableFilterWarning_ShouldStaySilent_WhenTheBlockIsReported() {
		// Arrange
		DescribedElement element = UserTask("Grant", "ChangeAdminRightsUserTask", "{\"object\":\"Order\"}");

		// Act
		string warning = AccessRightsBlockExpectation.BuildUnreportableFilterWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().BeNull(
			because: "the read-back reported the block, so the filter state is checkable and BuildNoFilterWarning "
				+ "is the check that speaks for it");
	}

	[Test]
	[Description("FilterTouched names clearFilter targets only. A setFilter always carried an object and its conditions, so it can only leave the element narrowing - including it would put a whole-schema describe on the retarget-then-refilter batch the tool description itself prescribes, to check a state that cannot occur.")]
	public void FilterTouched_ShouldNameClearFilterTargetsOnly() {
		// Arrange
		const string operations = """
			[ { "op": "clearFilter", "elementName": "Grant" },
			  { "op": "setFilter", "elementName": "Other", "filter": { "object": "Order" } } ]
			""";

		// Act
		IReadOnlyList<string> touched = AccessRightsBlockExpectation.FilterTouched(operations);

		// Assert
		touched.Should().BeEquivalentTo(["Grant"],
			because: "clearFilter is the operation that can widen an element to every record; setFilter cannot, "
				+ "and the tool description promises a read-back for exactly the first of those");
	}

	[Test]
	[Description("The lossy-read warning fires on the server's addUnreadable/removeUnreadable counts. A supplied collection REPLACES the stored one, so building a replacement from a read that omitted entries deletes permissions nobody can see - this is the only signal that the read was incomplete.")]
	public void BuildLossyReadWarning_ShouldFire_WhenTheServerReportsUnreportedEntries() {
		// Arrange — 2 entries dropped from add, collection itself undecodable for remove.
		DescribedElement element = Element("Grant",
			"{\"object\":\"Order\",\"addUnreadable\":2,\"removeUnreadable\":-1}");

		// Act
		string warning = AccessRightsBlockExpectation.BuildLossyReadWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().NotBeNull(
			because: "a non-zero count means the read-back omitted stored entries, and a replacement built from "
				+ "it would delete them");
		warning.Should().Contain("'Grant'", because: "the caller needs to know which element is affected");
		warning.Should().Contain("REPLACES",
			because: "the danger is not the incomplete read itself but feeding it back as a collection");
	}

	[Test]
	[Description("No lossy-read warning when both counts are zero, and none when the server predates the field and omits it entirely - an older environment must behave exactly as before rather than warning on every write.")]
	public void BuildLossyReadWarning_ShouldStaySilent_WhenNothingWasDropped() {
		// Arrange
		DescribedElement complete = Element("Complete",
			"{\"object\":\"Order\",\"addUnreadable\":0,\"removeUnreadable\":0}");
		DescribedElement olderServer = Element("Older", "{\"object\":\"Order\"}");

		// Act + Assert
		AccessRightsBlockExpectation.BuildLossyReadWarning(Described(complete), ["Complete"])
			.Should().BeNull(because: "zero means the collections reported in full");
		AccessRightsBlockExpectation.BuildLossyReadWarning(Described(olderServer), ["Older"])
			.Should().BeNull(
				because: "a server predating the counts omits the fields, which must read as 0 - warning there "
					+ "would fire on every write against an older environment");
	}

	[Test]
	[Description("A filter that describe could NOT decode is reported as undecodable, not as absent: describe returns no filter block for the legacy FilterEdit format, and every shipped designer-built specimen uses it. Calling that 'no filter' would tell the caller a live element is inert and invite a setFilter that overwrites a working filter.")]
	public void BuildNoFilterWarning_ShouldReportUndecodable_WhenTheParameterStillCarriesAValue() {
		// Arrange — no decoded Filter, but DataSourceFilters holds a stored value.
		DescribedElement element = Element("Grant", "{\"object\":\"Order\"}");
		element.Parameters = [new DescribedParameter {
			Name = "DataSourceFilters", Source = "ConstValue", Value = "a legacy FilterEdit payload"
		}];

		// Act
		string warning = AccessRightsBlockExpectation.BuildNoFilterWarning(Described(element), ["Grant"]);

		// Assert
		warning.Should().Contain("could not decode",
			because: "the stored parameter proves a filter EXISTS, so absence must not be claimed");
		warning.Should().NotContain("EVERY record of the target object",
			because: "that is the ABSENT filter's consequence; an undecodable one may be narrowing correctly");
		warning.Should().NotContain("setFilter",
			because: "prescribing setFilter here would overwrite a working legacy filter on a live permission "
				+ "change - the failure this guard exists to prevent, pointed the other way");
	}

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
			because: "the record filter decides WHICH records the element acts on, so without one the runtime "
				+ "never enters its filter branch and the run applies the change to EVERY record of the object, "
				+ "with no output parameter to say so");
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
			because: "a filter object carrying neither conditions nor groups narrows nothing, so the runtime takes "
				+ "its \"filters empty\" exit and the element is INERT - reporting it as present would let a "
				+ "grant or revoke that can never happen pass as configured");
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
	[Description("An element with NO record filter is reported as acting on EVERY record of its object - the runtime gates on a non-empty filter, so an absent one never enters that branch and the query runs unfiltered, with record permissions disabled. The OPPOSITE state from a present-but-conditionless filter, and the direction the shipped text originally had backwards.")]
	public void BuildNoFilterWarning_ShouldSayEveryRecord_WhenTheFilterIsAbsent() {
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
