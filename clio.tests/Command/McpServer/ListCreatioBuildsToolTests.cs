using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ListCreatioBuildsToolTests {
	private const string ProductsFolder = @"F:\CreatioBuilds";

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable list-creatio-builds MCP tool name.")]
	public void ListCreatioBuilds_Should_Advertise_Stable_Tool_Name() {
		ListCreatioBuildsTool.ListCreatioBuildsToolName.Should().Be("list-creatio-builds",
			because: "the MCP contract should keep a stable build-discovery tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Enumerates build archives under the configured products folder, newest first, exposing the deploy-creatio zip path.")]
	public void ListCreatioBuilds_Should_List_Builds_Newest_First() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetCreatioProductsFolder().Returns(ProductsFolder);
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData> {
			[@"F:\CreatioBuilds\8.1.5\older.zip"] = new MockFileData("a") {
				LastWriteTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
			},
			[@"F:\CreatioBuilds\8.1.6\newer.zip"] = new MockFileData("bb") {
				LastWriteTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
			},
			[@"F:\CreatioBuilds\readme.txt"] = new MockFileData("ignored")
		});
		ListCreatioBuildsTool tool = new(settingsRepository, fileSystem);

		// Act
		ListCreatioBuildsResult result = tool.ListCreatioBuilds();

		// Assert
		result.Status.Should().Be("ok",
			because: "builds were found under the configured products folder");
		result.ProductsFolder.Should().Be(ProductsFolder,
			because: "the resolved products folder should be surfaced");
		result.ProductsFolderExists.Should().BeTrue(
			because: "the configured products folder exists in the mock file system");
		result.Builds.Should().HaveCount(2,
			because: "only .zip build archives should be returned, not unrelated files");
		result.Builds.Select(build => build.FileName).Should().Equal(["newer.zip", "older.zip"],
			because: "builds should be ordered newest-first by last write time");
		result.Builds[0].FullPath.Should().Be(@"F:\CreatioBuilds\8.1.6\newer.zip",
			because: "the full path is what the caller passes to deploy-creatio as zip-file");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports products-folder-not-configured when no creatio-products folder is set.")]
	public void ListCreatioBuilds_Should_Report_NotConfigured_When_Folder_Unset() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetCreatioProductsFolder().Returns((string)null);
		ListCreatioBuildsTool tool = new(settingsRepository, new MockFileSystem());

		// Act
		ListCreatioBuildsResult result = tool.ListCreatioBuilds();

		// Assert
		result.Status.Should().Be("products-folder-not-configured",
			because: "an unset products folder must be reported with actionable status rather than an empty list");
		result.Builds.Should().BeEmpty(
			because: "no builds can be listed without a configured folder");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports products-folder-missing with the resolved path when the configured folder does not exist.")]
	public void ListCreatioBuilds_Should_Report_Missing_When_Folder_Absent() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetCreatioProductsFolder().Returns(@"F:\DoesNotExist");
		ListCreatioBuildsTool tool = new(settingsRepository, new MockFileSystem());

		// Act
		ListCreatioBuildsResult result = tool.ListCreatioBuilds();

		// Assert
		result.Status.Should().Be("products-folder-missing",
			because: "a stale configured products path must be surfaced explicitly");
		result.ProductsFolder.Should().Be(@"F:\DoesNotExist",
			because: "the resolved path should be reported so the caller can fix the configuration");
		result.ProductsFolderExists.Should().BeFalse(
			because: "the configured folder does not exist");
		result.Message.Should().Contain("does not exist",
			because: "the message should be an actionable remediation hint");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports no-builds-found when the products folder exists but has no zip archives.")]
	public void ListCreatioBuilds_Should_Report_NoBuilds_When_Folder_Empty() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetCreatioProductsFolder().Returns(ProductsFolder);
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData> {
			[@"F:\CreatioBuilds\notes.txt"] = new MockFileData("no builds here")
		});
		ListCreatioBuildsTool tool = new(settingsRepository, fileSystem);

		// Act
		ListCreatioBuildsResult result = tool.ListCreatioBuilds();

		// Assert
		result.Status.Should().Be("no-builds-found",
			because: "an existing-but-empty products folder should be distinguished from a missing one");
		result.ProductsFolderExists.Should().BeTrue(
			because: "the folder exists even though it holds no builds");
		result.Builds.Should().BeEmpty(
			because: "no .zip archives are present");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes read-only, non-destructive MCP metadata for build discovery.")]
	public void ListCreatioBuilds_Should_Expose_ReadOnly_Metadata() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ListCreatioBuildsTool)
			.GetMethod(nameof(ListCreatioBuildsTool.ListCreatioBuilds))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ListCreatioBuildsTool.ListCreatioBuildsToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeTrue(
			because: "listing builds only reads the filesystem");
		attribute.Destructive.Should().BeFalse(
			because: "build discovery changes nothing");
	}
}
