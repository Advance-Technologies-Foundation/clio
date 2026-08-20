using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageSyncToolBaselineTests
{
	private const string SchemaUId = "test-uid";
	private const string SchemaName = "UsrTodo_FormPage";
	private const string SchemaDirPath = "/ws/.clio-pages/UsrTodo_FormPage";
	private const string MetaPath = SchemaDirPath + "/meta.json";
	private const string BodyPath = SchemaDirPath + "/body.js";
	private const string BundlePath = SchemaDirPath + "/bundle.json";

	private const string VerifiedChecksum = "verify-checksum";
	private const string PreviousBody = "define('TestPage', [], function() { return 'previous-generation'; });";
	private const string CompetingWriterBody = "define('TestPage', [], function() { return 'competing-generation'; });";
	private const string CompetingWriterChecksum = "competing-writer-checksum";

	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static IPageDesignerHierarchyClient CreateHierarchyClient() {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		return hierarchyClient;
	}

	/// <summary>
	/// Builds a PageUpdateCommand whose SelectQuery stub distinguishes the byUId checksum query
	/// from the by-name metadata query, dequeuing <paramref name="checksumResponses"/> for the
	/// former so the conflict check and the post-save refresh can return different values.
	/// </summary>
	private static PageUpdateCommand CreateUpdateCommand(params string[] checksumResponses) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		Queue<string> checksumQueue = new(checksumResponses);
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => checksumQueue.Count > 0 ? checksumQueue.Dequeue() : """{"success": false}""");
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "schema": {"body": "original"} }""");
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		return new PageUpdateCommand(
			applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), Substitute.For<IPageBaselineGuard>(), CreateHierarchyClient());
	}

	private static string ChecksumRow(string checksum) =>
		$$"""{"success": true, "rows": [{"Checksum": "{{checksum}}", "ModifiedOn": "2026-06-12T09:00:00"}]}""";

	private static PageSyncTool CreateTool(PageUpdateCommand updateCommand, MockFileSystem fileSystem,
		PageGetCommand getCommand = null, IInterprocessFileGate fileGate = null) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		if (getCommand != null) {
			commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(getCommand);
		}
		// The baseline guard stays UNGATED even when a gate is supplied: only the tool's own
		// .clio-pages writes are under test here, and routing the guard's pre-save read through the same
		// gate double would consume a one-shot interleaving trigger before the verify publication runs.
		return new PageSyncTool(
			commandResolver, fileSystem,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(fileSystem),
			fileGate: fileGate);
	}

	private static MockFileSystem CreateFileSystemWithBaseline(string checksum, string environmentName = "dev") {
		MockFileSystem fileSystem = new();
		fileSystem.AddFile(MetaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-12T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = environmentName,
				EditableSchemaExists = true,
				EditableSchemaUId = SchemaUId,
				Checksum = checksum,
				ModifiedOn = "raw",
				CapturedAt = "2026-06-12T10:00:00Z"
			}
		})));
		return fileSystem;
	}

	[Test]
	[Description("sync-pages must fail a stale-baseline page with a per-page conflict while the rest of the batch continues.")]
	public async Task SyncPages_ShouldReturnConflictPerPage_WhenBaselineChecksumStale() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystemWithBaseline("baseline-checksum");
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("server-checksum"));
		PageSyncTool tool = CreateTool(updateCommand, fileSystem);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput(SchemaName, ValidPageBody),
				new PageSyncPageInput("UsrOther_FormPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(because: "one page in the batch hit an external-modification conflict");
		response.Pages[0].Conflict.Should().BeTrue(because: "the stale-baseline page must surface the conflict marker");
		response.Pages[0].ConflictDetails.Reason.Should().Be(PageConflictReasons.ChecksumMismatch,
			because: "the server checksum differs from the stored baseline");
		response.Pages[0].Error.Should().Contain("Re-run get-page",
			because: "the per-page error must guide the agent to reload and rebase");
		response.Pages[1].Success.Should().BeTrue(because: "a conflict on one page must not abort the rest of the batch");
		response.Pages[1].Conflict.Should().BeFalse(because: "the second page has no baseline and therefore no conflict");
	}

	[Test]
	[Description("sync-pages must honor the per-page force flag, overwriting despite a stale baseline and refreshing it from the post-save checksum.")]
	public async Task SyncPages_ShouldOverwriteAndRefreshBaseline_WhenPerPageForceTrue() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystemWithBaseline("baseline-checksum");
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("fresh-after-save"));
		PageSyncTool tool = CreateTool(updateCommand, fileSystem);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody, Force: true)],
			Validate: false,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(because: "per-page force=true deliberately bypasses the conflict check");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("fresh-after-save",
			because: "after a forced overwrite the baseline must track the new server state");
	}

	[Test]
	[Description("sync-pages without verify must refresh the meta.json baseline from the post-save checksum when the baseline matched.")]
	public async Task SyncPages_ShouldRefreshBaselineFromNewChecksum_WhenVerifyFalse() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystemWithBaseline("match");
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("match"), ChecksumRow("fresh-2"));
		PageSyncTool tool = CreateTool(updateCommand, fileSystem);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody)],
			Validate: false,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(because: "a matching baseline allows the save to proceed");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("fresh-2",
			because: "consecutive syncs in the same session must compare against the post-save checksum");
		meta.FetchedAt.Should().Be("2026-06-12T10:00:00Z",
			because: "the refresh must not touch the get-page snapshot fields");
	}

	[Test]
	[Description("sync-pages must drop the baseline when the post-save checksum query fails (fail toward no-check).")]
	public async Task SyncPages_ShouldDropBaseline_WhenPostSaveChecksumUnavailable() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystemWithBaseline("match");
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("match"), """{"success": false}""");
		PageSyncTool tool = CreateTool(updateCommand, fileSystem);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody)],
			Validate: false,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(because: "a failed post-save metadata query must not fail the save");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().BeNull(
			because: "a stale baseline must be removed so the next sync skips the check instead of false-conflicting");
	}

	[Test]
	[Description("sync-pages with verify=true must write a fresh meta.json (with the verify-time baseline) next to the verified body.js.")]
	public async Task SyncPages_ShouldRewriteMetaJsonBaseline_WhenVerifyTrue() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystemWithBaseline("match");
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("match"));
		PageGetCommand getCommand = CreateGetCommandWithChecksum("verify-checksum");
		PageSyncTool tool = CreateTool(updateCommand, fileSystem, getCommand);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(because: "the verified save must succeed");
		response.Pages[0].VerifiedBodyFile.Should().NotBeNull(because: "verify=true writes the read-back body to disk");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().NotBeNull(because: "the verify path must rewrite the full meta.json including the baseline");
		meta.Baseline.Checksum.Should().Be("verify-checksum",
			because: "the baseline must reflect the post-save state captured by the verify read-back, fixing the stale-baseline gap");
		meta.FetchedAt.Should().NotBe("2026-06-12T10:00:00Z",
			because: "verify rewrites the whole meta.json snapshot with a fresh fetch timestamp");
	}

	private static PageGetCommand CreateGetCommandWithChecksum(string checksum) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ChecksumRow(checksum));
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject {
						["Name"] = SchemaName,
						["UId"] = SchemaUId,
						["PackageUId"] = "test-pkg-uid",
						["PackageName"] = "UsrPkg",
						["ParentSchemaName"] = "BaseModulePage",
						["SchemaType"] = 9
					}
				}
			}.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = SchemaUId,
					Name = SchemaName,
					PackageUId = "test-pkg-uid",
					PackageName = "UsrPkg",
					SchemaVersion = 1,
					Body = ValidPageBody
				}
			]);
		return new PageGetCommand(
			applicationClient,
			serviceUrlBuilder,
			Substitute.For<ILogger>(),
			hierarchyClient,
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			CreatePassthroughPageFileWriter());
	}

	[Test]
	[Description("A competing writer that replaces the whole schema directory in the gap between sync-pages' verified body write and its meta write must not be able to leave body.js and meta.json describing different generations.")]
	public async Task SyncPages_ShouldPublishTheVerifiedBodyAndItsBaselineTogether_WhenAnotherWriterReplacesTheSchemaDirectory() {
		// Arrange — the competing writer publishes a COMPLETE, different generation, the way get-page
		// does: it swaps the schema directory wholesale rather than editing files inside it.
		MockFileSystem fileSystem = CreateFileSystemWithPublishedPage();
		OneShotInterleavingFileGate fileGate = new(() => ReplaceSchemaDirectory(fileSystem));
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("match"));
		PageGetCommand getCommand = CreateGetCommandWithChecksum(VerifiedChecksum);
		PageSyncTool tool = CreateTool(updateCommand, fileSystem, getCommand, fileGate);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(because: "a concurrent local writer must not fail a save that landed on the server");
		response.Pages[0].VerifiedBodyFile.Should().NotBeNull(
			because: "the verify publication must actually have run for the pairing below to be evidence of anything");
		string body = fileSystem.GetFile(BodyPath).TextContents;
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fileSystem.GetFile(MetaPath).TextContents);
		bool bodyIsFromVerify = body == ValidPageBody;
		bool metaIsFromVerify = meta.Baseline?.Checksum == VerifiedChecksum;
		metaIsFromVerify.Should().Be(bodyIsFromVerify,
			because: "a baseline captured against one generation of body.js while the other generation sits beside it is exactly the stale-or-false conflict signal the baseline exists to prevent — the pair must be published without a gap another writer can enter");
	}

	[Test]
	[Description("sync-pages must publish the verified body and its refreshed baseline under ONE interprocess gate acquisition, so no other writer can interleave between them.")]
	public async Task SyncPages_ShouldTakeTheSchemaGateOnce_WhenPublishingTheVerifiedReadBack() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystemWithPublishedPage();
		OneShotInterleavingFileGate fileGate = new(() => { });
		PageUpdateCommand updateCommand = CreateUpdateCommand(ChecksumRow("match"));
		PageGetCommand getCommand = CreateGetCommandWithChecksum(VerifiedChecksum);
		PageSyncTool tool = CreateTool(updateCommand, fileSystem, getCommand, fileGate);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true,
			OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(because: "the verified save must succeed for its gate usage to be meaningful");
		fileGate.TopLevelAcquisitions.Should().Be(1,
			because: "every additional acquisition is a window in which a concurrent get-page can replace the schema directory between the body write and the meta write");
	}

	/// <summary>
	/// A complete, previously published <c>.clio-pages/{schema}/</c> tree: the three files a finished
	/// get-page leaves, with a baseline whose checksum matches what the update path will read back, so
	/// the save proceeds instead of raising a conflict.
	/// </summary>
	private static MockFileSystem CreateFileSystemWithPublishedPage() {
		MockFileSystem fileSystem = CreateFileSystemWithBaseline("match");
		fileSystem.AddFile(BodyPath, new MockFileData(PreviousBody));
		fileSystem.AddFile(BundlePath, new MockFileData("{\"name\":\"previous-generation\"}"));
		return fileSystem;
	}

	/// <summary>
	/// Publishes a whole other generation over the schema directory, mirroring how
	/// <c>PageFileWriter</c> publishes: the previous tree is replaced, not merged into.
	/// </summary>
	private static void ReplaceSchemaDirectory(MockFileSystem fileSystem) {
		// Normalised the way the sibling kill-safety fixture does, so the directory op is separator-agnostic.
		fileSystem.Directory.Delete(fileSystem.Path.GetFullPath(SchemaDirPath), recursive: true);
		fileSystem.AddFile(BodyPath, new MockFileData(CompetingWriterBody));
		fileSystem.AddFile(BundlePath, new MockFileData("{\"name\":\"competing-generation\"}"));
		fileSystem.AddFile(MetaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-08-19T00:00:00.0000000Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = "dev",
				EditableSchemaExists = true,
				EditableSchemaUId = SchemaUId,
				Checksum = CompetingWriterChecksum,
				ModifiedOn = "raw",
				CapturedAt = "2026-08-19T00:00:00.0000000Z"
			}
		})));
	}

	private static IPageFileWriter CreatePassthroughPageFileWriter() {
		IPageFileWriter writer = Substitute.For<IPageFileWriter>();
		writer.WritePageFiles(
				Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => callInfo.Arg<PageGetResponse>());
		return writer;
	}
}
