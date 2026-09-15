using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Characterization tests for the <c>sync-pages</c> label-resource gate (issue #1464). The AC-2 rescue
/// of #1356 landed on <c>update-page</c> and on the command-level gate, but <c>sync-pages</c> rejected
/// the body in its OWN pre-execution gate, before the command was ever resolved — so the recommended
/// write path kept the defect its deprecated fallback had just lost. These tests pin the behaviour on
/// <c>sync-pages</c> itself so the gap cannot reopen silently.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageSyncToolPersistedResourcesTests {

	private const string SchemaName = "UsrTodo_FormPage";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string PersistedResourceKey = "CaseSLA_label";

	/// <summary>The invariant half of the diagnostic this gate produces; unchanged by issue #1464.</summary>
	private const string UnresolvedLabelSentence =
		"is neither auto-provided by a DS-bound attribute nor registered in the 'resources' parameter.";

	private IApplicationClient _applicationClient;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private PageUpdateCommand _updateCommand;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => "http://test" + callInfo.Arg<string>());
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		StubSchemaWithPersistedKeys(PersistedResourceKey);
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		_hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		_updateCommand = new PageUpdateCommand(
			_applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(), _hierarchyClient);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_hierarchyClient.ClearReceivedCalls();
	}

	private void StubSchemaWithPersistedKeys(params string[] persistedKeys) {
		string entries = string.Join(",", persistedKeys.Select(
			key => "{\"name\": \"" + key + "\", \"value\": \"stored caption\"}"));
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\": true, \"schema\": {\"uId\": \"" + SchemaUId + "\", \"name\": \"" + SchemaName
				+ "\", \"body\": \"original\", \"localizableStrings\": [" + entries + "]}}");
	}

	private PageSyncTool CreateTool(MockFileSystem fileSystem = null) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(_updateCommand);
		return new PageSyncTool(
			commandResolver, fileSystem ?? new MockFileSystem(),
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(fileSystem ?? new MockFileSystem()),
			new PersistedResourceKeyReader());
	}

	/// <summary>
	/// A body inserting a field whose label points at <paramref name="resourceKey"/> while binding to a
	/// DIFFERENT attribute name, so the platform cannot auto-provide the caption: the validator rejects it
	/// unless the key is found among the keys already persisted on the schema. The exact shape issue #1320
	/// reported — the second save of a page whose key the FIRST save registered.
	/// </summary>
	private static string BuildLabelResourcePageBody(string resourceKey) =>
		BuildDiffBackedPageBody(
			"""
			[
				{
					"operation":"insert",
					"name":"CaseSLA",
					"values":{"type":"crt.Input","label":"$Resources.Strings.
			""".TrimEnd() + resourceKey + """
			","control":"$PDS_CaseSLA"}
				}
			]
			""",
			"""
			[
				{
					"operation":"merge",
					"path":[],
					"values":{"attributes":{"PDS_CaseSLA":{"modelConfig":{"path":"PDS.UsrSLA"}}}}
				}
			]
			""");

	private static string BuildDiffBackedPageBody(string viewConfigDiff, string viewModelConfigDiff) =>
		$$"""
		define(
			"{{SchemaName}}",
			/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
			function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			}
		);
		""";

	private static PageSyncArgs BuildArgs(params PageSyncPageInput[] pages) =>
		new("dev", pages, Validate: true, SkipSampling: true);

	private int HierarchyResolutionCount() => _hierarchyClient.ReceivedCalls().Count(call =>
		call.GetMethodInfo().Name == nameof(IPageDesignerHierarchyClient.GetDesignPackageUId));

	private int SchemaReadCount() => _applicationClient.ReceivedCalls().Count(call =>
		call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest)
		&& call.GetArguments().FirstOrDefault() is string url && url.Contains("GetSchema"));

	[Test]
	[Description("(a) sync-pages accepts a body whose label key is ALREADY persisted on the schema and is NOT repeated in `resources` - the rescue #1356 shipped on update-page now also clears the gate that rejected the save before the command was reached (issue #1464).")]
	public async Task SyncPages_ShouldSave_WhenTheLabelResourceIsOnlyPersistedOnTheSchema() {
		// Arrange
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = BuildArgs(
			new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody(PersistedResourceKey)));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().ContainSingle().Which.Success.Should().BeTrue(
			because: "a key stored in the schema's localizableStrings resolves at runtime whether or not this call repeats it");
		response.Success.Should().BeTrue(
			because: "the batch verdict must follow the only page in it");
	}

	[Test]
	[Description("(b) A genuinely unregistered label key is STILL rejected by sync-pages, with the unchanged diagnostic sentence - the rescue widens what is accepted only for keys the schema actually stores (issue #1464).")]
	public async Task SyncPages_ShouldReject_WhenTheLabelResourceIsNeitherSentNorPersisted() {
		// Arrange
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = BuildArgs(
			new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody("NeverRegistered_label")));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeFalse(
			because: "a key that is neither passed in `resources` nor stored on the schema renders the label blank");
		page.Error.Should().Contain(UnresolvedLabelSentence,
			because: "the user-facing wording is unchanged by the machine-readable error identity");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("(c) The syntax-failure path keeps its no-Creatio-I/O promise (ENG-92049): a body that cannot parse is diagnosed offline, so the persisted-key provider must not be offered there even though the same content chain runs.")]
	public async Task SyncPages_ShouldNotReadTheSchema_WhenTheBodyCannotParse() {
		// Arrange
		PageSyncTool tool = CreateTool();
		string unparsableBody = BuildLabelResourcePageBody(PersistedResourceKey)
			.Replace("return {", "return {{{", StringComparison.Ordinal);
		PageSyncArgs args = BuildArgs(new PageSyncPageInput(SchemaName, unparsableBody));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().ContainSingle().Which.Success.Should().BeFalse(
			because: "a body that does not parse cannot be saved");
		SchemaReadCount().Should().Be(0,
			because: "the syntax-failure path promises no Creatio round trip for a body that cannot even parse");
	}

	[Test]
	[Description("The owning module resolves the schema hierarchy ONCE for the rescue, however many gates ask for it: sync-pages runs the label-resource chain at THREE points in one logical save - the deterministic pre-pass, the in-lock re-validation and the command-level gate - and an unmemoized provider would pay the resolution at each (issue #1464).")]
	public async Task SyncPages_ShouldResolveTheHierarchyOnceForTheRescue_OnTopOfTheSavesOwnResolution() {
		// Arrange - a clean body needs no rescue, so its resolution count is the save's own cost and the
		// baseline the rescued save is measured against. A hard-coded expectation would silently absorb a
		// future change to how many times the SAVE resolves.
		PageSyncTool cleanTool = CreateTool();
		await cleanTool.SyncPages(
			BuildArgs(new PageSyncPageInput(SchemaName, BuildDiffBackedPageBody("[]", "[]"))), null);
		int cleanSaveResolutions = HierarchyResolutionCount();
		int cleanSaveSchemaReads = SchemaReadCount();
		_hierarchyClient.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = BuildArgs(
			new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody(PersistedResourceKey)));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		cleanSaveResolutions.Should().BeGreaterThan(0,
			because: "a zero baseline would make the comparison below vacuous");
		cleanSaveSchemaReads.Should().BeGreaterThan(0,
			because: "a zero baseline would make the schema-read comparison below vacuous too");
		response.Pages.Should().ContainSingle().Which.Success.Should().BeTrue(
			because: "the count below is only meaningful for a save that actually took the rescue path");
		HierarchyResolutionCount().Should().Be(cleanSaveResolutions + 1,
			because: "the rescue costs ONE hierarchy resolution for the whole batch - one per gate would triple it");
		SchemaReadCount().Should().Be(cleanSaveSchemaReads + 1,
			because: "the GetSchema behind the rescue is paid once too - the hierarchy count alone would not notice a cached context re-reading the schema per gate");
	}

	[Test]
	[Description("A batch with TWO pages on the SAME schema: the first page REGISTERS the key, so the second must be validated against the post-save key set. Caching the read for the whole batch would judge page 2 against the keys read before page 1 saved and reject it for a key that now exists (issue #1464 review).")]
	public async Task SyncPages_ShouldRevalidateAgainstThePostSaveKeys_WhenTwoPagesTargetTheSameSchema() {
		// Arrange - the schema starts with NO persisted keys, so only page 1's own save can register one.
		StubSchemaWithPersistedKeys();
		PageSyncTool tool = CreateTool();
		string body = BuildLabelResourcePageBody(PersistedResourceKey);
		PageSyncArgs args = new("dev",
			[
				new PageSyncPageInput(SchemaName, body, Resources: "{\"" + PersistedResourceKey + "\": \"Probe label\"}"),
				new PageSyncPageInput(SchemaName, body)
			],
			Validate: true, SkipSampling: true);
		// Page 1's save registers the key; from that moment the schema reports it as persisted.
		_applicationClient
			.When(client => client.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(_ => StubSchemaWithPersistedKeys(PersistedResourceKey));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "every submitted page must produce its own result");
		response.Pages[0].Success.Should().BeTrue(
			because: $"page 1 carries the key in `resources`. Error: {response.Pages[0].Error}");
		response.Pages[1].Success.Should().BeTrue(
			because: $"page 1's save persisted the key, so page 2 must be re-validated against the CURRENT key set rather than the one cached before that save. Error: {response.Pages[1].Error}");
	}

	[Test]
	[Description("The cached read is DROPPED after a save of that schema: page 1 takes the rescue (so its key set is cached), then registers a further key, and page 2 - validated in the lock after that save - must see the post-save key set rather than the one page 1's rescue cached (issue #1464 review).")]
	public async Task SyncPages_ShouldDropTheCachedRead_WhenAnEarlierPageSavedTheSameSchema() {
		// Arrange - the schema starts with only PersistedResourceKey, so page 1's rescue caches exactly
		// that set; page 1's own `resources` then registers SecondResourceKey, which page 2's label needs.
		const string secondResourceKey = "SecondProbe_label";
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = new("dev",
			[
				new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody(PersistedResourceKey),
					Resources: "{\"" + secondResourceKey + "\": \"Second label\"}"),
				new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody(secondResourceKey))
			],
			Validate: true, SkipSampling: true);
		_applicationClient
			.When(client => client.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(_ => StubSchemaWithPersistedKeys(PersistedResourceKey, secondResourceKey));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "every submitted page must produce its own result");
		response.Pages[0].Success.Should().BeTrue(
			because: $"page 1's label key is already persisted, so the rescue clears it - and that rescue is what puts a key set in the cache. Error: {response.Pages[0].Error}");
		response.Pages[1].Success.Should().BeTrue(
			because: $"page 1's save registered page 2's label key, so a cache entry that survived that save would reject page 2 for a key that exists. Error: {response.Pages[1].Error}");
	}

	[Test]
	[Description("Non-vacuity twin: a page the rescue cannot help is still rejected, proving the accepted case above is the persisted key doing the work and not a disabled validator (issue #1464).")]
	public async Task SyncPages_ShouldRejectOnlyTheUnrescuablePage_WhenABatchMixesBoth() {
		// Arrange
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = BuildArgs(
			new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody(PersistedResourceKey)),
			new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody("NeverRegistered_label")));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "every submitted page must produce its own result");
		response.Pages[0].Success.Should().BeTrue(because: "its key is persisted on the schema");
		response.Pages[1].Success.Should().BeFalse(because: "its key is registered nowhere");
		response.Success.Should().BeFalse(because: "one failed page fails the batch verdict");
	}

	[Test]
	[Description("A failed persisted-key read never changes the verdict, but its reason must reach the caller on THAT page's own warning channel - otherwise a 401 or an unresolved hierarchy is reported as 'resource is neither auto-provided nor registered' (issues #1320, #1464).")]
	public async Task SyncPages_ShouldWarn_WhenThePersistedKeyReadIsRefused() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false, "errorInfo": {"message": "Access denied to schema"}}""");
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = BuildArgs(
			new PageSyncPageInput(SchemaName, BuildLabelResourcePageBody(PersistedResourceKey)));

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeFalse(
			because: "an unreadable schema leaves the stricter verdict standing");
		(page.Validation?.Warnings ?? Array.Empty<string>()).Should().Contain(
			warning => warning.Contains("Persisted resource keys could not be read"),
			because: "the reason must be visible on the page result, not only in a log the MCP caller never sees");
	}
}
