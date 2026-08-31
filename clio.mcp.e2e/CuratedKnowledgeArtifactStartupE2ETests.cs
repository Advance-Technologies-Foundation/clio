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
public sealed class CuratedKnowledgeArtifactStartupE2ETests
{
    private const string McpServerVerb = "mcp-server";
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

    [Test]
    [Description("A real warm MCP start reports an old curated artifact generation on stderr without corrupting stdout.")]
    [AllureTag(McpServerVerb)]
    [AllureName("stale curated artifact cache is visible on stdio stderr")]
    [AllureDescription("Publishes a synthetic old curated activation marker into an isolated Clio home, starts the real MCP server, and verifies the initialize response stays valid while stderr names the cached candidate and update command.")]
    public async Task McpServer_ShouldWarnOnStandardError_WhenCuratedArtifactCacheIsStale()
    {
        // Arrange
        ArrangeContext context = await ArrangeAsync();

        try
        {
            // Act
            ActResult result = await ActAsync(context);

            // Assert
            AllureApi.Step("Assert stdout contains a valid MCP initialize response", () =>
            {
                result.Response.TryGetProperty("result", out JsonElement initializeResult).Should().BeTrue(
                    because: "the staleness diagnostic must not corrupt or delay the JSON-RPC protocol stream");
            });
            AllureApi.Step("Assert the real MCP server completed initialization", () =>
            {
                JsonElement initializeResult = result.Response.GetProperty("result");
                initializeResult.TryGetProperty("serverInfo", out _).Should().BeTrue(
                    because: "the real stdio host must finish initialization after inspecting the cached marker");
            });
            AllureApi.Step("Assert closing stdin exits the MCP server cleanly", () =>
            {
                result.ExitCode.Should().Be(0,
                    because: "closing stdin after a successful handshake is a normal MCP shutdown");
            });
            AllureApi.Step("Assert the startup warning is emitted on stderr", () =>
            {
                result.StandardError.Should().Contain("[WAR]",
                    because: "stdio suppresses console logging on stdout, so warnings must use stderr");
            });
            AllureApi.Step("Assert stderr identifies the stale cached candidate", () =>
            {
                result.StandardError.Should().Contain("library version 1.12.0",
                    because: "the operator must see which cached generation the activation marker references");
            });
            AllureApi.Step("Assert stderr identifies the remediation command", () =>
            {
                result.StandardError.Should().Contain("update-knowledge --source creatio-curated",
                    because: "the warning must name the exact command that checks the publisher for a newer release");
            });
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static async Task<ArrangeContext> ArrangeAsync()
    {
        return await AllureApi.Step("Arrange an isolated stale curated artifact cache", async () =>
        {
            McpE2ESettings settings = TestConfiguration.Load();
            settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
            string clioHome = Path.Combine(Path.GetTempPath(), $"clio-mcp-artifact-startup-{Guid.NewGuid():N}");
            string knowledgeRoot = Path.Combine(clioHome, "knowledge");
            try
            {
                Directory.CreateDirectory(clioHome);
                string appSettings = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["knowledge"] = new Dictionary<string, object?> { ["root-path"] = knowledgeRoot }
                });
                await File.WriteAllTextAsync(Path.Combine(clioHome, "appsettings.json"), appSettings);

                IFileSystem fileSystem = new FileSystem();
                IKnowledgeSourceInstallationStore store = new KnowledgeSourceInstallationStore(
                    new FixedKnowledgeRootPathProvider(knowledgeRoot),
                    fileSystem,
                    new KnowledgeManagedTreeDeleter(fileSystem),
                    new KnowledgeInstallationStoreOptions(LockTimeoutMilliseconds: 5_000));
                KnowledgeInstallationResult published = store.Publish(new KnowledgeGenerationPublication
                {
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
                AllureApi.Step("Assert the synthetic activation marker was published", () =>
                {
                    published.Status.Should().Be(KnowledgeInstallationStatus.Installed,
                        because: "the real warm-start probe requires one materialized activation marker");
                });
                KnowledgeSourceCurrentState current = store.ReadCurrent(
                    CuratedKnowledgeSourceDefaults.Alias,
                    out string? diagnostic)!;
                AllureApi.Step("Assert the synthetic activation marker is readable", () =>
                {
                    diagnostic.Should().BeNull(
                        because: "the synthetic activation marker must be valid before it is backdated");
                });
                KnowledgeSourceCurrentState stale = current with
                {
                    Active = current.Active with { ActivatedAtUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(10) }
                };
                string markerPath = Directory.EnumerateFiles(knowledgeRoot, "current.json", SearchOption.AllDirectories).Single();
                await File.WriteAllBytesAsync(markerPath, JsonSerializer.SerializeToUtf8Bytes(
                    stale,
                    KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));

                ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, McpServerVerb);
                ProcessStartInfo startInfo = new()
                {
                    FileName = descriptor.Command,
                    WorkingDirectory = descriptor.WorkingDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                foreach (string argument in descriptor.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
                startInfo.Environment["CLIO_HOME"] = clioHome;
                return new ArrangeContext(clioHome, startInfo);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(clioHome))
                    {
                        Directory.Delete(clioHome, recursive: true);
                    }
                }
                catch (Exception cleanupException) when (cleanupException is IOException
                        or UnauthorizedAccessException)
                {
                    // Preserve the arrangement failure; temporary-directory cleanup is best-effort.
                }
                throw;
            }
        });
    }

    private static async Task<ActResult> ActAsync(ArrangeContext context)
    {
        return await AllureApi.Step("Start the real MCP server and send initialize", async () =>
        {
            using CancellationTokenSource deadline = new(ResponseTimeout);
            Process process = new() { StartInfo = context.StartInfo };
            context.Process = process;
            AllureApi.Step("Assert the external Clio process starts", () =>
            {
                process.Start().Should().BeTrue(
                    because: "the external clio process must launch before its stdio boundary can be verified");
            });
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curated-artifact-startup-e2e","version":"1.0"}}}"""
                    .AsMemory(),
                deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            string? responseLine = await process.StandardOutput.ReadLineAsync(deadline.Token);
            AllureApi.Step("Assert the external Clio process answers initialize", () =>
            {
                responseLine.Should().NotBeNullOrWhiteSpace(
                    because: "the real MCP process must answer initialize after the offline warm-start probe");
            });
            using JsonDocument responseDocument = JsonDocument.Parse(responseLine!);
            JsonElement response = responseDocument.RootElement.Clone();
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            string standardError = await standardErrorTask;
            return new ActResult(response, standardError, process.ExitCode);
        });
    }

    private static async Task CleanupAsync(ArrangeContext context)
    {
        try
        {
            if (context.Process is { } process)
            {
                try
                {
                    process.StandardInput.Close();
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        using CancellationTokenSource cleanupDeadline = new(TimeSpan.FromSeconds(5));
                        await process.WaitForExitAsync(cleanupDeadline.Token);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                        or OperationCanceledException
                        or System.ComponentModel.Win32Exception
                        or NotSupportedException)
                {
                    // Cleanup is best-effort and must not replace the assertion that triggered it.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        finally
        {
            if (Directory.Exists(context.ClioHome))
            {
                try
                {
                    Directory.Delete(context.ClioHome, recursive: true);
                }
                catch (IOException)
                {
                    // A transient scanner handle must not hide the behavior under test.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup mirrors the other raw-process MCP fixtures on Windows agents.
                }
            }
        }
    }

    private sealed class FixedKnowledgeRootPathProvider(string rootPath) : IKnowledgeRootPathProvider
    {
        public string GetOrCreateRoot() => rootPath;
    }

    private sealed record ArrangeContext(string ClioHome, ProcessStartInfo StartInfo)
    {
        internal Process? Process { get; set; }
    }

    private sealed record ActResult(JsonElement Response, string StandardError, int ExitCode);
}
