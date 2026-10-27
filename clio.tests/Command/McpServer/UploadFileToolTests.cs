using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
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
public class UploadFileToolTests {

	private const string FilePath = "C:/docs/contract.pdf";
	private const string ParentId = "11111111-2222-3333-4444-555555555555";
	private static readonly Guid UploadedFileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

	private static UploadFileArgs ValidArgs() => new(
		EnvironmentName: "docker_fix2", File: FilePath, Entity: "ContactFile", ParentColumn: "Contact",
		ParentId: ParentId);

	private static (UploadFileTool Tool, FakeUploadFileCommand Default, FakeUploadFileCommand Resolved,
		IToolCommandResolver Resolver) BuildTool(RecordFileUploadResult result = null) {
		FakeUploadFileCommand defaultCommand = new();
		FakeUploadFileCommand resolvedCommand = new(result);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UploadFileCommand>(Arg.Any<UploadFileOptions>()).Returns(resolvedCommand);
		return (new UploadFileTool(defaultCommand, ConsoleLogger.Instance, commandResolver), defaultCommand,
			resolvedCommand, commandResolver);
	}

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the upload-file tool method: a non-destructive write (every call creates a new file record), non-idempotent, closed-world.")]
	public void UploadFileTool_ShouldDeclareAdditiveWriteSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(UploadFileTool)
			.GetMethod(nameof(UploadFileTool.UploadFile))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(UploadFileTool.ToolName,
			because: "the tool must be published under its canonical kebab-case name");
		attribute.Name.Should().Be("upload-file", because: "the MCP name matches the CLI verb");
		attribute.ReadOnly.Should().BeFalse(because: "uploading a file writes a file record");
		attribute.Destructive.Should().BeFalse(
			because: "the upload is additive-only and never overwrites existing data");
		attribute.Idempotent.Should().BeFalse(
			because: "repeating the call creates another file record with a new id");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level.")]
	public void UploadFileTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(UploadFileTool)
			.GetMethod(nameof(UploadFileTool.UploadFile))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "an omitted args object must fail with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-scoped upload-file command with the mapped options and returns the created file id.")]
	public void UploadFile_ShouldResolveCommandAndReturnFileId() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		(UploadFileTool tool, FakeUploadFileCommand defaultCommand, FakeUploadFileCommand resolvedCommand,
			IToolCommandResolver commandResolver) = BuildTool(RecordFileUploadResult.Successful(UploadedFileId));

		// Act
		UploadFileResult result = tool.UploadFile(ValidArgs());

		// Assert
		result.Success.Should().BeTrue(because: "a verified upload must report success");
		result.FileId.Should().Be(UploadedFileId.ToString(), because: "the created file id is what the caller needs");
		result.Error.Should().BeNull(because: "a success carries no failure text");
		commandResolver.Received(1).Resolve<UploadFileCommand>(Arg.Is<UploadFileOptions>(options =>
			options.Environment == "docker_fix2"
			&& options.File == FilePath
			&& options.Entity == "ContactFile"
			&& options.ParentColumn == "Contact"
			&& options.ParentId == ParentId));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should perform the upload");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path must not use the startup-time command");
		ConsoleLogger.Instance.ClearMessages();
	}

	[TestCase(null, FilePath, "ContactFile", "Contact", ParentId, "environment-name")]
	[TestCase("docker_fix2", null, "ContactFile", "Contact", ParentId, "file")]
	[TestCase("docker_fix2", FilePath, "", "Contact", ParentId, "entity")]
	[TestCase("docker_fix2", FilePath, "ContactFile", " ", ParentId, "parent-column")]
	[TestCase("docker_fix2", FilePath, "ContactFile", "Contact", null, "parent-id")]
	[Category("Unit")]
	[Description("Returns a structured failure naming the missing required field, without resolving a command.")]
	public void UploadFile_ShouldReturnFailure_WhenRequiredFieldIsMissing(string environmentName, string file,
		string entity, string parentColumn, string parentId, string expectedField) {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		(UploadFileTool tool, _, _, IToolCommandResolver commandResolver) = BuildTool();

		// Act
		UploadFileResult result = tool.UploadFile(new UploadFileArgs(environmentName, file, entity, parentColumn,
			parentId));

		// Assert
		result.Success.Should().BeFalse(because: "a request without a required field is invalid");
		result.Error.Should().StartWith($"{expectedField} is required",
			because: "the failure must name the exact kebab-case field the caller has to add");
		result.FileId.Should().BeNull(because: "nothing was uploaded");
		commandResolver.DidNotReceive().Resolve<UploadFileCommand>(Arg.Any<UploadFileOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[TestCase("parentId", "parent-id")]
	[TestCase("parent_column", "parent-column")]
	[TestCase("environmentName", "environment-name")]
	[Category("Unit")]
	[Description("Rejects a legacy camelCase or snake_case argument with a rename hint to the kebab-case name, without resolving a command.")]
	public void UploadFile_ShouldReturnRenameHint_WhenLegacyAliasIsUsed(string alias, string canonical) {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		(UploadFileTool tool, _, _, IToolCommandResolver commandResolver) = BuildTool();
		UploadFileArgs args = JsonSerializer.Deserialize<UploadFileArgs>(
			$$"""{"environment-name":"docker_fix2","file":"{{FilePath}}","entity":"ContactFile","parent-column":"Contact","parent-id":"{{ParentId}}","{{alias}}":"x"}""")!;

		// Act
		UploadFileResult result = tool.UploadFile(args);

		// Assert
		result.Success.Should().BeFalse(because: "a legacy spelling must not be silently dropped");
		result.Error.Should().Contain($"'{alias}' -> '{canonical}'",
			because: "the hint must name the canonical kebab-case argument");
		commandResolver.DidNotReceive().Resolve<UploadFileCommand>(Arg.Any<UploadFileOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Carries the file id on a failure after the file was sent, so the caller inspects the record instead of uploading again.")]
	public void UploadFile_ShouldReturnFileId_WhenCommandFailsAfterUploadWasSent() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		(UploadFileTool tool, _, _, _) = BuildTool(RecordFileUploadResult.Failure(
			$"File upload was not confirmed: the file API returned HTTP 502. The file record {UploadedFileId} can exist.",
			UploadedFileId));

		// Act
		UploadFileResult result = tool.UploadFile(ValidArgs());

		// Assert
		result.Success.Should().BeFalse(because: "an unconfirmed upload must not report success");
		result.FileId.Should().Be(UploadedFileId.ToString(), because: "the caller needs the id of the record to inspect");
		result.Error.Should().Contain("not confirmed", because: "the command's reason must reach the caller");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Omits the file id when the command fails before anything was sent.")]
	public void UploadFile_ShouldOmitFileId_WhenCommandFailsBeforeUpload() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		(UploadFileTool tool, _, _, _) = BuildTool(RecordFileUploadResult.Failure("File not found: 'C:/docs/contract.pdf'."));

		// Act
		UploadFileResult result = tool.UploadFile(ValidArgs());

		// Assert
		result.Success.Should().BeFalse(because: "a failed upload must not report success");
		result.FileId.Should().BeNull(because: "no file record was created");
		result.Error.Should().Contain("File not found", because: "the command's reason must reach the caller");
		JsonSerializer.Serialize(result).Should().NotContain("file-id",
			because: "a null file id is omitted from the wire result");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts a full request URI with embedded credentials out of the command's failure message while keeping the reason.")]
	public void UploadFile_ShouldRedactSensitiveErrorText_WhenCommandFailsWithSensitiveMessage() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string sensitiveMessage =
			"File upload failed: POST https://admin:s3cr3t@stand.creatio.com/0/rest/FileApiService/UploadFile returned 500.";
		(UploadFileTool tool, _, _, _) = BuildTool(RecordFileUploadResult.Failure(sensitiveMessage));

		// Act
		UploadFileResult result = tool.UploadFile(ValidArgs());

		// Assert
		result.Success.Should().BeFalse(because: "a failed upload must not report success");
		result.Error.Should().NotContain("s3cr3t",
			because: "the credential embedded in the request URI must never reach the MCP transcript");
		result.Error.Should().NotContain("stand.creatio.com", because: "the target host must be scrubbed");
		result.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI must be replaced by the stable redaction placeholder");
		result.Error.Should().Contain("File upload failed",
			because: "the human-readable reason must survive redaction");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeUploadFileCommand : UploadFileCommand {
		private readonly RecordFileUploadResult _result;

		public UploadFileOptions CapturedOptions { get; private set; }

		public FakeUploadFileCommand(RecordFileUploadResult result = null)
			: base(new EnvironmentSettings(), Substitute.For<IRecordFileUploader>()) {
			_result = result ?? RecordFileUploadResult.Successful(UploadedFileId);
		}

		public override RecordFileUploadResult UploadFile(UploadFileOptions options) {
			CapturedOptions = options;
			return _result;
		}
	}
}
