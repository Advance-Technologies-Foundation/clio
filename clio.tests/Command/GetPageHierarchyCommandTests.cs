using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
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

	// ---- Major 1 (AC3): default response size budget -------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("BuildResponse auto-omits bodies and flags bodiesOmittedForSize when the selected window exceeds the default size budget (AC3), while still reporting metadata and body length.")]
	public void BuildResponse_Should_Omit_Bodies_When_Window_Exceeds_Size_Budget() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage" };
		string hugeBody = new('x', GetPageHierarchyCommand.DefaultBodySizeBudgetChars + 1);
		List<PageDesignerHierarchySchema> chain = new() { Schema("UsrLeaf_FormPage", hugeBody) };

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.BodiesOmittedForSize.Should().BeTrue(
			because: "the summed body length of the window is over the response budget");
		response.Warning.Should().Contain("metadata-only",
			because: "the caller is told how to re-request the bodies within the budget");
		response.Schemas.Should().OnlyContain(s => s.Body == null,
			because: "bodies are omitted for the whole page once the budget is blown");
		response.Schemas.Single().HasBody.Should().BeTrue(
			because: "body presence is still reported even when the body is omitted for size");
		response.Schemas.Single().BodyLength.Should().Be(hugeBody.Length,
			because: "body length is reported so the caller can page deliberately");
	}

	[Test]
	[Category("Unit")]
	[Description("BuildResponse keeps bodies and leaves bodiesOmittedForSize/warning clear when the window fits the budget.")]
	public void BuildResponse_Should_Keep_Bodies_Within_Size_Budget() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage" };
		List<PageDesignerHierarchySchema> chain = EffectiveFirstChain();

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.BodiesOmittedForSize.Should().BeFalse(because: "the small chain is well under the budget");
		response.Warning.Should().BeNull(because: "no size warning is emitted when bodies are included");
		response.Schemas.Should().OnlyContain(s => s.Body != null, because: "bodies are inlined within budget");
	}

	[Test]
	[Category("Unit")]
	[Description("Paging under the budget avoids the omission: an over-budget full chain returns its bodies when a small window is requested.")]
	public void BuildResponse_Should_Not_Omit_When_Paged_Window_Fits_Budget() {
		// Arrange — each body is just over half the budget, so the full 2-chain blows it but a single-entry page fits.
		string halfPlus = new('x', (GetPageHierarchyCommand.DefaultBodySizeBudgetChars / 2) + 10);
		List<PageDesignerHierarchySchema> chain = new() {
			Schema("UsrLeaf_FormPage", halfPlus),
			Schema("RootBase_FormPage", halfPlus)
		};

		// Act
		GetPageHierarchyResponse full = GetPageHierarchyCommand.BuildResponse(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" }, chain);
		GetPageHierarchyResponse paged = GetPageHierarchyCommand.BuildResponse(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage", Limit = 1 }, chain);

		// Assert
		full.BodiesOmittedForSize.Should().BeTrue(because: "the two bodies together exceed the budget");
		paged.BodiesOmittedForSize.Should().BeFalse(because: "a single-entry window is under the budget");
		paged.Schemas.Single().Body.Should().NotBeNull(because: "the in-budget page keeps its body");
	}

	// ---- Minor 4: paging edge cases ------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("BuildResponse clamps an over-range offset to total and returns an empty page with hasMore=false.")]
	public void BuildResponse_Should_Handle_Offset_Beyond_Total() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "UsrLeaf_FormPage", Offset = 50 };
		List<PageDesignerHierarchySchema> chain = EffectiveFirstChain();

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.TotalCount.Should().Be(3, because: "totalCount is the full chain size");
		response.Offset.Should().Be(3, because: "an over-range offset is clamped to total");
		response.ReturnedCount.Should().Be(0, because: "no entries remain past the end of the chain");
		response.HasMore.Should().BeFalse(because: "there is nothing beyond a clamped-to-end page");
	}

	[Test]
	[Category("Unit")]
	[Description("BuildResponse over a single-level chain returns one root entry with hasMore=false.")]
	public void BuildResponse_Should_Handle_Single_Level_Chain() {
		// Arrange
		GetPageHierarchyOptions options = new() { SchemaName = "Root_FormPage" };
		List<PageDesignerHierarchySchema> chain = new() { Schema("Root_FormPage", "root-body") };

		// Act
		GetPageHierarchyResponse response = GetPageHierarchyCommand.BuildResponse(options, chain);

		// Assert
		response.TotalCount.Should().Be(1, because: "a single-level chain has one schema");
		response.ReturnedCount.Should().Be(1, because: "the one entry is returned");
		response.HasMore.Should().BeFalse(because: "there is nothing beyond the only entry");
		response.RootSchemaName.Should().Be("Root_FormPage",
			because: "the sole schema is both root and leaf");
		response.Schemas.Single().HierarchyLevel.Should().Be(0, because: "the root is level 0");
	}

	// ---- Major 2: TryGetHierarchy error contract -----------------------------------------------

	private static GetPageHierarchyCommand BuildCommand(
		IApplicationClient applicationClient = null,
		IPageDesignerHierarchyClient hierarchyClient = null) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		return new GetPageHierarchyCommand(
			applicationClient ?? Substitute.For<IApplicationClient>(),
			urlBuilder,
			hierarchyClient ?? Substitute.For<IPageDesignerHierarchyClient>(),
			Substitute.For<ILogger>());
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy rejects a missing schema-name before any I/O with the exact error.")]
	public void TryGetHierarchy_Should_Fail_When_SchemaName_Missing() {
		// Arrange
		GetPageHierarchyCommand sut = BuildCommand();

		// Act
		bool ok = sut.TryGetHierarchy(new GetPageHierarchyOptions { SchemaName = "  " }, out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "a blank schema-name is a validation failure");
		response.Success.Should().BeFalse();
		response.Error.Should().Be("schema-name is required", because: "the guard reports the exact contract error");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy rejects a negative offset with the exact error.")]
	public void TryGetHierarchy_Should_Fail_When_Offset_Negative() {
		// Arrange
		GetPageHierarchyCommand sut = BuildCommand();

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage", Offset = -1 },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "a negative offset is invalid");
		response.Error.Should().Be("offset must be zero or greater");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy rejects a negative limit with the exact error.")]
	public void TryGetHierarchy_Should_Fail_When_Limit_Negative() {
		// Arrange
		GetPageHierarchyCommand sut = BuildCommand();

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage", Limit = -5 },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "a negative limit is invalid");
		response.Error.Should().Be("limit must be zero or greater");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy reports the empty-hierarchy branch when schema metadata cannot be resolved.")]
	public void TryGetHierarchy_Should_Fail_When_Hierarchy_Empty() {
		// Arrange — metadata query returns success with no rows, so the schema UId cannot be resolved.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[]}");
		GetPageHierarchyCommand sut = BuildCommand(applicationClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "Missing_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "an unresolvable schema yields no chain");
		response.Error.Should().Contain("hierarchy is empty or could not be resolved",
			because: "the empty-hierarchy branch reports why the chain is missing");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy catches an exception from the hierarchy client and surfaces its message.")]
	public void TryGetHierarchy_Should_Catch_Client_Exception() {
		// Arrange — metadata resolves, then GetParentSchemas throws (not wrapped) → the catch branch.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[{\"UId\":\"schema-uid\",\"PackageUId\":\"pkg-uid\"}]}");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException("designer service unavailable"));
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "an unhandled client error fails the resolution");
		response.Error.Should().Be("designer service unavailable",
			because: "the catch branch surfaces the client exception message");
	}
}
