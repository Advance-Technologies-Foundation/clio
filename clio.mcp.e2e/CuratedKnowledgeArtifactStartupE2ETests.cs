using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Knowledge;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for curated artifact-cache diagnostics emitted before the real stdio MCP
/// transport begins serving requests.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-server")]
[NonParallelizable]
public sealed class CuratedKnowledgeArtifactStartupE2ETests {
	private const string McpServerVerb = "mcp-server";
	private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

	[Test]
	[Description("A real warm MCP start reports an old curated artifact generation on stderr without corrupting stdout.")]
	[AllureTag(McpServerVerb)]
	[AllureName("stale curated artifact cache is visible on stdio stderr")]
	[AllureDescription("Publishes a synthetic old curated generation into an isolated Clio home, starts the real MCP server, and verifies the initialize response stays valid while stderr names the served version and update command.")]
	public async Task McpServer_ShouldWarnOnStandardError_WhenCuratedArtifactCacheIsStale() {
		// Arrange
		ArrangeContext context = await ArrangeAsync();

		try {
			// Act
			ActResult result = await ActAsync(context);

			// Assert
			AllureApi.Step("Assert stdout remains a valid MCP initialize response", () => {
				result.Response.TryGetProperty("result", out JsonElement initializeResult).Should().BeTrue(
					because: "the staleness diagnostic must not corrupt or delay the JSON-RPC protocol stream");
				initializeResult.TryGetProperty("serverInfo", out _).Should().BeTrue(
					because: "the real stdio host must finish initialization while serving the cached generation");
				result.ExitCode.Should().Be(0,
					because: "closing stdin after a successful handshake is a normal MCP shutdown");
			});
			AllureApi.Step("Assert stderr identifies the stale served generation and remediation", () => {
				result.StandardError.Should().Contain("[WAR]",
					because: "stdio suppresses console logging on stdout, so warnings must use stderr");
				result.StandardError.Should().Contain("library version 1.12.0",
					because: "the operator must see which cached generation the process actually served");
				result.StandardError.Should().Contain("update-knowledge --source creatio-curated",
					because: "the warning must name the exact command that checks the publisher for a newer release");
			});
		} finally {
			Cleanup(context);
		}
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		return await AllureApi.Step("Arrange an isolated stale curated artifact cache", async () => {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			string clioHome = Path.Combine(Path.GetTempPath(), $"clio-mcp-artifact-startup-{Guid.NewGuid():N}");
			string knowledgeRoot = Path.Combine(clioHome, "knowledge");
			Directory.CreateDirectory(clioHome);
			string appSettings = JsonSerializer.Serialize(new Dictionary<string, object?> {
				["knowledge"] = new Dictionary<string, object?> { ["root-path"] = knowledgeRoot }
			});
			await File.WriteAllTextAsync(Path.Combine(clioHome, "appsettings.json"), appSettings);

			IKnowledgeSourceInstallationStore store = new KnowledgeSourceInstallationStore(
				new FixedKnowledgeRootPathProvider(knowledgeRoot),
				new FileSystem(),
				new KnowledgeInstallationStoreOptions(LockTimeoutMilliseconds: 5_000));
			KnowledgeInstallationResult published = store.Publish(new KnowledgeGenerationPublication {
				SourceAlias = CuratedKnowledgeSourceDefaults.Alias,
				LibraryId = CuratedKnowledgeSourceDefaults.LibraryId,
				LibraryVersion = "1.12.0",
				Sequence = 7,
				TransportType = KnowledgeSourceTypeNames.GitHubRelease,
				Location = CuratedKnowledgeSourceDefaults.Location,
				ResolvedRevision = "1.12.0",
				BundleBytes = [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
				IsUpdate = false,
				AllowRepair = false
			});
			published.Status.Should().Be(KnowledgeInstallationStatus.Installed,
				because: "the real warm-start probe requires one materialized activation marker");
			KnowledgeSourceCurrentState current = store.ReadCurrent(
				CuratedKnowledgeSourceDefaults.Alias,
				out string? diagnostic)!;
			diagnostic.Should().BeNull(
				because: "the synthetic activation marker must be valid before it is backdated");
			KnowledgeSourceCurrentState stale = current with {
				Active = current.Active with { ActivatedAtUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(10) }
			};
			string markerPath = Directory.EnumerateFiles(knowledgeRoot, "current.json", SearchOption.AllDirectories).Single();
			await File.WriteAllBytesAsync(markerPath, JsonSerializer.SerializeToUtf8Bytes(
				stale,
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));

			ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, McpServerVerb);
			ProcessStartInfo startInfo = new() {
				FileName = descriptor.Command,
				WorkingDirectory = descriptor.WorkingDirectory,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false
			};
			foreach (string argument in descriptor.Arguments) {
				startInfo.ArgumentList.Add(argument);
			}
			startInfo.Environment["CLIO_HOME"] = clioHome;
			return new ArrangeContext(clioHome, startInfo);
		});
	}

	private static async Task<ActResult> ActAsync(ArrangeContext context) {
		return await AllureApi.Step("Start the real MCP server and send initialize", async () => {
			using CancellationTokenSource deadline = new(ResponseTimeout);
			using Process process = new() { StartInfo = context.StartInfo };
			process.Start().Should().BeTrue(
				because: "the external clio process must launch before its stdio boundary can be verified");
			Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(deadline.Token);
			await process.StandardInput.WriteLineAsync(
				"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curated-artifact-startup-e2e","version":"1.0"}}}"""
					.AsMemory(),
				deadline.Token);
			await process.StandardInput.FlushAsync(deadline.Token);
			string? responseLine = await process.StandardOutput.ReadLineAsync(deadline.Token);
			responseLine.Should().NotBeNullOrWhiteSpace(
				because: "the real MCP process must answer initialize after the offline warm-start probe");
			using JsonDocument responseDocument = JsonDocument.Parse(responseLine!);
			JsonElement response = responseDocument.RootElement.Clone();
			process.StandardInput.Close();
			await process.WaitForExitAsync(deadline.Token);
			string standardError = await standardErrorTask;
			return new ActResult(response, standardError, process.ExitCode);
		});
	}

	private static void Cleanup(ArrangeContext context) {
		try {
			if (Directory.Exists(context.ClioHome)) {
				Directory.Delete(context.ClioHome, recursive: true);
			}
		} catch (IOException) {
			// The process has already exited; a transient scanner handle must not hide the behavior under test.
		} catch (UnauthorizedAccessException) {
			// Best-effort cleanup mirrors the other raw-process MCP fixtures on Windows agents.
		}
	}

	private sealed class FixedKnowledgeRootPathProvider(string rootPath) : IKnowledgeRootPathProvider {
		public string GetOrCreateRoot() => rootPath;
	}

	private sealed record ArrangeContext(string ClioHome, ProcessStartInfo StartInfo);

	private sealed record ActResult(JsonElement Response, string StandardError, int ExitCode);
}
