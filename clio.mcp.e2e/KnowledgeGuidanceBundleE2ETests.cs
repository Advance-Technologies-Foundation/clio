using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("external-knowledge-bundle")]
[NonParallelizable]
public sealed class KnowledgeGuidanceActiveBundleE2ETests : McpContractFixtureBase {
	private const string SyntheticName = "esq-filters";
	private const string SyntheticUri = "docs://mcp/guides/esq-filters";
	private const string SyntheticText = "Synthetic delivery fixture.\n";
	private readonly SyntheticKnowledgeBundle _bundle =
		SyntheticKnowledgeBundle.Create(SyntheticName, SyntheticUri, SyntheticText);

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_bundle.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleActivator.BundlePathVariable] = _bundle.BundlePath;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = _bundle.KeyId;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] =
			_bundle.PublicKeyPath;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("synthetic verified bytes flow through get-guidance and docs routing")]
	[Description("Starts the real Clio MCP process and proves stable tool and docs routing with a generated signed fixture that contains no product guidance.")]
	public async Task GuidanceSurfaces_ShouldReturnSyntheticPayload_WhenVerifiedBundleIsConfigured() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["name"] = SyntheticName }
			},
			context.CancellationTokenSource.Token);
		GuidanceGetResponse toolResponse = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
		ReadResourceResult resourceResult = await context.Session.ReadResourceAsync(
			EsqFiltersGuidanceResource.ResourceUri,
			context.CancellationTokenSource.Token);
		TextResourceContents resource = resourceResult.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the stable docs URI must return one text article from the active bundle").Which;

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a compatible verified synthetic bundle must be served");
		toolResponse.Success.Should().BeTrue(because: "get-guidance must resolve the active synthetic article");
		toolResponse.Article!.Name.Should().Be(SyntheticName,
			because: "the stable external identifier must survive the real MCP tool path");
		toolResponse.Article.Uri.Should().Be(SyntheticUri,
			because: "the stable external URI must survive the real MCP tool path");
		toolResponse.Article.Text.Should().Be(SyntheticText,
			because: "the tool path must preserve verified synthetic payload bytes");
		resource.Uri.Should().Be(SyntheticUri,
			because: "the direct docs path must preserve the same stable resource identity");
		resource.Text.Should().Be(SyntheticText,
			because: "the direct docs path must preserve the same verified synthetic payload");
	}
}

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("external-knowledge-bundle")]
[NonParallelizable]
public sealed class KnowledgeGuidanceColdBundleE2ETests : McpContractFixtureBase {
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleActivator.BundlePathVariable] = null;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = null;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] = null;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("cold external guidance is typed unavailable while executable safety remains discoverable")]
	[Description("Starts the real Clio MCP process without a bundle and proves guidance fails closed without weakening executable destructive metadata.")]
	public async Task ColdBundle_ShouldReturnTypedUnavailable_AndRetainExecutableSafetyMetadata() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["name"] = "esq-filters" }
			},
			context.CancellationTokenSource.Token);
		GuidanceGetResponse response = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
		IReadOnlyList<ToolContractIndexEntry> contracts = await context.Session.GetToolContractIndexAsync(
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "typed unavailability is a normal tool result rather than a process crash");
		response.Success.Should().BeFalse(because: "cold guidance cannot be treated as success");
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "agents must distinguish cold guidance from an unknown guide");
		response.Article.Should().BeNull(because: "cold guidance must never become permissive empty content");
		ToolContractIndexEntry destructiveExecutor = contracts.Should().ContainSingle(
			entry => entry.Name == ClioRunDestructiveTool.ToolName,
			because: "the executable destructive dispatch boundary must remain discoverable without guidance").Which;
		destructiveExecutor.Destructive.Should().BeTrue(
			because: "guidance availability must not control the host-enforced destructive safety flag");
	}
}

internal sealed class SyntheticKnowledgeBundle : IDisposable {
	private SyntheticKnowledgeBundle(string root, string bundlePath, string publicKeyPath, string keyId) {
		Root = root;
		BundlePath = bundlePath;
		PublicKeyPath = publicKeyPath;
		KeyId = keyId;
	}

	internal string Root { get; }
	internal string BundlePath { get; }
	internal string PublicKeyPath { get; }
	internal string KeyId { get; }

	internal static SyntheticKnowledgeBundle Create(string name, string uri, string text) {
		string root = Path.Combine(Path.GetTempPath(), $"clio-knowledge-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		string bundlePath = Path.Combine(root, "synthetic.zip");
		string publicKeyPath = Path.Combine(root, "synthetic-public.pem");
		const string keyId = "synthetic-test-key";
		using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		File.WriteAllText(publicKeyPath, signingKey.ExportSubjectPublicKeyInfoPem());
		IReadOnlyDictionary<string, string> catalog = GuidanceCatalog.GetExternalResourceUris();
		if (!catalog.TryGetValue(name, out string? expectedUri)
				|| !string.Equals(expectedUri, uri, StringComparison.Ordinal)) {
			throw new InvalidOperationException("The selected synthetic article must use a stable external catalog route.");
		}
		SyntheticResource[] resources = catalog
			.Select((entry, index) => {
				string resourceText = string.Equals(entry.Key, name, StringComparison.Ordinal)
					? text
					: $"Synthetic delivery fixture {index}.\n";
				byte[] bytes = new UTF8Encoding(false, true).GetBytes(resourceText);
				return new SyntheticResource(
					entry.Key,
					entry.Value,
					$"resources/synthetic-{index}.txt",
					bytes,
					Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
			})
			.ToArray();
		byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new {
			contractVersion = "0.1.0",
			bundleSchemaVersion = "0.1.0",
			sequence = 1,
			bundleVersion = "1.0.0-synthetic",
			issuedAt = "2026-07-18T00:00:00Z",
			source = new { repository = "synthetic-test", commit = "0123456" },
			compatibility = new {
				clio = new { min = "0.0.0", max = "99.99.99" },
				mcpToolContract = new { min = "1.0.0", max = "1.0.0" }
			},
			requirements = new {
				tools = new[] { GuidanceGetTool.ToolName },
				guidanceIds = resources.Select(resource => resource.Name).ToArray(),
				resourceUris = resources.Select(resource => resource.Uri).ToArray()
			},
			digestAlg = "SHA-256",
			signature = new { algorithm = "ECDSA-P256-SHA256", keyId },
			resources = resources.Select(resource =>
				new {
					id = resource.Name,
					uri = resource.Uri,
					path = resource.Path,
					mediaType = "text/plain",
					length = resource.Bytes.LongLength,
					digest = resource.Digest
				})
		});
		using (FileStream output = File.Create(bundlePath))
		using (ZipArchive archive = new(output, ZipArchiveMode.Create)) {
			WriteEntry(archive, "manifest.json", manifestBytes);
			WriteEntry(archive, "manifest.sig", signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256));
			foreach (SyntheticResource resource in resources) {
				WriteEntry(archive, resource.Path, resource.Bytes);
			}
		}
		return new SyntheticKnowledgeBundle(root, bundlePath, publicKeyPath, keyId);
	}

	public void Dispose() {
		if (Directory.Exists(Root)) {
			Directory.Delete(Root, recursive: true);
		}
	}

	private static void WriteEntry(ZipArchive archive, string path, byte[] bytes) {
		ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
		using Stream stream = entry.Open();
		stream.Write(bytes);
	}

	private sealed record SyntheticResource(string Name, string Uri, string Path, byte[] Bytes, string Digest);
}
