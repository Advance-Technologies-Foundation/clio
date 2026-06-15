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
	private const string MetaPath = "/ws/.clio-pages/UsrTodo_FormPage/meta.json";

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
			applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), CreateHierarchyClient());
	}

	private static string ChecksumRow(string checksum) =>
		$$"""{"success": true, "rows": [{"Checksum": "{{checksum}}", "ModifiedOn": "2026-06-12T09:00:00"}]}""";

	private static PageSyncTool CreateTool(PageUpdateCommand updateCommand, MockFileSystem fileSystem,
		PageGetCommand getCommand = null) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		if (getCommand != null) {
			commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(getCommand);
		}
		return new PageSyncTool(
			commandResolver, fileSystem,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>());
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
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()));
	}
}
