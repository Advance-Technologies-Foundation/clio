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
		const string body = """{ "viewConfigDiff": [], "viewModelConfigDiff": [], "modelConfigDiff": [] }""";

		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue();
	}

	[Test]
	[Description("A flat field insert with no parent applies into the root and is valid.")]
	public void Validate_FlatFieldInsert_IsValid() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "UsrName", "values": { "type": "crt.Input", "label": "$Resources.Strings.UsrName_caption" } }
			] }
			""";

		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue();
	}

	[Test]
	[Description("A viewModelConfigDiff root merge (path: []) applies through the path applier and is valid.")]
	public void Validate_ViewModelConfigDiffRootMerge_IsValid() {
		const string body = """
			{ "viewModelConfigDiff": [
				{ "operation": "merge", "path": [], "values": { "attributes": { "UsrName": { "modelConfig": { "path": "PDS.UsrName" } } } } }
			] }
			""";

		MobileDiffApplyValidator.Validate(body).IsValid.Should().BeTrue();
	}

	[Test]
	[Description("Malformed JSON is not the oracle's concern (ValidateMobileBody reports it) — the oracle returns valid without throwing.")]
	public void Validate_MalformedJson_IsValidNoThrow() {
		MobileDiffApplyValidator.Validate("{ not json").IsValid.Should().BeTrue();
	}
}
