using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Tests for <see cref="MobileDiffApplyValidator"/> — the applier-based validation oracle that replaces the
/// heuristic auto-repair on the mobile validate path. It applies the body's diff sections through the faithful
/// client-engine clones (<see cref="JsonDiffApplier"/> / <see cref="JsonPathDiffApplier"/>) and surfaces any
/// differ exception (notably "Item \"X\" is not a container for other items").
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobileDiffApplyValidatorTests {

	[Test]
	[Description("A child insert targeting a parent slot (itemLayout) the in-diff parent does not declare reproduces the Creatio differ's not-a-container error.")]
	public void Validate_InsertIntoUndeclaredParentSlot_ReportsNotAContainer() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "ProductsList", "parentName": "ProductsListContainer", "propertyName": "items",
					"values": { "type": "crt.List", "items": "$ProductsList" } },
				{ "operation": "insert", "name": "ProductsList_ListItem", "parentName": "ProductsList", "propertyName": "itemLayout",
					"values": { "type": "crt.ListItem", "title": "$PDS_Name" } }
			] }
			""";

		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body);

		result.IsValid.Should().BeFalse(
			because: "the parent ProductsList does not declare an 'itemLayout' slot, so the differ rejects the child insert");
		result.Errors.Should().ContainSingle(e =>
			e.Contains("ProductsList") && e.Contains("is not a container for other items"),
			because: "the surfaced message must be the server-faithful differ exception");
	}

	[Test]
	[Description("When the in-diff parent declares the target slot (itemLayout: {}), the child insert applies cleanly.")]
	public void Validate_InsertIntoDeclaredParentSlot_IsValid() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "ProductsList", "parentName": "ProductsListContainer", "propertyName": "items",
					"values": { "type": "crt.List", "items": "$ProductsList", "itemLayout": {} } },
				{ "operation": "insert", "name": "ProductsList_ListItem", "parentName": "ProductsList", "propertyName": "itemLayout",
					"values": { "type": "crt.ListItem", "title": "$PDS_Name" } }
			] }
			""";

		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body);

		result.IsValid.Should().BeTrue(
			because: "the parent declares an empty 'itemLayout' object, so the differ can place the child there");
	}

	[Test]
	[Description("A NEWLY-INSERTED container (no 'items' in its own values) that a later insert targets via parentName/items reproduces the exact production error from the mobile page converter bug: 'Item \"X\" is not a container for other items'. This is the container-level sibling of Validate_InsertIntoUndeclaredParentSlot_ReportsNotAContainer above (that one used 'itemLayout' one level down the tree).")]
	public void Validate_ContainerInsertMissingItemsSlot_ReportsNotAContainer() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "SalesTab", "parentName": "Tabs", "propertyName": "items",
					"values": { "type": "crt.TabContainer", "caption": "#ResourceString(SalesTab_caption)#" } },
				{ "operation": "insert", "name": "ProductsExpansionPanel", "parentName": "SalesTab", "propertyName": "items",
					"values": { "type": "crt.ExpansionPanel" } }
			] }
			""";

		// Act
		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "SalesTab's own values carry no 'items' array, so the differ has nowhere to place ProductsExpansionPanel");
		result.Errors.Should().ContainSingle(e =>
			e.Contains("SalesTab") && e.Contains("is not a container for other items"),
			because: "this is the literal error the mobile page converter's elementMap used to produce before its container inserts declared an items slot");
	}

	[Test]
	[Description("The fix's contract: once the container insert declares an empty 'items' array, the identical child insert applies cleanly — this is exactly what WebToMobileAnalysisService.InitializeContainerItemSlots now guarantees for every converter-created container.")]
	public void Validate_ContainerInsertWithItemsSlot_IsValid() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "SalesTab", "parentName": "Tabs", "propertyName": "items",
					"values": { "type": "crt.TabContainer", "caption": "#ResourceString(SalesTab_caption)#", "items": [] } },
				{ "operation": "insert", "name": "ProductsExpansionPanel", "parentName": "SalesTab", "propertyName": "items",
					"values": { "type": "crt.ExpansionPanel" } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue(
			because: "SalesTab now declares an empty 'items' array, so the differ can place ProductsExpansionPanel into it");
	}

	[Test]
	[Description("An operation whose parentName equals its name reproduces the differ's cyclic-dependency error.")]
	public void Validate_LoopDependency_ReportsCyclicDependency() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "Loop", "parentName": "Loop", "propertyName": "items", "values": { "type": "crt.List" } }
			] }
			""";

		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Contains("Cyclic dependency") && e.Contains("Loop"));
	}

	[Test]
	[Description("Empty diff sections apply as a no-op and are valid.")]
	public void Validate_EmptyDiffs_IsValid() {
		// Arrange
		const string body = """{ "viewConfigDiff": [], "viewModelConfigDiff": [], "modelConfigDiff": [] }""";

		// Act / Assert
		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue(
			because: "empty diff sections apply as a no-op");
	}

	[Test]
	[Description("A flat field insert with no parent applies into the root and is valid.")]
	public void Validate_FlatFieldInsert_IsValid() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "UsrName", "values": { "type": "crt.Input", "label": "$Resources.Strings.UsrName_caption" } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue(
			because: "a root-level merge applies cleanly against the empty base");
	}

	[Test]
	[Description("A viewModelConfigDiff root merge (path: []) applies through the path applier and is valid.")]
	public void Validate_ViewModelConfigDiffRootMerge_IsValid() {
		// Arrange
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "merge", "path": [], "values": { "attributes": { "UsrName": { "modelConfig": { "path": "PDS.UsrName" } } } } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue(
			because: "a path:[] root merge applies through the path applier");
	}

	[Test]
	[Description("Malformed JSON is not the oracle's concern (ValidateMobileBody reports it) — the oracle returns valid without throwing.")]
	public void Validate_MalformedJson_IsValidNoThrow() {
		// Act / Assert
		MobileDiffApplyValidator.Validate("{ not json").IsValid.Should().BeTrue(
			because: "the structural validators own the malformed-JSON case; the oracle must not throw on it");
	}

	[Test]
	[Description("A viewModelConfigDiff insert that appends to an array the mobile template owns (absent from the body) validates cleanly when NO template base is supplied: the oracle seeds an empty container at the insert path so a template-owned-array append does not false-positive as not-a-container (the validate-page / seeded fallback).")]
	public void Validate_PathDiffInsertIntoTemplateArray_NoBase_IsValid() {
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"],
					"values": { "name": "QuickFilter_x_Items", "loadOnChange": true } }
			] }
			""";

		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue(
			because: "with no base the oracle seeds an empty container at the insert path so the append resolves");
	}

	[Test]
	[Description("The same insert validates against the supplied mobile template base that owns the array — the faithful path update-page uses (it resolves the page's merged config).")]
	public void Validate_PathDiffInsertIntoTemplateArray_WithBase_IsValid() {
		// Arrange
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"],
					"values": { "name": "QuickFilter_x_Items", "loadOnChange": true } }
			] }
			""";
		const string templateViewModelConfig = """
			{ "attributes": { "Items": { "modelConfig": { "filterAttributes": [ { "name": "QuickFilterGroup_Filters" } ] } } } }
			""";

		// Act / Assert
		MobileDiffApplyValidator.Validate(body, templateViewModelConfig).IsValid.Should().BeTrue(
			because: "the supplied base already owns the array, so the append resolves against it");
	}

	[Test]
	[Description("A genuine self-consistency error still surfaces even with the seeded base: a merge sets the attribute to a scalar, then an insert targets a sub-path of it — the differ reports not-a-container.")]
	public void Validate_PathDiffInsertIntoScalar_ReportsNotAContainer() {
		// Arrange: a merge sets attributes.Items to a scalar, then an insert targets a sub-path of it.
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "merge", "path": ["attributes"], "values": { "Items": "scalar" } },
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"], "values": { "name": "x" } }
			] }
			""";

		// Act
		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "inserting into a sub-path of a scalar is a genuine self-consistency error the differ rejects");
		result.Errors.Should().ContainSingle(e => e.Contains("is not a container for other items"),
			because: "the seeded base does not mask a real not-a-container error introduced by the diff itself");
	}

	[Test]
	[Description("Two inserts appending to the SAME template-owned array both validate against the seeded base — the seed reuses the one shared empty array rather than overwriting it on the second insert.")]
	public void Validate_TwoInsertsIntoSameArray_NoBase_IsValid() {
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"], "values": { "name": "A" } },
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"], "values": { "name": "B" } }
			] }
			""";

		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue(
			because: "the seed reuses one shared empty array, so both appends target the same container");
	}

	[Test]
	[Description("The lazy base resolver is NOT invoked for a viewConfigDiff-only body (no path diff needs the base), so validation of such a body spends no get-page read.")]
	public void Validate_ViewConfigDiffOnly_DoesNotInvokeBaseResolver() {
		// Arrange: a body with only a viewConfigDiff — no viewModelConfigDiff / modelConfigDiff.
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "UsrName", "values": { "type": "crt.Input" } }
			] }
			""";
		int resolverCalls = 0;

		// Act
		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body, () => {
			resolverCalls++;
			return (null, null);
		});

		// Assert
		result.IsValid.Should().BeTrue(because: "the viewConfigDiff applies cleanly");
		resolverCalls.Should().Be(0,
			because: "no path diff carries a base need, so the (potentially I/O-bound) resolver must not run");
	}

	[Test]
	[Description("The lazy base resolver is invoked at most ONCE even when both viewModelConfigDiff and modelConfigDiff need a base — the resolution is memoized and shared across sections.")]
	public void Validate_BothPathDiffs_InvokesBaseResolverAtMostOnce() {
		// Arrange: both path diffs insert into a template-owned array, so both need the base.
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"], "values": { "name": "A" } } ],
			  "modelConfigDiff": [
				{ "operation": "insert", "path": ["dataSources","PDS","config","filterAttributes"], "values": { "name": "B" } } ] }
			""";
		int resolverCalls = 0;

		// Act
		SchemaValidationResult result = MobileDiffApplyValidator.Validate(body, () => {
			resolverCalls++;
			return ((string)null, (string)null);
		});

		// Assert
		result.IsValid.Should().BeTrue(because: "both inserts resolve against the seeded base");
		resolverCalls.Should().Be(1,
			because: "the resolver result is memoized and shared, so it runs once rather than per section");
	}

	[Test]
	[Description("NeedsResolvedBase is true when a non-empty viewModelConfigDiff carries no own viewModelConfig base — the case that would otherwise trigger a get-page read inside the sync lock.")]
	public void NeedsResolvedBase_PathDiffWithoutOwnBase_IsTrue() {
		// Arrange
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "insert", "path": ["attributes","Items","modelConfig","filterAttributes"], "values": { "name": "x" } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.NeedsResolvedBase(body).Should().BeTrue(
			because: "a non-empty path diff with no inline base must be resolved against the template base");
	}

	[Test]
	[Description("NeedsResolvedBase is true for a non-empty modelConfigDiff with no own modelConfig base.")]
	public void NeedsResolvedBase_ModelConfigDiffWithoutOwnBase_IsTrue() {
		// Arrange
		const string body = """
			{ "modelConfigDiff": [
				{ "operation": "insert", "path": ["dataSources","PDS","config","filterAttributes"], "values": { "name": "x" } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.NeedsResolvedBase(body).Should().BeTrue(
			because: "modelConfigDiff follows the same rule as viewModelConfigDiff");
	}

	[Test]
	[Description("NeedsResolvedBase is false when the body inlines its own base object — the diff resolves against that, so no get-page read is needed.")]
	public void NeedsResolvedBase_PathDiffWithInlineOwnBase_IsFalse() {
		// Arrange
		const string body = """
			{ "viewModelConfig": { "attributes": {} },
			  "viewModelConfigDiff": [
				{ "operation": "insert", "path": ["attributes","Items"], "values": { "name": "x" } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.NeedsResolvedBase(body).Should().BeFalse(
			because: "an inline own base is used directly, so no external base resolution is required");
	}

	[Test]
	[Description("NeedsResolvedBase is false for a viewConfigDiff-only body — there is no path diff that needs a base, so pre-resolution must skip it.")]
	public void NeedsResolvedBase_ViewConfigDiffOnly_IsFalse() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "Items", "values": { "type": "crt.List" } }
			] }
			""";

		// Act / Assert
		MobileDiffApplyValidator.NeedsResolvedBase(body).Should().BeFalse(
			because: "a viewConfigDiff-only body never resolves a path-diff base, so it must not spend a get-page read");
	}

	[Test]
	[Description("NeedsResolvedBase is false for an empty or unparseable body — nothing to resolve.")]
	public void NeedsResolvedBase_EmptyOrMalformed_IsFalse() {
		// Act / Assert
		MobileDiffApplyValidator.NeedsResolvedBase("").Should().BeFalse(because: "an empty body needs no base");
		MobileDiffApplyValidator.NeedsResolvedBase("{ not json").Should().BeFalse(because: "an unparseable body is handled by the structural validators, not here");
	}
}
