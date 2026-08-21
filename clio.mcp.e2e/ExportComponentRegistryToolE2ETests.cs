using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the export-component-registry MCP tool. The tool is long-tail (like
/// get-classic-page-sources), invoked by its own name through the real MCP server's durable
/// dispatch path rather than being advertised in tools/list. These tests hit the real academy CDN
/// registry (no live Creatio stand required), so they run under the NoEnvironment category.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("export-component-registry")]
[NonParallelizable]
public sealed class ExportComponentRegistryToolE2ETests : McpContractFixtureBase {
	private const string ToolName = ExportComponentRegistryTool.ToolName;

	[Test]
	[Description("Exports the full web component registry for 'latest' to an explicit output-file, with the response carrying no registry content and requiresVersionConfirmation=true.")]
	[AllureTag(ToolName)]
	[AllureName("export-component-registry writes the full registry with no environment")]
	[AllureDescription("Invokes the real clio MCP server's export-component-registry tool with no environment-name/version, verifies the response carries only the output path, counters, and a latest-fallback marker, and that the written file actually contains component entries.")]
	public async Task ExportComponentRegistry_Should_Write_FullRegistry_OnLatestFallback() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string outputDirectory = CreateFixtureDirectory("export-component-registry-latest");
		string outputFile = Path.Combine(outputDirectory, "registry.json");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["output-file"] = outputFile
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ExportComponentRegistryResponse response =
			EntitySchemaStructuredResultParser.Extract<ExportComponentRegistryResponse>(callResult);

		// Assert — the response carries the file path, version markers, and counters, never content
		callResult.IsError.Should().NotBeTrue(because: "the routed MCP call must return a structured payload");
		response.Success.Should().BeTrue(because: $"the export must succeed against the shipped registry. Error: {response.Error}");
		response.OutputFile.Should().Be(outputFile, because: "an explicit absolute output-file must be echoed back");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromLatestFallback,
			because: "with no environment-name/version supplied, the export must honestly report the latest-fallback tier");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "an unknown target version must not be silently assumed by the caller");
		response.VersionWarning.Should().Be(ComponentInfoResolution.LatestFallbackWarning,
			because: "the hard-stop caveat must reach the MCP caller as prose, not only as the boolean flag");
		response.ComponentCount.Should().BeGreaterThan(0,
			because: "the shipped registry carries a non-empty component set");

		// Assert — the registry content lives ONLY in the file, and it actually contains component entries
		File.Exists(outputFile).Should().BeTrue(because: "the registry must be written to disk");
		using JsonDocument written = JsonDocument.Parse(await File.ReadAllTextAsync(outputFile));
		JsonElement components = written.RootElement.ValueKind == JsonValueKind.Array
			? written.RootElement
			: written.RootElement.GetProperty("components");
		components.GetArrayLength().Should().Be(response.ComponentCount,
			because: "the reported componentCount must match exactly what was written to disk");
	}

	[Test]
	[Description("Exports the mobile registry when schema-type=mobile is passed, sourcing a different file than the web export.")]
	[AllureTag(ToolName)]
	[AllureName("export-component-registry honors schema-type=mobile")]
	[AllureDescription("Invokes export-component-registry with schema-type=mobile and verifies the written file differs from the web export and still carries component entries.")]
	public async Task ExportComponentRegistry_Should_Export_MobileRegistry_WhenRequested() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string outputDirectory = CreateFixtureDirectory("export-component-registry-mobile");
		string outputFile = Path.Combine(outputDirectory, "mobile-registry.json");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-type"] = "mobile",
					["output-file"] = outputFile
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ExportComponentRegistryResponse response =
			EntitySchemaStructuredResultParser.Extract<ExportComponentRegistryResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: $"the mobile export must succeed against the shipped registry. Error: {response.Error}");
		File.Exists(outputFile).Should().BeTrue(because: "the mobile registry must be written to disk");
		response.ComponentCount.Should().BeGreaterThan(0, because: "the shipped mobile registry carries components");
	}

	[Test]
	[Description("Rejects an output-file that escapes the workspace and OS temp directory, over the real MCP path, without writing the file.")]
	[AllureTag(ToolName)]
	[AllureName("export-component-registry rejects an out-of-bounds output-file")]
	[AllureDescription("Invokes export-component-registry with an output-file that traverses out of the OS temp directory and verifies the command fails before writing.")]
	public async Task ExportComponentRegistry_Should_Reject_OutputFile_Outside_AllowedZones() {
		// Arrange — a path that resolves well outside both the workspace and the OS temp dir
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string escapingPath = Path.Combine(
			Path.GetTempPath(), "..", "..", "..", "clio-e2e-export-escape", $"registry-{Guid.NewGuid():N}.json");
		string resolvedEscape = Path.GetFullPath(escapingPath);

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["output-file"] = escapingPath
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ExportComponentRegistryResponse response =
			EntitySchemaStructuredResultParser.Extract<ExportComponentRegistryResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a rejected output path is a command-level failure, not an MCP transport failure");
		response.Success.Should().BeFalse(because: "an output-file outside the allowed zones must not be written");
		response.Error.Should().Contain("output-file", because: "the failure must name the offending option");
		File.Exists(resolvedEscape).Should().BeFalse(because: "no file may be written to the out-of-bounds path");
	}

	[Test]
	[Description("Rejects combining version and environment-name before any registry fetch, over the real MCP path.")]
	[AllureTag(ToolName)]
	[AllureName("export-component-registry rejects version+environment-name")]
	[AllureDescription("Invokes export-component-registry with both version and environment-name and verifies the mutual-exclusivity error, matching the CLI verb's validation.")]
	public async Task ExportComponentRegistry_Should_Reject_Version_And_EnvironmentName_Together() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["version"] = "8.3.4",
					["environment-name"] = "dev"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ExportComponentRegistryResponse response =
			EntitySchemaStructuredResultParser.Extract<ExportComponentRegistryResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(because: "version and environment-name are mutually exclusive");
		response.Error.Should().Contain("mutually exclusive",
			because: "the caller must be told which two arguments conflicted, exactly as the CLI verb reports it");
	}
}
