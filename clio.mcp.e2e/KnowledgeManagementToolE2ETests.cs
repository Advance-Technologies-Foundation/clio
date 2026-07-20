using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using System.Security.Cryptography;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end contract coverage for non-resident knowledge source management.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(KnowledgeManagementTools.AddKnowledgeSourceToolName)]
[NonParallelizable]
public sealed class KnowledgeManagementToolE2ETests : McpContractFixtureBase {
	private string _clioHome = null!;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_clioHome = CreateIsolatedClioHome("""
			{
			  "knowledge": {
			    "sources": {
			      "creatio-curated": {
			        "library-id": "com.creatio.clio",
			        "type": "git",
			        "location": "https://github.com/Advance-Technologies-Foundation/clio-knowledge.git",
			        "branch": "master",
			        "enabled": false,
			        "priority": 100,
			        "participation": "authoritative"
			      }
			    },
			    "topic-pins": {}
			  }
			}
			""", "knowledge-source-management");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _clioHome;
	}

	[Test]
	[AllureTag(KnowledgeManagementTools.InfoKnowledgeToolName)]
	[AllureName("MCP startup preserves the curated source kill switch")]
	[AllureDescription("Starts the real Clio MCP server with a disabled built-in source and verifies bootstrap preserves the canonical configuration without contacting Git or asserting external guidance content.")]
	[Description("Preserves the disabled curated source through the real MCP startup path.")]
	public async Task McpStartup_ShouldPreserveCuratedKillSwitch_WhenSourceIsDisabled() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.InfoKnowledgeToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["source"] = "creatio-curated",
					["checkUpdates"] = false
				}
			},
			context.CancellationTokenSource.Token);
		string serializedResult = JsonSerializer.Serialize(callResult);
		using JsonDocument response = JsonDocument.Parse(serializedResult);
		string responseText = response.RootElement.GetProperty("content")[0]
			.GetProperty("text").GetString() ?? string.Empty;
		using JsonDocument settings = JsonDocument.Parse(
			File.ReadAllText(Path.Combine(_clioHome, "appsettings.json")));
		JsonElement source = settings.RootElement
			.GetProperty("knowledge")
			.GetProperty("sources")
			.GetProperty("creatio-curated");

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"the real local-only info path should see the checkout installed during startup: {serializedResult}");
		responseText.Should().Contain("\"enabled\":false",
			because: "the local-only info path must expose the operator's durable bootstrap opt-out");
		source.GetProperty("library-id").GetString().Should().Be("com.creatio.clio",
			because: "the built-in source is bound to the stable curated library identity");
		source.GetProperty("location").GetString().Should().Be(
			"https://github.com/Advance-Technologies-Foundation/clio-knowledge.git",
			because: "bootstrap must persist the supported public Git origin rather than an implicit transport");
		source.GetProperty("branch").GetString().Should().Be("master",
			because: "the unpinned curated source follows the supported master branch");
		source.GetProperty("enabled").GetBoolean().Should().BeFalse(
			because: "MCP bootstrap must never silently re-enable an explicitly disabled built-in source");
	}

	[Test]
	[AllureTag(KnowledgeManagementTools.RemoveKnowledgeSourceToolName)]
	[AllureName("remove-knowledge-source refuses the built-in curated source")]
	[AllureDescription("Invokes remove-knowledge-source through the real lazy MCP dispatch path and verifies the built-in source remains configured with disable as the supported alternative.")]
	[Description("Refuses removal of the built-in curated source through the real MCP tool path.")]
	public async Task RemoveKnowledgeSource_ShouldRefuseBuiltInCuratedSource() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.RemoveKnowledgeSourceToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["alias"] = "creatio-curated",
					["confirmed"] = true
				}
			},
			context.CancellationTokenSource.Token);
		string serializedResult = JsonSerializer.Serialize(callResult);
		string persisted = File.ReadAllText(Path.Combine(_clioHome, "appsettings.json"));

		// Assert
		serializedResult.Should().Contain("cannot be removed",
			because: "the real MCP command result must explain the built-in source invariant");
		serializedResult.Should().Contain("disable-knowledge-source --alias creatio-curated",
			because: "agents need the exact valid kill-switch command in the refusal response");
		persisted.Should().Contain("creatio-curated",
			because: "a refused removal must leave the canonical source configuration intact");
	}

	[Test]
	[AllureTag(KnowledgeManagementTools.AddKnowledgeSourceToolName)]
	[AllureName("add-knowledge-source is discoverable through the lazy MCP surface")]
	[AllureDescription("Starts the real Clio MCP server and verifies that add-knowledge-source is discoverable for clio-run dispatch without becoming a resident tool.")]
	[Description("Discovers add-knowledge-source through the real lazy MCP tool-contract index.")]
	public async Task AddKnowledgeSource_ShouldBeDiscoverable_OnLazySurface() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames = await context.Session.ListReachableToolNamesAsync(
			context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(KnowledgeManagementTools.AddKnowledgeSourceToolName,
			because: "agents must be able to discover the non-resident source-management tool before clio-run dispatch");
	}

	[Test]
	[AllureTag(KnowledgeManagementTools.AddKnowledgeSourceToolName)]
	[AllureName("add-knowledge-source persists publisher-specific signing trust")]
	[AllureDescription("Invokes add-knowledge-source through the real MCP server and verifies the isolated appsettings file contains the supplied signing key ID and absolute public-key path.")]
	[Description("Persists required per-source signing trust through the real MCP add-knowledge-source tool.")]
	public async Task AddKnowledgeSource_ShouldPersistSigningTrust_WhenArgumentsAreValid() {
		// Arrange
		await using ArrangeContext context = Arrange();
		string publicKeyPath = Path.Combine(_clioHome, "keys", "partner-public.pem");
		Directory.CreateDirectory(Path.GetDirectoryName(publicKeyPath)!);
		using (ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256)) {
			File.WriteAllText(publicKeyPath, key.ExportSubjectPublicKeyInfoPem());
		}

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.AddKnowledgeSourceToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["alias"] = "partner",
					["libraryId"] = "com.example.partner",
					["type"] = "nuget",
					["location"] = "https://packages.example.test/v3/index.json",
					["trustedKeyId"] = "partner-signing-2026",
					["trustedPublicKeyPath"] = publicKeyPath,
					["packageId"] = "Example.Partner.Knowledge",
					["confirmed"] = true
				}
			},
			context.CancellationTokenSource.Token);
		string serializedResult = JsonSerializer.Serialize(callResult);
		string persisted = File.ReadAllText(Path.Combine(_clioHome, "appsettings.json"));
		using JsonDocument settings = JsonDocument.Parse(persisted);
		JsonElement source = settings.RootElement
			.GetProperty("knowledge")
			.GetProperty("sources")
			.GetProperty("partner");

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"a complete source trust contract should bind and execute through the real MCP server: {serializedResult}");
		serializedResult.Should().Contain("partner",
			because: "the structured MCP result should identify the source affected by the successful operation");
		source.GetProperty("trusted-key-id").GetString().Should().Be("partner-signing-2026",
			because: "the publisher-specific manifest key ID is part of the persisted source trust boundary");
		source.GetProperty("trusted-public-key-path").GetString().Should().Be(publicKeyPath,
			because: "the exact absolute local public-key path must survive MCP binding and settings persistence");
	}

	[Test]
	[AllureTag(KnowledgeManagementTools.AddKnowledgeSourceToolName)]
	[AllureName("add-knowledge-source requires explicit trust confirmation")]
	[AllureDescription("Invokes add-knowledge-source without confirmation and verifies the real MCP path does not persist the trust root.")]
	[Description("Refuses an unconfirmed publisher trust root through the real MCP tool.")]
	public async Task AddKnowledgeSource_ShouldNotPersist_WhenConfirmationIsMissing() {
		// Arrange
		await using ArrangeContext context = Arrange();
		string publicKeyPath = Path.Combine(_clioHome, "keys", "unconfirmed-public.pem");

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.AddKnowledgeSourceToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["alias"] = "unconfirmed",
					["libraryId"] = "com.example.unconfirmed",
					["type"] = "nuget",
					["location"] = "https://packages.example.test/v3/index.json",
					["trustedKeyId"] = "unconfirmed-signing-2026",
					["trustedPublicKeyPath"] = publicKeyPath,
					["packageId"] = "Example.Unconfirmed.Knowledge",
					["confirmed"] = false
				}
			},
			context.CancellationTokenSource.Token);
		string serializedResult = JsonSerializer.Serialize(callResult);
		string persisted = File.ReadAllText(Path.Combine(_clioHome, "appsettings.json"));

		// Assert
		serializedResult.Should().Contain("requires explicit confirmation",
			because: "the real lazy-dispatch result must explain the trust-boundary confirmation requirement");
		persisted.Should().NotContain("com.example.unconfirmed",
			because: "an unconfirmed publisher key must not be written to settings");
	}
}
