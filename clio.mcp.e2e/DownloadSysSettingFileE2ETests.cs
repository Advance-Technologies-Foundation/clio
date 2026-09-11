using System.Text;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture, Category("E2E"), NonParallelizable, AllureNUnit]
[AllureFeature(DownloadSysSettingFileTool.ToolName)]
public sealed class DownloadSysSettingFileE2ETests : McpContractFixtureBase {
	private sealed record WireResult(
		[property: System.Text.Json.Serialization.JsonPropertyName("exit-code")] int ExitCode,
		[property: System.Text.Json.Serialization.JsonPropertyName("execution-log-messages")] System.Text.Json.JsonElement[] Output,
		[property: System.Text.Json.Serialization.JsonPropertyName("file-name")] string? FileName,
		[property: System.Text.Json.Serialization.JsonPropertyName("byte-count")] long? ByteCount);
	[TestCase("binary")]
	[TestCase("png")]
	[TestCase("txt")]
	[TestCase("json")]
	[TestCase("xml")]
	[Description("Uploads real binary/image/text files and proves exact CLI and clio-run readback with caller-selected extensions.")]
	[AllureTag(DownloadSysSettingFileTool.ToolName), AllureName("Binary file round trip through CLI and clio-run")]
	[AllureDescription("An explicitly opted-in disposable environment receives unique Binary settings; downloads preserve every byte and refuse overwriting.")]
	public async Task Download_ShouldPreserveBytes_WhenBinaryContainsAnyFileType(string kind) {
		// Arrange
		McpE2ESettings settings = SandboxSettings();
		await using var context = Arrange(TimeSpan.FromMinutes(4));
		string directory = CreateFixtureDirectory("binary-download");
		string code = $"UsrDownload{Guid.NewGuid():N}"[..32];
		byte[] bytes = kind switch {
			"png" => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="),
			"txt" => Encoding.UTF8.GetBytes("Hello Ω 日本語\r\nsecond line\n"),
			"json" => Encoding.UTF8.GetBytes("{\"message\":\"日本語\",\"number\":42}\r\n"),
			"xml" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<?xml version=\"1.0\" encoding=\"utf-16\"?><root>Ω</root>\r\n")).ToArray(),
			_ => Enumerable.Range(0, 65536).Select(i => (byte)i).ToArray()
		};
		string source = Path.Combine(directory, "source." + kind);
		await File.WriteAllBytesAsync(source, bytes);
		CallToolResult created = await Run(context, SysSettingCreateTool.CreateSysSettingToolName, new() {
			["environment-name"] = settings.Sandbox.EnvironmentName, ["code"] = code, ["name"] = code,
			["value-type-name"] = "Binary"
		});
		EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(created).Success.Should().BeTrue(
			because: "the download precondition is a real Binary setting");
		CallToolResult uploaded = await Run(context, SysSettingUpdateTool.UpdateSysSettingToolName, new() {
			["environment-name"] = settings.Sandbox.EnvironmentName, ["code"] = code, ["value-file-path"] = source
		});
		EntitySchemaStructuredResultParser.Extract<SysSettingUpdateResult>(uploaded).Success.Should().BeTrue(
			because: "the server must accept the actual source file before verifying readback");
		string cliFile = Path.Combine(directory, "cli.chosen");
		string mcpFile = Path.Combine(directory, "mcp.chosen");

		// Act
		ClioCliCommandResult cli = await ClioCliCommandRunner.RunAsync(settings,
			[DownloadSysSettingFileTool.ToolName, "--code", code, "--file-name", cliFile, "-e", settings.Sandbox.EnvironmentName!]);
		CallToolResult downloaded = await Run(context, DownloadSysSettingFileTool.ToolName, new() {
			["environment-name"] = settings.Sandbox.EnvironmentName, ["code"] = code, ["file-name"] = mcpFile
		});
		WireResult result = EntitySchemaStructuredResultParser.Extract<WireResult>(downloaded);

		// Assert
		AllureApi.Step("CLI returns success", () => cli.ExitCode.Should().Be(0, because: "the real CLI must save the Binary value"));
		AllureApi.Step("CLI saves exact bytes", () => File.ReadAllBytes(cliFile).Should().Equal(bytes, because: "text encoding and binary content are opaque"));
		AllureApi.Step("MCP invocation succeeds", () => downloaded.IsError.Should().NotBeTrue(because: "clio-run must reach the long-tail tool"));
		AllureApi.Step("MCP exit code is zero", () => result.ExitCode.Should().Be(0, because: "the file was published successfully"));
		AllureApi.Step("MCP includes Info", () => result.Output.Should().Contain(m => m.GetProperty("message-type").GetString() == "Info", because: "success must include execution diagnostics"));
		AllureApi.Step("MCP reports path", () => result.FileName.Should().Be(mcpFile, because: "the caller controls the filename"));
		AllureApi.Step("MCP reports bytes", () => result.ByteCount.Should().Be(bytes.Length, because: "metadata counts the exact saved bytes"));
		AllureApi.Step("MCP saves exact bytes", () => File.ReadAllBytes(mcpFile).Should().Equal(bytes, because: "no format inference or conversion is permitted"));
		CallToolResult repeated = await Run(context, DownloadSysSettingFileTool.ToolName, new() {
			["environment-name"] = settings.Sandbox.EnvironmentName, ["code"] = code, ["file-name"] = mcpFile
		});
		WireResult refusal = EntitySchemaStructuredResultParser.Extract<WireResult>(repeated);
		AllureApi.Step("Existing destination is refused", () => refusal.ExitCode.Should().NotBe(0, because: "the tool never overwrites"));
		AllureApi.Step("Refusal includes Error", () => refusal.Output.Should().Contain(m => m.GetProperty("message-type").GetString() == "Error", because: "the refusal must be explicit"));
		AllureApi.Step("Original bytes survive", () => File.ReadAllBytes(mcpFile).Should().Equal(bytes, because: "refusal must preserve the original file"));
		TestContext.Progress.WriteLine($"Verified {kind}: {bytes.Length} bytes; SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
	}

	[TestCase("missing-filename")]
	[TestCase("relative-filename")]
	[TestCase("text-setting")]
	[TestCase("missing-setting")]
	[Description("Invalid file download requests fail through real clio-run without creating a destination.")]
	[AllureTag(DownloadSysSettingFileTool.ToolName), AllureName("Download refusal through clio-run")]
	[AllureDescription("Exercises required filename, absolute MCP path and Binary setting validation over stdio.")]
	public async Task Download_ShouldLeaveNoFile_WhenRequestIsInvalid(string scenario) {
		// Arrange
		McpE2ESettings settings = SandboxSettings();
		await using var context = Arrange();
		string directory = CreateFixtureDirectory("download-refusal");
		string destination = Path.Combine(directory, "refused.bin");
		Dictionary<string, object?> args = new() {
			["environment-name"] = settings.Sandbox.EnvironmentName,
			["code"] = scenario == "missing-setting" ? "UsrMissing" + Guid.NewGuid().ToString("N") : "Maintainer"
		};
		if (scenario != "missing-filename") {
			args["file-name"] = scenario == "relative-filename" ? "relative.bin" : destination;
		}
		// Act
		CallToolResult call = await Run(context, DownloadSysSettingFileTool.ToolName, args);
		// Assert
		string text = string.Join(" ", call.Content.OfType<TextContentBlock>().Select(block => block.Text));
		AllureApi.Step("Failure has readable diagnostics", () => text.Should().NotBeNullOrWhiteSpace(because: "callers need a reason for refusal"));
		if (call.IsError != true) {
			WireResult result = EntitySchemaStructuredResultParser.Extract<WireResult>(call);
			AllureApi.Step("Command is refused", () => result.ExitCode.Should().NotBe(0, because: "invalid inputs must never produce success"));
			AllureApi.Step("Error log is present", () => result.Output.Should().Contain(m => m.GetProperty("message-type").GetString() == "Error", because: "refusal requires an error diagnostic"));
		}
		AllureApi.Step("No output exists", () => Directory.GetFiles(directory).Should().BeEmpty(because: "refusals must not leave partial files"));
	}

	private static McpE2ESettings SandboxSettings() {
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Enable destructive MCP tests and select an exclusive disposable sandbox for Binary upload/readback.");
		}
		if (string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Fail("McpE2E__Sandbox__EnvironmentName is required for opted-in Binary readback tests.");
		}
		return settings;
	}

	private static Task<CallToolResult> Run(ArrangeContext context, string tool, Dictionary<string, object?> args) =>
		context.Session.CallToolAsync(ClioRunTool.ToolName, new Dictionary<string, object?> {
			["command"] = tool, ["args"] = args
		}, context.CancellationTokenSource.Token);
}

