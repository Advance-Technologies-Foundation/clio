using System.ComponentModel.DataAnnotations;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class PackageFileToolTests {
	[TestCase(nameof(PackageFileTool.ListPackageFiles), PackageFileTool.ListPackageFilesToolName)]
	[TestCase(nameof(PackageFileTool.GetPackageFile), PackageFileTool.GetPackageFileToolName)]
	[Category("Unit")]
	[Description("Declares both package file tools as read-only, non-destructive, idempotent operations.")]
	public void PackageFileTools_ShouldDeclareReadOnlySafetyFlags(string methodName, string toolName) {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(PackageFileTool)
			.GetMethod(methodName)!.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();

		// Assert
		attribute.Name.Should().Be(toolName, because: "each tool must use its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "package file inspection never changes Creatio");
		attribute.Destructive.Should().BeFalse(because: "a read must not require destructive approval");
		attribute.Idempotent.Should().BeTrue(because: "repeating the same read has no side effect");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only addresses the selected Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps list-package-files arguments to the environment-scoped command and returns its structured result.")]
	public void ListPackageFiles_ShouldMapArgumentsAndReturnResponse() {
		// Arrange
		FakePackageFileCommand command = new() {
			ListResponse = new PackageFileListResponse { Success = true, Files = ["UsrCode.csproj"], Count = 1 }
		};
		PackageFileTool tool = CreateTool(command);

		// Act
		PackageFileListResponse response = tool.ListPackageFiles(new ListPackageFilesArgs {
			EnvironmentName = "dev", PackageName = "UsrCode"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command returned a valid list");
		response.Files.Should().ContainSingle().Which.Should().Be("UsrCode.csproj",
			because: "the command result must pass through unchanged");
		command.CapturedOptions.Environment.Should().Be("dev", because: "the requested environment must be addressed");
		command.CapturedOptions.PackageName.Should().Be("UsrCode", because: "the requested package must be inspected");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps get-package-file arguments and returns both requested source and generated project content.")]
	public void GetPackageFile_ShouldMapArgumentsAndReturnBothContents() {
		// Arrange
		FakePackageFileCommand command = new() {
			ContentResponse = new PackageFileContentResponse {
				Success = true, Content = "class Probe {}", ProjectContent = "<Project />"
			}
		};
		PackageFileTool tool = CreateTool(command);

		// Act
		PackageFileContentResponse response = tool.GetPackageFile(new GetPackageFileArgs {
			EnvironmentName = "dev", PackageName = "UsrCode", FilePath = "src/cs/Probe.cs"
		});

		// Assert
		response.Content.Should().Be("class Probe {}", because: "the requested source must be returned exactly");
		response.ProjectContent.Should().Be("<Project />", because: "the generated project accompanies the source");
		command.CapturedOptions.FilePath.Should().Be("src/cs/Probe.cs",
			because: "the caller's package-relative path must be passed to the command");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts credential material from a non-fatal generated-project error before returning it through MCP.")]
	public void GetPackageFile_ShouldRedactProjectError_WhenPrimaryReadSucceeds() {
		// Arrange
		FakePackageFileCommand command = new() {
			ContentResponse = new PackageFileContentResponse {
				Success = true,
				Content = "class Probe {}",
				ProjectError = "project read failed; password=topsecret"
			}
		};
		PackageFileTool tool = CreateTool(command);

		// Act
		PackageFileContentResponse response = tool.GetPackageFile(new GetPackageFileArgs {
			EnvironmentName = "dev", PackageName = "UsrCode", FilePath = "src/cs/Probe.cs"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the primary source read succeeded");
		response.ProjectError.Should().NotContain("topsecret",
			because: "credential material must not cross the MCP boundary in a non-fatal error");
	}

	[Test]
	[Category("Unit")]
	[Description("Requires the args wrapper and both required get-package-file fields in the emitted MCP schema.")]
	public void GetPackageFile_ShouldDeclareRequiredArguments() {
		// Arrange & Act
		var method = typeof(PackageFileTool).GetMethod(nameof(PackageFileTool.GetPackageFile))!;
		object[] wrapperAttributes = method.GetParameters()[0].GetCustomAttributes(typeof(RequiredAttribute), false);
		object[] packageAttributes = typeof(PackageFileArgsBase).GetProperty(nameof(PackageFileArgsBase.PackageName))!
			.GetCustomAttributes(typeof(RequiredAttribute), false);
		object[] fileAttributes = typeof(GetPackageFileArgs).GetProperty(nameof(GetPackageFileArgs.FilePath))!
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		wrapperAttributes.Should().NotBeEmpty(because: "an omitted args object must fail schema validation");
		packageAttributes.Should().NotBeEmpty(because: "the tool cannot inspect an unnamed package");
		fileAttributes.Should().NotBeEmpty(because: "the tool cannot read an unnamed file");
	}

	private static PackageFileTool CreateTool(FakePackageFileCommand command) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ShowPackageFileContentCommand>(Arg.Any<ShowPackageFileContentOptions>()).Returns(command);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<IRequiredPackageChecker>());
		return new PackageFileTool(command, Substitute.For<ILogger>(), resolver);
	}

	private sealed class FakePackageFileCommand : ShowPackageFileContentCommand {
		public FakePackageFileCommand() : base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
			Substitute.For<ILogger>()) { }

		public PackageFileListResponse ListResponse { get; init; }
		public PackageFileContentResponse ContentResponse { get; init; }
		public ShowPackageFileContentOptions CapturedOptions { get; private set; }

		public override bool TryListPackageFiles(ShowPackageFileContentOptions options,
			out PackageFileListResponse response) {
			CapturedOptions = options;
			response = ListResponse;
			return response.Success;
		}

		public override bool TryGetPackageFile(ShowPackageFileContentOptions options,
			out PackageFileContentResponse response) {
			CapturedOptions = options;
			response = ContentResponse;
			return response.Success;
		}
	}
}
