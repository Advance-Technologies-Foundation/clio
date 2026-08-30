using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>diagnostics</c> field on <c>get-guidance</c>. Unit tests assert the CLR
/// property; only the real server proves what ships on the wire — that the field carries the neutralized form
/// rather than raw text from the configured repository, and that it never names the knowledge cache path.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GuidanceGetTool.ToolName)]
[NonParallelizable]
public sealed class GuidanceGetDiagnosticsE2ETests : McpContractFixtureBase {

	// sha256("probe")[..24] - how KnowledgeSourceInstallationStore.SourceKey derives a source directory.
	private const string ProbeSourceKey = "ba9c736f19e7f60b7f6764ad";
	private const string UntrustedMarker = "[untrusted-source-text begin]";

	private string _clioHome = null!;

	// The Git source is ENABLED and its checkout is materialized but unreadable, which is what makes
	// activation actually try to read it and record a reason. A merely-absent checkout is skipped silently
	// and would leave `diagnostics` absent, making every assertion below vacuous. `creatio-curated` stays
	// disabled so the run is offline and no other source can satisfy the lookup.
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_clioHome = CreateIsolatedClioHome(
			"""
			{
			  "Autoupdate": false,
			  "knowledge": {
			    "sources": {
			      "creatio-curated": {
			        "library-id": "com.creatio.clio",
			        "type": "github-release",
			        "location": "https://api.github.com/",
			        "repository-owner": "Advance-Technologies-Foundation",
			        "repository-name": "clio-knowledge",
			        "asset-name": "clio-knowledge-bundle.zip",
			        "enabled": false,
			        "priority": 100,
			        "participation": "authoritative"
			      },
			      "probe": {
			        "library-id": "com.example.probe",
			        "type": "git",
			        "location": "http://127.0.0.1:9/probe.git",
			        "branch": "main",
			        "enabled": true,
			        "priority": 200,
			        "participation": "authoritative"
			      }
			    }
			  }
			}
			""",
			"guidance-diagnostics-home");
		SeedUnreadableCheckout(_clioHome);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _clioHome;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance ships the activation reason neutralized, never raw")]
	[AllureDescription("Starts the real MCP server against a knowledge source that cannot activate and inspects the raw get-guidance JSON, so the neutralization and the absence of cache paths are both proven on the wire.")]
	[Description("Verifies the diagnostics field reaches the client marked as untrusted data, single-line, and free of the knowledge cache path.")]
	public async Task GuidanceGet_ShouldShipANeutralizedDiagnostic_WhenNoBundleIsActive() {
		// Arrange
		ArrangeContext context = null!;
		await AllureApi.Step("Arrange an MCP server with an unreadable Git knowledge checkout", async () => {
			context = Arrange(TimeSpan.FromMinutes(3));
			await Task.CompletedTask;
		});
		await using (context) {

			// Act
			CallToolResult callResult = await AllureApi.Step("Invoke get-guidance through the real MCP server", async () =>
				await context.Session.CallToolAsync(
					GuidanceGetTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> { ["name"] = "routing" }
					},
					context.CancellationTokenSource.Token));
			using JsonDocument document = JsonDocument.Parse(ExtractRawText(callResult));

			// Assert
			AllureApi.Step("Assert the inactive bundle is a structured tool failure", () => {
				callResult.IsError.Should().NotBeTrue(
					because: "an inactive knowledge bundle is a structured failure, not a protocol-level error");
				document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse(
					because: "no verified bundle can be active when the only enabled source cannot be read");
			});
			JsonElement diagnostics = default;
			AllureApi.Step("Assert the wire response contains a string diagnostic", () => {
				document.RootElement.TryGetProperty("diagnostics", out diagnostics).Should().BeTrue(
					because: "the activation reason must be observable when guidance is unavailable");
				diagnostics.ValueKind.Should().Be(JsonValueKind.String,
					because: "WhenWritingNull must omit the field rather than serialize a null");
			});
			string text = diagnostics.GetString()!;
			AllureApi.Step("Assert the diagnostic is fenced as untrusted data", () =>
				text.Should().StartWith(UntrustedMarker,
					because: "repository-authored text must not read as server-authored instructions"));
			AllureApi.Step("Assert the diagnostic does not disclose the cache path", () =>
				text.Should().NotContain(_clioHome,
					because: "an absolute cache path carries the OS account name into third-party transcripts"));
			AllureApi.Step("Assert the diagnostic is a single line", () =>
				text.Should().NotContain("\n").And.NotContain("\r",
					because: "a repository must not forge a separate rendered message block"));
		}
	}

	// A source root that exists and is owned but holds no usable Git checkout: activation reads it, fails,
	// and records why. Written directly rather than through the CLI so the fixture stays offline.
	private static void SeedUnreadableCheckout(string clioHome) {
		string knowledgeRoot = Path.Combine(clioHome, "knowledge");
		string sourceRoot = Path.Combine(knowledgeRoot, "sources", ProbeSourceKey);
		Directory.CreateDirectory(Path.Combine(sourceRoot, "repository"));
		File.WriteAllText(Path.Combine(knowledgeRoot, ".clio-knowledge-root"), "clio-knowledge-store-v1\n");
		File.WriteAllText(Path.Combine(sourceRoot, ".clio-knowledge-source"), "probe\n");
	}

	private static string ExtractRawText(CallToolResult callResult) {
		foreach (ContentBlock block in callResult.Content) {
			if (block is TextContentBlock text) {
				return text.Text;
			}
		}
		throw new InvalidOperationException("get-guidance returned no text content.");
	}
}
