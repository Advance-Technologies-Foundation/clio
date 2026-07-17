using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class GetPageHierarchyCommandTests {

	private static PageDesignerHierarchySchema Schema(string name, string body, int schemaType = 9, int version = 1) =>
		new() {
			UId = name + "-uid",
			Name = name,
			PackageName = name + "Pkg",
			PackageUId = name + "-pkg-uid",
			SchemaVersion = version,
			SchemaType = schemaType,
			Body = body
		};

	// effectiveFirst = designer-service order: [0] effective/leaf, ascending to the root.
	private static List<PageDesignerHierarchySchema> EffectiveFirstChain() =>
		new() {
			Schema("UsrLeaf_FormPage", "leaf-body"),
			Schema("MidBase_FormPage", "mid-body"),
			Schema("RootBase_FormPage", "root-body")
		};

	[Test]
	[Category("Unit")]
	[Description("BuildResponse orders the chain root-first (by hierarchy level) matching the deterministic merge, with body-bearing entries and correct totals.")]
	public void BuildResponse_Should_Order_Root_First_With_Bodies() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage" };
		List<PageDesignerHierarchySchema> chain = EffectiveFirstChain();

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.Success.Should().BeTrue(because: "a non-empty chain resolves successfully");
		response.TotalCount.Should().Be(3, because: "the whole chain has three schemas");
		response.ReturnedCount.Should().Be(3, because: "no paging window was requested");
		response.HasMore.Should().BeFalse(because: "the whole chain fits in one page");
		response.RootSchemaName.Should().Be("RootBase_FormPage",
			because: "the root (base) schema is the last element of the effective-first chain");
		response.Schemas.Select(s => s.SchemaName).Should().ContainInOrder(
			new[] { "RootBase_FormPage", "MidBase_FormPage", "UsrLeaf_FormPage" },
			because: "entries must be ordered root-first (by hierarchy level), the order the merge consumes");
		response.Schemas.Select(s => s.HierarchyLevel).Should().ContainInOrder(
			new[] { 0, 1, 2 },
			because: "hierarchy level ascends from the root");
		response.Schemas[2].Body.Should().Be("leaf-body",
			because: "each entry carries its own raw body by default");
		response.Schemas.Should().OnlyContain(s => s.SchemaType == "web",
			because: "schema type 9 maps to the web label");
	}

	[Test]
	[Category("Unit")]
	[Description("BuildResponse honors offset/limit paging over the ordered chain and reports hasMore.")]
	public void BuildResponse_Should_Page_With_Offset_And_Limit() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage", Offset = 1, Limit = 1 };
		List<PageDesignerHierarchySchema> chain = EffectiveFirstChain();

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.TotalCount.Should().Be(3, because: "totalCount reports the full chain regardless of paging");
		response.Offset.Should().Be(1, because: "the requested offset is applied");
		response.ReturnedCount.Should().Be(1, because: "limit=1 returns a single entry");
		response.Schemas.Single().SchemaName.Should().Be("MidBase_FormPage",
			because: "offset 1 in root-first order is the middle schema");
		response.Schemas.Single().HierarchyLevel.Should().Be(1,
			because: "the reported level is the absolute chain level, not the page index");
		response.HasMore.Should().BeTrue(because: "the leaf entry still remains beyond this page");
	}

	[Test]
	[Category("Unit")]
	[Description("BuildResponse with metadata-only omits raw bodies while still reporting body length and presence.")]
	public void BuildResponse_Should_Omit_Bodies_When_MetadataOnly() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage", MetadataOnly = true };
		List<PageDesignerHierarchySchema> chain = EffectiveFirstChain();

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.Schemas.Should().OnlyContain(s => s.Body == null,
			because: "metadata-only must not carry raw bodies");
		response.Schemas.Should().OnlyContain(s => s.HasBody && s.BodyLength > 0,
			because: "body presence and length are still reported for each schema");
	}

	[Test]
	[Category("Unit")]
	[Description("BuildResponse marks a body-less schema as hasBody=false and never emits a body for it.")]
	public void BuildResponse_Should_Flag_Body_Less_Schema() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage" };
		List<PageDesignerHierarchySchema> chain = new() {
			Schema("UsrLeaf_FormPage", "leaf-body"),
			Schema("Compiled_FormPage", null)
		};

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		PageHierarchySchemaEntry root = response.Schemas.Single(s => s.SchemaName == "Compiled_FormPage");
		root.HasBody.Should().BeFalse(because: "a null body means the schema is compiled or empty");
		root.Body.Should().BeNull(because: "no body is emitted for a body-less schema");
		root.BodyLength.Should().Be(0, because: "a body-less schema has zero length");
	}
}
