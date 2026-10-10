using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// GH-1752 at the MCP boundary: validate-page rejects an insert/move with a parentName but no propertyName on web
/// and mobile bodies, and get-page returns the recovery warning in its compact envelope.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PagePlacementSlotToolTests {
	private const string SlotlessMove = "[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"index\":0}]";

	private static PageValidateTool CreateValidateTool() => new(
		Substitute.For<IMobileComponentInfoCatalog>(),
		Substitute.For<IComponentInfoCatalog>(),
		new MockFileSystem(),
		new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));

	[TestCase(true)]
	[TestCase(false)]
	[Description("validate-page reports a move with a parentName but no propertyName as invalid on mobile and web bodies, matching the save path.")]
	public async Task ValidatePage_ShouldReject_WhenMoveNamesParentWithoutSlot(bool mobile) {
		// Arrange
		string body = mobile
			? PagePlacementSlotValidationTests.MobileBody(SlotlessMove)
			: PagePlacementSlotValidationTests.WebBody(SlotlessMove);

		// Act
		PageValidateResponse response = await CreateValidateTool().ValidatePage(new PageValidateArgs(Body: body));

		// Assert
		response.Valid.Should().BeFalse(because: "update-page and sync-pages refuse this body, so the pre-flight must too");
		response.Validation.ContentOk.Should().BeFalse(because: "the finding is a content error");
		response.Validation.Errors.Should().Contain(error => error.Contains("move 'Profile'") && error.Contains("propertyName"),
			because: "the error names the operation and the missing slot");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("validate-page accepts the same move once it names its propertyName slot.")]
	public async Task ValidatePage_ShouldAccept_WhenMoveNamesItsSlot(bool mobile) {
		// Arrange
		const string diff = "[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"propertyName\":\"items\",\"index\":0}]";
		string body = mobile ? PagePlacementSlotValidationTests.MobileBody(diff) : PagePlacementSlotValidationTests.WebBody(diff);

		// Act
		PageValidateResponse response = await CreateValidateTool().ValidatePage(new PageValidateArgs(Body: body));

		// Assert
		response.Valid.Should().BeTrue(because: "a move with its slot is valid");
	}

	[Test]
	[Description("The MCP get-page envelope carries the warning for a page it could only read by skipping a placement without a slot.")]
	public void GetPage_ShouldReturnWarnings_WhenPageWasReadWithoutSlotlessPlacement() {
		// Arrange
		const string schemaName = "UsrProof_MobileFormPage";
		const string parentDiff =
			"[{\"operation\":\"insert\",\"name\":\"Main\",\"values\":{\"items\":[]}},"
			+ "{\"operation\":\"insert\",\"name\":\"Feed\",\"values\":{\"items\":[]}},"
			+ "{\"operation\":\"insert\",\"name\":\"Profile\",\"parentName\":\"Main\",\"propertyName\":\"items\",\"values\":{}}]";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/SelectQuery");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{schemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"Template"}]}""");
		IPageDesignerHierarchyClient hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchy.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchy.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = schemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1,
				SchemaType = 10, Body = PagePlacementSlotValidationTests.MobileBody(SlotlessMove)
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "Template", PackageUId = "pkg-2", PackageName = "CrtPkg", SchemaVersion = 1,
				SchemaType = 10, Body = PagePlacementSlotValidationTests.MobileBody(parentDiff)
			}
		]);
		var command = new PageGetCommand(client, urls, logger, hierarchy, new PageSchemaBodyParser(),
			new PageBundleBuilder(() => new JsonDiffApplier(), () => new JsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		var tool = new PageGetTool(command, logger, resolver, new PageFileWriter(new MockFileSystem()));

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs(schemaName) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the page stays readable so it can be repaired: " + response.Error);
		response.Warnings.Should().ContainSingle(warning => warning.Contains("move 'Profile'"),
			because: "the compact MCP envelope must not drop the warning the command produced");
		response.Files.Should().NotBeNull(because: "the body is still written to disk for the repair");
	}
}
