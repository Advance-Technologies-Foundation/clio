using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Per-page <c>checksum</c> on <c>sync-pages</c> (issue #1464). Before this, the tool clio itself calls
/// the canonical page write path had NO way to pin a conflict baseline: every write took the unpinned
/// path against the <c>.clio-pages</c> baseline — which is keyed by (anchor directory, schema name) and
/// can therefore describe a different body than the one the caller read — leaving the per-page
/// <c>force: true</c> as the only exit. <c>force</c> is the one flag that must stay reserved for real
/// conflicts, so an agent reaching for it by reflex is how the next genuine concurrent edit gets
/// overwritten unnoticed.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageSyncToolChecksumTests {

	private const string SchemaUId = "test-uid";
	private const string SchemaName = "UsrTodo_FormPage";
	private const string ServerChecksum = "server-checksum";

	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;

	private static string ChecksumRow(string checksum) =>
		$$"""{"success": true, "rows": [{"Checksum": "{{checksum}}", "ModifiedOn": "2026-09-11T09:00:00"}]}""";

	private PageUpdateCommand CreateUpdateCommand() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => "http://test" + callInfo.Arg<string>());
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ChecksumRow(ServerChecksum));
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "schema": {"body": "original"} }""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		return new PageUpdateCommand(
			_applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(), hierarchyClient);
	}

	// The REAL PageBaselineGuard over an empty file system: no .clio-pages baseline exists, so the pin
	// is the only thing that can arm the check - which is exactly the shape this argument exists for.
	private PageSyncTool CreateTool() {
		MockFileSystem fileSystem = new();
		// Built BEFORE the Returns() call: NSubstitute forbids configuring other substitutes inside a
		// Returns() argument, and CreateUpdateCommand stubs several.
		PageUpdateCommand updateCommand = CreateUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		return new PageSyncTool(
			commandResolver, fileSystem,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(fileSystem),
			new PersistedResourceKeyReader());
	}

	private static PageSyncArgs BuildArgs(string checksum, bool? force = null) =>
		new("dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody, Force: force, Checksum: checksum)],
			Validate: false,
			SkipSampling: true,
			OutputDirectory: "/ws");

	private void AssertNoSave() => _applicationClient.DidNotReceive().ExecutePostRequest(
		Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
		Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	[Test]
	[Description("(d) A per-page checksum matching the server pins the baseline and the save proceeds - the contract update-page gained in #1356, on the tool update-page's own ToolDeprecation points callers at (issue #1464).")]
	public async Task SyncPages_ShouldSave_WhenThePinnedChecksumMatchesTheServer() {
		// Arrange
		PageSyncTool tool = CreateTool();

		// Act
		PageSyncResponse response = await tool.SyncPages(BuildArgs(ServerChecksum), null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeTrue(because: "the caller read exactly the body the server still holds");
		page.Conflict.Should().BeFalse(because: "a matching pin is the opposite of an external modification");
	}

	[Test]
	[Description("(d) A stale per-page checksum is refused with the SAME per-page conflict shape the on-disk baseline produces - detection is not weakened by letting the caller pin (issue #1464).")]
	public async Task SyncPages_ShouldReportConflict_WhenThePinnedChecksumIsStale() {
		// Arrange
		PageSyncTool tool = CreateTool();

		// Act
		PageSyncResponse response = await tool.SyncPages(BuildArgs("00000000000000000000000000000000"), null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeFalse(because: "somebody else saved this schema since the caller read it");
		page.Conflict.Should().BeTrue(because: "the per-page conflict flag is what tells the caller to re-read");
		page.ConflictDetails.Should().NotBeNull(
			because: "a conflict a caller cannot inspect leaves force:true as its only response");
		page.ConflictDetails.Reason.Should().Be("checksum-mismatch",
			because: "the pin differs from the server checksum, which is exactly that reason and not a schema-identity one");
		page.ConflictDetails.ActualChecksum.Should().Be(ServerChecksum,
			because: "the caller needs the server's current value to recognise what it would have overwritten");
		AssertNoSave();
	}

	[Test]
	[Description("(d) The per-page force flag keeps its meaning alongside a pin: an explicitly confirmed overwrite still goes through even when the pinned checksum is stale (issue #1464).")]
	public async Task SyncPages_ShouldSave_WhenTheChecksumIsStaleAndForceIsSet() {
		// Arrange
		PageSyncTool tool = CreateTool();

		// Act
		PageSyncResponse response = await tool.SyncPages(
			BuildArgs("00000000000000000000000000000000", force: true), null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeTrue(because: "force is the explicit, user-confirmed overwrite");
		page.Conflict.Should().BeFalse(because: "force skips the external-modification check entirely");
	}

	[TestCase("   ")]
	[TestCase("")]
	[Description("(e) A whitespace-only or empty checksum collapses to 'not supplied' at the shared PageBaselineGuard chokepoint, exactly as on update-page - arming the guard with nothing to compare would report a mismatch that never happened (issue #1464).")]
	public async Task SyncPages_ShouldTreatABlankChecksumAsNotSupplied(string blankChecksum) {
		// Arrange - no .clio-pages baseline exists, so with no pin nothing arms the check and the save runs.
		PageSyncTool tool = CreateTool();

		// Act
		PageSyncResponse response = await tool.SyncPages(BuildArgs(blankChecksum), null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeTrue(because: "a blank pin is equivalent to omitting the argument");
		page.Conflict.Should().BeFalse(because: "nothing armed the comparison");
	}

	[Test]
	[Description("(e) A padded checksum - the natural shape when the value is piped from a file or a shell substitution - is trimmed at the same chokepoint, so it matches rather than reporting a false conflict (issue #1464).")]
	public async Task SyncPages_ShouldMatch_WhenThePinnedChecksumCarriesSurroundingWhitespace() {
		// Arrange
		PageSyncTool tool = CreateTool();

		// Act
		PageSyncResponse response = await tool.SyncPages(BuildArgs("  " + ServerChecksum + "\n"), null);

		// Assert
		PageSyncPageResult page = response.Pages.Should().ContainSingle().Subject;
		page.Success.Should().BeTrue(
			because: "the arming predicate is whitespace-tolerant while the comparison is strictly Ordinal, so an untrimmed pin armed the check and then failed it");
		page.Conflict.Should().BeFalse(
			because: "a trimmed pin that matches the server is not an external modification");
	}

	[Test]
	[Description("The per-page pin is per PAGE: a stale pin fails only its own page and leaves the rest of the batch to complete, matching the per-page conflict contract sync-pages already documents (issue #1464).")]
	public async Task SyncPages_ShouldFailOnlyThePinnedPage_WhenAnotherPageInTheBatchIsUnpinned() {
		// Arrange
		PageSyncTool tool = CreateTool();
		PageSyncArgs args = new("dev",
			[
				new PageSyncPageInput(SchemaName, ValidPageBody, Checksum: "00000000000000000000000000000000"),
				new PageSyncPageInput("UsrOther_FormPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "every submitted page must produce a result, conflicted or not");
		response.Pages[0].Conflict.Should().BeTrue(because: "its pin is stale");
		response.Pages[1].Success.Should().BeTrue(
			because: "an unpinned page with no on-disk baseline has nothing armed and must not inherit its neighbour's conflict");
	}
}
