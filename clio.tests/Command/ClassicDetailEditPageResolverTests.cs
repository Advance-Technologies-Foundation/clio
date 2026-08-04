using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="ClassicDetailEditPageResolver"/> — the <c>SysModuleEdit</c> child-page lookup that
/// replaced scanning detail bodies for a <c>getEditPageName</c> token (ENG-94401), whose measured yield on the
/// product is zero.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ClassicDetailEditPageResolverTests {

	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
	private const string ContractEntityUId = "11111111-1111-1111-1111-111111111111";
	private const string ActivityEntityUId = "22222222-2222-2222-2222-222222222222";
	private const string ContractPageUId = "33333333-3333-3333-3333-333333333333";
	private const string MiniPageUId = "44444444-4444-4444-4444-444444444444";
	private const string EmailPageUId = "55555555-5555-5555-5555-555555555555";

	// A distinct, readable UId per index, so a test can generate a value set wider than one query chunk.
	private static string Uid(int index) => $"{index:D8}-0000-0000-0000-000000000000";

	// Sizes of every In-filter list the query carries: an In value costs one query parameter, so this is what has to
	// stay bounded no matter how wide the page is.
	private static IEnumerable<int> InFilterSizes(JObject query) =>
		query.Descendants().OfType<JObject>()
			.Where(node => node["filterType"]?.Value<int>() == 4)
			.Select(node => ((JArray)node["rightExpressions"]).Count);

	private static string Rows(params string[] rows) =>
		"{\"rows\":[" + string.Join(",", rows) + "],\"success\":true}";

	// Captured payloads of every SelectQuery the resolver issued, so a test can assert HOW it queried (batching,
	// filters) and not only what it returned.
	private readonly List<JObject> _queries = new();

	[SetUp]
	public void Setup() => _queries.Clear();

	// Routes each SelectQuery to a canned response by root schema and column shape, so one substitute serves the
	// resolver's three batched steps (entity names -> UIds, SysModuleEdit rows, page UIds -> names).
	private IApplicationClient ClientReturning(string entityResponse, string editResponse, string schemaResponse) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => {
			JObject query = JObject.Parse(ci.ArgAt<string>(1));
			_queries.Add(query);
			if (query["rootSchemaName"]?.ToString() == "SysModuleEdit") {
				return editResponse;
			}
			// Both SysSchema calls share a root; the entity lookup is the one selecting ExtendParent.
			return query["columns"]?["items"]?["ExtendParent"] != null ? entityResponse : schemaResponse;
		});
		return client;
	}

	// The third step: SysSchema keyed by UId (the entity step is the SysSchema query selecting ExtendParent).
	private static bool IsPageNameLookup(JObject query) =>
		query["rootSchemaName"]?.ToString() == "SysSchema" && query["columns"]?["items"]?["ExtendParent"] == null;

	private static ClassicDetailEditPageResolver Create(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns("http://localhost/svc");
		return new ClassicDetailEditPageResolver(client, urlBuilder);
	}

	[Test]
	[Description("ResolveChildPages returns the edit card and the add mini page a detail entity registers in SysModuleEdit.")]
	public void ResolveChildPages_ShouldReturnCardAndMiniPage_WhenEntityRegistersBoth() {
		// Arrange
		IApplicationClient client = ClientReturning(
			Rows($"{{\"Name\":\"Contract\",\"UId\":\"{ContractEntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SysEntitySchemaUId\":\"{ContractEntityUId}\",\"CardSchemaUId\":\"{ContractPageUId}\"," +
				$"\"MiniPageSchemaUId\":\"{MiniPageUId}\"}}"),
			Rows($"{{\"UId\":\"{ContractPageUId}\",\"Name\":\"ContractPageV2\"}}",
				$"{{\"UId\":\"{MiniPageUId}\",\"Name\":\"ContractMiniPage\"}}"));
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "Contract" });

		// Assert
		result.Error.Should().BeNull(because: "a complete three-step lookup is not a failure");
		result.ChildPages.Select(page => page.SchemaName).Should().Equal(
			new[] { "ContractPageV2", "ContractMiniPage" },
			because: "both the edit card and its add mini page are child pages of the detail's entity, card first");
		result.ChildPages.Should().OnlyContain(page => page.EntityName == "Contract",
			because: "each page is attributed to the entity the caller asked about, so the caller can map it back to its detail");
		result.ChildPages.Single(page => page.SchemaName == "ContractMiniPage").IsMiniPage.Should().BeTrue(
			because: "the mini page is flagged so a caller can tell it apart from the edit card");
	}

	[Test]
	[Description("ResolveChildPages skips a CardSchemaUId of Guid.Empty instead of treating the unset reference as a page.")]
	public void ResolveChildPages_ShouldSkipEmptyGuidReferences() {
		// Arrange — a real SysModuleEdit row whose card AND mini page references are both unset (the shape a file
		// entity registers): DataService returns the all-zero GUID, not null, so a "not null" test would accept it.
		IApplicationClient client = ClientReturning(
			Rows($"{{\"Name\":\"AccountFile\",\"UId\":\"{ContractEntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SysEntitySchemaUId\":\"{ContractEntityUId}\",\"CardSchemaUId\":\"{EmptyGuid}\"," +
				$"\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}"),
			Rows());
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "AccountFile" });

		// Assert
		result.Error.Should().BeNull(because: "an entity registering no page is a legitimate outcome, not a failure");
		result.ChildPages.Should().BeEmpty(
			because: "an all-zero GUID means 'no page registered', so it must not be resolved as one");
		_queries.Count(IsPageNameLookup).Should().Be(0,
			because: "with every reference unset there is nothing to look a name up for, so the third query is skipped");
	}

	[Test]
	[Description("ResolveChildPages deduplicates a card an entity registers across several TypeColumnValue rows, and keeps distinct cards.")]
	public void ResolveChildPages_ShouldDeduplicatePerEntity_AcrossTypeRows() {
		// Arrange — Activity registers ActivityPageV2 twice (default + typed row) and EmailPageV2 once
		IApplicationClient client = ClientReturning(
			Rows($"{{\"Name\":\"Activity\",\"UId\":\"{ActivityEntityUId}\",\"ExtendParent\":false}}"),
			Rows(
				$"{{\"SysEntitySchemaUId\":\"{ActivityEntityUId}\",\"CardSchemaUId\":\"{ContractPageUId}\",\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}",
				$"{{\"SysEntitySchemaUId\":\"{ActivityEntityUId}\",\"CardSchemaUId\":\"{EmailPageUId}\",\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}",
				$"{{\"SysEntitySchemaUId\":\"{ActivityEntityUId}\",\"CardSchemaUId\":\"{ContractPageUId}\",\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}"),
			Rows($"{{\"UId\":\"{ContractPageUId}\",\"Name\":\"ActivityPageV2\"}}",
				$"{{\"UId\":\"{EmailPageUId}\",\"Name\":\"EmailPageV2\"}}"));
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "Activity" });

		// Assert
		result.ChildPages.Select(page => page.SchemaName).Should().Equal(new[] { "ActivityPageV2", "EmailPageV2" },
			because: "a card registered on several type rows is one child page, and row order is preserved");
	}

	[Test]
	[Description("ResolveChildPages resolves every requested entity in three batched queries rather than one lookup per entity.")]
	public void ResolveChildPages_ShouldIssueThreeQueries_ForManyEntities() {
		// Arrange
		IApplicationClient client = ClientReturning(
			Rows($"{{\"Name\":\"Contract\",\"UId\":\"{ContractEntityUId}\",\"ExtendParent\":false}}",
				$"{{\"Name\":\"Activity\",\"UId\":\"{ActivityEntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SysEntitySchemaUId\":\"{ContractEntityUId}\",\"CardSchemaUId\":\"{ContractPageUId}\",\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}",
				$"{{\"SysEntitySchemaUId\":\"{ActivityEntityUId}\",\"CardSchemaUId\":\"{EmailPageUId}\",\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}"),
			Rows($"{{\"UId\":\"{ContractPageUId}\",\"Name\":\"ContractPageV2\"}}",
				$"{{\"UId\":\"{EmailPageUId}\",\"Name\":\"ActivityPageV2\"}}"));
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "Contract", "Activity", "Contract" });

		// Assert
		_queries.Should().HaveCount(3,
			because: "the whole entity set resolves in three batched queries, so a large detail set costs no round-trip per detail");
		result.ChildPages.Select(page => page.EntityName + ":" + page.SchemaName).Should().BeEquivalentTo(
			new[] { "Contract:ContractPageV2", "Activity:ActivityPageV2" },
			because: "each entity's page is attributed to that entity, and a duplicate request name is collapsed");
	}

	[Test]
	[Description("ResolveChildPages picks the entity's base schema row (ExtendParent=false), not a replacing layer, so the SysModuleEdit filter matches the stable UId.")]
	public void ResolveChildPages_ShouldUseBaseEntityRow_WhenEntityHasReplacingLayers() {
		// Arrange — the replacing layer sorts first, so accepting the first row would query the wrong UId
		IApplicationClient client = ClientReturning(
			Rows($"{{\"Name\":\"Contract\",\"UId\":\"{ActivityEntityUId}\",\"ExtendParent\":true}}",
				$"{{\"Name\":\"Contract\",\"UId\":\"{ContractEntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SysEntitySchemaUId\":\"{ContractEntityUId}\",\"CardSchemaUId\":\"{ContractPageUId}\",\"MiniPageSchemaUId\":\"{EmptyGuid}\"}}"),
			Rows($"{{\"UId\":\"{ContractPageUId}\",\"Name\":\"ContractPageV2\"}}"));
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "Contract" });

		// Assert
		JObject editQuery = _queries.Single(query => query["rootSchemaName"]?.ToString() == "SysModuleEdit");
		editQuery.ToString().Should().Contain(ContractEntityUId,
			because: "the base row's UId is the stable migration unit the registration is keyed on");
		result.ChildPages.Should().ContainSingle(because: "the base-row UId matched one registration")
			.Which.SchemaName.Should().Be("ContractPageV2", because: "the registered card resolved to its schema name");
	}

	[Test]
	[Description("ResolveChildPages reports an entity whose metadata did not resolve as a warning instead of silently reading as 'registers nothing'.")]
	public void ResolveChildPages_ShouldWarn_WhenEntityMetadataDoesNotResolve() {
		// Arrange — the requested entity returns no SysSchema row at all
		IApplicationClient client = ClientReturning(Rows(), Rows(), Rows());
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "UsrGhostEntity" });

		// Assert
		result.Error.Should().BeNull(because: "an unresolvable entity name degrades the lookup, it does not fail it");
		result.ChildPages.Should().BeEmpty(because: "no entity UId means no registration could be read");
		result.Warnings.Should().ContainSingle(because: "the gap must reach the caller")
			.Which.Should().Contain("UsrGhostEntity",
				because: "the warning must name the entity so the caller knows which detail is unaccounted for");
	}

	[Test]
	[Description("ResolveChildPages reports a DataService failure through Error instead of throwing, so the caller can degrade.")]
	public void ResolveChildPages_ShouldReturnError_WhenDataServiceFails() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(default, default)
			.ReturnsForAnyArgs(_ => throw new InvalidOperationException("service unavailable"));
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "Contract" });

		// Assert
		result.Error.Should().Contain("service unavailable",
			because: "the transport failure is reported through the contract, never thrown at the caller");
		result.ChildPages.Should().BeEmpty(because: "a failed lookup resolves nothing");
	}

	[Test]
	[Description("ResolveChildPages chunks every In filter it issues so no single query can outgrow the DBMS parameter ceiling, and chunking alone does not manufacture a truncation warning.")]
	public void ResolveChildPages_ShouldChunkEveryInFilter_WhenValueSetExceedsChunkSize() {
		// Arrange — a page wide enough to push all three stages past one chunk: the entity-name list, the entity-UId
		// list, and (the sharpest edge) the page-UId list, whose size is row-driven rather than request-driven.
		const int entityCount = 450;
		const int editRowCount = 210;
		string[] names = Enumerable.Range(0, entityCount).Select(i => "UsrEntity" + i).ToArray();
		IApplicationClient client = ClientReturning(
			Rows(Enumerable.Range(0, entityCount)
				.Select(i => $"{{\"Name\":\"UsrEntity{i}\",\"UId\":\"{Uid(i)}\",\"ExtendParent\":false}}").ToArray()),
			Rows(Enumerable.Range(0, editRowCount)
				.Select(i => $"{{\"SysEntitySchemaUId\":\"{Uid(i)}\",\"CardSchemaUId\":\"{Uid(1000 + i)}\"," +
					$"\"MiniPageSchemaUId\":\"{Uid(2000 + i)}\"}}").ToArray()),
			Rows());
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(names);

		// Assert
		result.Error.Should().BeNull(because: "a wide page must resolve in several bounded queries, not fail one oversized one");
		_queries.SelectMany(InFilterSizes).Should().OnlyContain(
			size => size <= ClassicEntitySchemaQuery.InFilterChunkSize,
			because: "each In value costs one query parameter, so an unchunked list would throw past the ceiling and " +
				"abandon the whole page's child-page set at once");
		_queries.Count(query => query["rootSchemaName"]?.ToString() == "SysSchema" &&
			query["columns"]?["items"]?["ExtendParent"] != null).Should().Be(2,
			because: "450 entity names split into two bounded entity-name queries");
		_queries.Count(query => query["rootSchemaName"]?.ToString() == "SysModuleEdit").Should().Be(2,
			because: "the 450 resolved entity UIds split into two bounded SysModuleEdit queries");
		_queries.Count(IsPageNameLookup).Should().Be(2,
			because: "the 420 distinct page UIds those rows reference split into two bounded name lookups");
		result.Warnings.Should().NotContain(warning => warning.Contains("rowCount cap"),
			because: "each chunk carries its own proportional rowCount and the cap is checked over the accumulated " +
				"rows, so splitting a query cannot invent a truncation the unchunked query would not have reported");
	}

	[Test]
	[Description("ResolveChildPages reports the entities the metadata actually answered for, so a caller can tell 'registers nothing' from 'could not look'.")]
	public void ResolveChildPages_ShouldReportResolvedEntities_SeparatelyFromTheWarnedOnes() {
		// Arrange — Contract resolves and registers nothing; UsrGhostEntity has no SysSchema row at all
		IApplicationClient client = ClientReturning(
			Rows($"{{\"Name\":\"Contract\",\"UId\":\"{ContractEntityUId}\",\"ExtendParent\":false}}"),
			Rows(),
			Rows());
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { "Contract", "UsrGhostEntity" });

		// Assert
		result.Error.Should().BeNull(because: "one unresolvable name degrades the lookup, it does not fail it");
		result.ResolvedEntities.Should().BeEquivalentTo(new[] { "Contract" },
			because: "only the entity the metadata answered for may be read as a verified 'registers no edit page'; " +
				"the warned one was never looked up and must not be mistaken for one");
	}

	[Test]
	[Description("ResolveChildPages returns an empty result without querying anything when no entity name is supplied.")]
	public void ResolveChildPages_ShouldReturnEmpty_WhenNoEntityNamesSupplied() {
		// Arrange
		IApplicationClient client = ClientReturning(Rows(), Rows(), Rows());
		ClassicDetailEditPageResolver resolver = Create(client);

		// Act
		ClassicChildPageLookup result = resolver.ResolveChildPages(new[] { " ", null, string.Empty });

		// Assert
		result.Error.Should().BeNull(because: "nothing to resolve is not a failure");
		result.ChildPages.Should().BeEmpty(because: "no usable entity name was supplied");
		_queries.Should().BeEmpty(because: "a page with no resolvable detail entities must cost no round-trip at all");
	}
}
