using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json;
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
		IPageDesignerHierarchyClient hierarchyClient = null,
		ILogger logger = null) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		return new GetPageHierarchyCommand(
			applicationClient ?? Substitute.For<IApplicationClient>(),
			urlBuilder,
			hierarchyClient ?? Substitute.For<IPageDesignerHierarchyClient>(),
			logger ?? Substitute.For<ILogger>());
	}

	/// <summary>SysSchema metadata answer that resolves the requested schema to a UId and its own package.</summary>
	private const string ResolvedMetadataJson =
		"{\"success\":true,\"rows\":[{\"UId\":\"schema-uid\",\"PackageUId\":\"pkg-uid\"}]}";

	private static IApplicationClient ApplicationClientAnswering(string responseBody) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(responseBody);
		return applicationClient;
	}

	/// <summary>
	/// Hierarchy client whose design-package endpoint fails with <paramref name="designPackageFailure"/> while the
	/// designer endpoint keeps answering with a chain — the endpoint-specific failure that must not be papered over
	/// with the schema's runtime package.
	/// </summary>
	private static IPageDesignerHierarchyClient HierarchyClientWithFailingDesignPackage(Exception designPackageFailure) {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns(_ => throw designPackageFailure);
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => EffectiveFirstChain());
		return hierarchyClient;
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
	[Description("TryGetHierarchy reports the absent schema as not found (the environment answered) when the metadata query returns no rows.")]
	public void TryGetHierarchy_ShouldReportSchemaNotFound_WhenMetadataQueryReturnsNoRows() {
		// Arrange — the environment ANSWERED success:true with no rows: the schema is genuinely absent.
		IApplicationClient applicationClient = ApplicationClientAnswering("{\"success\":true,\"rows\":[]}");
		GetPageHierarchyCommand sut = BuildCommand(applicationClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "Missing_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "an absent schema has no chain to return");
		response.Success.Should().BeFalse(because: "the envelope reports the failure alongside the exit code");
		response.Error.Should().Be("Schema 'Missing_FormPage' not found",
			because: "an answered lookup with no rows is a statement about the schema, and the caller is told which schema");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy surfaces the classified transport/auth message verbatim when the metadata lookup never produced an answer, instead of claiming the chain is empty.")]
	public void TryGetHierarchy_ShouldSurfaceTransportError_WhenMetadataLookupDoesNotAnswer() {
		// Arrange — an expired session answers the SelectQuery with an HTML login page: no answer about the schema.
		IApplicationClient applicationClient = ApplicationClientAnswering("<html><body>Sign in</body></html>");
		GetPageHierarchyCommand sut = BuildCommand(applicationClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "a lookup that never answered cannot resolve a chain");
		response.Error.Should().Contain("HTML page instead of JSON",
			because: "the classified message from the metadata helper must reach the caller verbatim");
		response.Error.Should().Contain("reg-web-app",
			because: "the caller is told the actionable recovery for an expired session");
		response.Error.Should().NotContain("hierarchy is empty",
			because: "a request that produced no answer must not be reported as a statement about the chain");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy still reports the empty-hierarchy branch when the metadata resolves but the designer service returns no schemas.")]
	public void TryGetHierarchy_ShouldReportEmptyHierarchy_WhenDesignerReturnsNoSchemas() {
		// Arrange — metadata resolves and the designer answers, with an empty chain.
		IApplicationClient applicationClient = ApplicationClientAnswering(ResolvedMetadataJson);
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => new List<PageDesignerHierarchySchema>());
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "an empty chain is nothing to return");
		response.Error.Should().Contain("hierarchy is empty or could not be resolved",
			because: "an answered designer call with no schemas keeps the empty-hierarchy contract");
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy fails instead of anchoring on the runtime package when the design-package lookup never answered (transport failure on that endpoint alone).")]
	public void TryGetHierarchy_ShouldFail_WhenDesignPackageLookupTransportFails() {
		// Arrange — the design-package endpoint is unreachable while the designer endpoint still answers.
		IApplicationClient applicationClient = ApplicationClientAnswering(ResolvedMetadataJson);
		IPageDesignerHierarchyClient hierarchyClient =
			HierarchyClientWithFailingDesignPackage(new HttpRequestException("connection reset by peer"));
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(
			because: "a design-package lookup that never answered must not be substituted with the runtime package and reported as an answer");
		response.Success.Should().BeFalse(because: "the envelope reports the failure the exit code carries");
		response.Error.Should().Contain("connection reset by peer",
			because: "the caller is told the transport failure that stopped the read");
		response.Schemas.Should().BeNull(
			because: "a failed read returns no chain, so nothing can be mistaken for the answer");
		hierarchyClient.DidNotReceive().GetParentSchemas(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy fails when the design-package endpoint answers with a non-JSON body (expired session), rather than guessing the anchor package.")]
	public void TryGetHierarchy_ShouldFail_WhenDesignPackageLookupReturnsNonJson() {
		// Arrange — the design-package endpoint answers with an HTML login page, which the client surfaces as a parse failure.
		IApplicationClient applicationClient = ApplicationClientAnswering(ResolvedMetadataJson);
		IPageDesignerHierarchyClient hierarchyClient = HierarchyClientWithFailingDesignPackage(
			new JsonReaderException("Unexpected character encountered while parsing value: <. Path '', line 0, position 0."));
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(
			because: "a body that is not JSON says nothing about the design package, so it cannot license the fallback");
		response.Error.Should().Contain("Unexpected character",
			because: "the parse failure is surfaced rather than hidden behind a substituted package");
		hierarchyClient.DidNotReceive().GetParentSchemas(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("TryGetHierarchy keeps the design-package fallback to the schema's own package when the service ANSWERED and rejected the lookup, and records the degradation.")]
	public void TryGetHierarchy_ShouldFallBackToOwnPackage_WhenDesignPackageServiceRejectsTheSchema() {
		// Arrange — the service answered success:false, which the client reports as a rejection, not a missing answer.
		IApplicationClient applicationClient = ApplicationClientAnswering(ResolvedMetadataJson);
		ILogger logger = Substitute.For<ILogger>();
		IPageDesignerHierarchyClient hierarchyClient = HierarchyClientWithFailingDesignPackage(
			new InvalidOperationException("Failed to resolve design package"));
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient, logger);

		// Act
		bool ok = sut.TryGetHierarchy(
			new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" },
			out GetPageHierarchyResponse response);

		// Assert
		ok.Should().BeTrue(
			because: "an answered rejection means the schema has no design package, and its own package is the correct anchor");
		response.Success.Should().BeTrue(because: "the chain resolved from the fallback anchor");
		hierarchyClient.Received().GetParentSchemas("schema-uid", "pkg-uid");
		logger.Received().WriteDebug(Arg.Is<string>(message => message.Contains("GetDesignPackageUId")));
	}

	// ---- Story 13 (ENG-95262): the CLI exit code and the envelope must agree --------------------

	[Test]
	[Category("Unit")]
	[Description("Execute exits non-zero and prints a success:false envelope when the design-package lookup never answered.")]
	public void Execute_ShouldExitNonZero_WhenDesignPackageLookupTransportFails() {
		// Arrange
		IApplicationClient applicationClient = ApplicationClientAnswering(ResolvedMetadataJson);
		ILogger logger = Substitute.For<ILogger>();
		IPageDesignerHierarchyClient hierarchyClient =
			HierarchyClientWithFailingDesignPackage(new HttpRequestException("connection reset by peer"));
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient, logger);

		// Act
		int exitCode = sut.Execute(new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" });

		// Assert
		exitCode.Should().Be(1,
			because: "a script that checks the exit code must be able to tell a failed read from a page it can use");
		logger.Received(1).WriteInfo(Arg.Is<string>(payload => payload.Contains("\"success\":false")));
	}

	[Test]
	[Description("Execute exits non-zero when the schema is absent, and still prints the structured envelope (AC-01).")]
	[Category("Unit")]
	public void Execute_ShouldExitNonZero_WhenSchemaIsAbsent() {
		// Arrange
		IApplicationClient applicationClient = ApplicationClientAnswering("{\"success\":true,\"rows\":[]}");
		ILogger logger = Substitute.For<ILogger>();
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, logger: logger);

		// Act
		int exitCode = sut.Execute(new GetPageHierarchyOptions { SchemaName = "Missing_FormPage" });

		// Assert
		exitCode.Should().Be(1, because: "a missing page is a failed read, not a successful call that returned nothing");
		logger.Received(1).WriteInfo(Arg.Is<string>(payload =>
			payload.Contains("\"success\":false") && payload.Contains("not found")));
	}

	[Test]
	[Category("Unit")]
	[Description("Execute exits zero and prints the resolved chain when the read succeeds (AC-02).")]
	public void Execute_ShouldExitZero_WhenHierarchyResolves() {
		// Arrange
		IApplicationClient applicationClient = ApplicationClientAnswering(ResolvedMetadataJson);
		ILogger logger = Substitute.For<ILogger>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("schema-uid").Returns("design-pkg-uid");
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), "design-pkg-uid").Returns(_ => EffectiveFirstChain());
		GetPageHierarchyCommand sut = BuildCommand(applicationClient, hierarchyClient, logger);

		// Act
		int exitCode = sut.Execute(new GetPageHierarchyOptions { SchemaName = "UsrLeaf_FormPage" });

		// Assert
		exitCode.Should().Be(0, because: "a resolved chain is a successful read");
		logger.Received(1).WriteInfo(Arg.Is<string>(payload => payload.Contains("\"success\":true")));
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
