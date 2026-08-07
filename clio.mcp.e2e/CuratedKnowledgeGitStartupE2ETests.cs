using System.Diagnostics;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end regression coverage for the bounded curated Git bootstrap that runs before the
/// real <c>mcp-server</c> stdio transport begins consuming requests.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-server")]
[NonParallelizable]
public sealed class CuratedKnowledgeGitStartupE2ETests {
	private const string McpServerVerb = "mcp-server";
	private const string BootstrapWarning = "MCP is starting without built-in curated knowledge:";
	private static readonly TimeSpan StartupDeadline = TimeSpan.FromSeconds(7);
	private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);

	[Test]
	[Description("Starts the real MCP server with a Git process whose descendant retains redirected handles and verifies bounded fallback.")]
	[AllureTag(McpServerVerb)]
	[AllureName("curated Git pipe drain falls back before MCP initialize deadline")]
	[AllureDescription("Places a deterministic fake Git executable first on PATH. The fake Git parent exits after spawning a thirty-second descendant that inherits stdout and stderr, proving the real MCP bootstrap reaches the bounded process-drain path before serving initialize.")]
	public async Task McpServer_ShouldInitializeWithinBudget_WhenGitDescendantRetainsRedirectedHandles() {
		// Arrange
		ArrangeContext context = await ArrangeAsync();

		try {
			// Act
			ActResult result = await ActAsync(context);

			// Assert
			AssertFakeGitWasInvoked(result);
			AssertPipeHoldingDescendantWasStarted(result);
			JsonElement initializeResult = AssertInitializeResult(result);
			AssertInitializeServerInfo(initializeResult);
			AssertStartupWasBounded(result);
			AssertFallbackWarning(result);
			AssertCleanupLimitationWarning(result);
			AssertNormalShutdown(result);
		} finally {
			await CleanupAsync(context);
		}
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		return await AllureApi.Step("Arrange an isolated MCP home and fake Git inherited-pipe fixture", async () => {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, McpServerVerb);
			string clioHome = Path.Combine(Path.GetTempPath(), $"clio-mcp-git-startup-{Guid.NewGuid():N}");
			string knowledgeRoot = Path.Combine(clioHome, "knowledge");
			Directory.CreateDirectory(knowledgeRoot);
			string fixtureDirectory = ResolveFixtureDirectory();
			string invocationMarkerPath = Path.Combine(fixtureDirectory, "invoked.marker");
			string descendantIdentityPath = Path.Combine(fixtureDirectory, "descendant.identity");
			File.Delete(invocationMarkerPath);
			File.Delete(descendantIdentityPath);
			string logPath = Path.Combine(clioHome, "mcp-startup.log");
			await File.WriteAllTextAsync(Path.Combine(clioHome, "appsettings.json"),
				CreateSettings(knowledgeRoot));

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
			startInfo.ArgumentList.Add("--log");
			startInfo.ArgumentList.Add(logPath);
			startInfo.Environment["CLIO_HOME"] = clioHome;
			string inheritedPath = startInfo.Environment.TryGetValue("PATH", out string? path)
				? path ?? string.Empty
				: string.Empty;
			startInfo.Environment["PATH"] = string.IsNullOrEmpty(inheritedPath)
				? fixtureDirectory
				: $"{fixtureDirectory}{Path.PathSeparator}{inheritedPath}";

			return new ArrangeContext(clioHome, logPath, invocationMarkerPath, descendantIdentityPath, startInfo);
		});
	}

	private static async Task<ActResult> ActAsync(ArrangeContext context) {
		return await AllureApi.Step("Act by starting the real MCP server and sending initialize", async () => {
			using CancellationTokenSource responseDeadline = new(ResponseTimeout);
			Process process = new() { StartInfo = context.StartInfo };
			context.Process = process;
			Stopwatch elapsed = Stopwatch.StartNew();
			process.Start().Should().BeTrue(
				because: "the real clio mcp-server process must launch before bootstrap can be tested");
			Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
			await process.StandardInput.WriteLineAsync(
				"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curated-git-startup-e2e","version":"1.0"}}}"""
					.AsMemory(),
				responseDeadline.Token);
			await process.StandardInput.FlushAsync(responseDeadline.Token);
			JsonElement response = await ReadResponseAsync(process, responseDeadline.Token);
			elapsed.Stop();
			process.StandardInput.Close();
			await process.WaitForExitAsync(responseDeadline.Token);
			_ = await standardErrorTask;
			string startupLog = await File.ReadAllTextAsync(context.LogPath, responseDeadline.Token);
			string? invocation = File.Exists(context.InvocationMarkerPath)
				? await File.ReadAllTextAsync(context.InvocationMarkerPath, responseDeadline.Token)
				: null;
			ProcessIdentity? descendantIdentity = File.Exists(context.DescendantIdentityPath)
				? JsonSerializer.Deserialize<ProcessIdentity>(
					await File.ReadAllTextAsync(context.DescendantIdentityPath, responseDeadline.Token))
				: null;
			context.DescendantIdentity = descendantIdentity;
			return new ActResult(response, startupLog, elapsed.Elapsed, process.ExitCode, invocation, descendantIdentity);
		});
	}

	[AllureStep("Assert the real bootstrap invoked the fake Git executable")]
	private static void AssertFakeGitWasInvoked(ActResult result) {
		result.Invocation.Should().NotBeNullOrWhiteSpace(
			because: "the regression must reach Git execution instead of failing earlier during knowledge-root setup");
	}

	[AllureStep("Assert fake Git spawned the inherited-pipe descendant")]
	private static void AssertPipeHoldingDescendantWasStarted(ActResult result) {
		result.DescendantIdentity.Should().NotBeNull(
			because: "the fake Git parent must leave a real descendant holding its redirected handles");
	}

	[AllureStep("Assert MCP initialize returns a result after curated Git fallback")]
	private static JsonElement AssertInitializeResult(ActResult result) {
		result.Response.TryGetProperty("result", out JsonElement initializeResult).Should().BeTrue(
			because: "a timed-out curated Git bootstrap is non-fatal and must still produce an initialize result");
		return initializeResult;
	}

	[AllureStep("Assert MCP initialize identifies the server")]
	private static void AssertInitializeServerInfo(JsonElement initializeResult) {
		initializeResult.TryGetProperty("serverInfo", out _).Should().BeTrue(
			because: "the initialize response proves MCP stdio began serving after fallback");
	}

	[AllureStep("Assert MCP startup remains bounded despite the thirty-second descendant")]
	private static void AssertStartupWasBounded(ActResult result) {
		result.Elapsed.Should().BeLessThan(StartupDeadline,
			because: "the five-second bootstrap budget plus process scheduling must beat the descendant's thirty-second lifetime");
	}

	[AllureStep("Assert the existing non-fatal curated knowledge warning is logged")]
	private static void AssertFallbackWarning(ActResult result) {
		result.StartupLog.Should().Contain(BootstrapWarning,
			because: "operators must be told that MCP started without built-in curated knowledge");
	}

	[AllureStep("Assert fallback warning discloses the portable cleanup limitation")]
	private static void AssertCleanupLimitationWarning(ActResult result) {
		result.StartupLog.Should().Contain("termination of already reparented descendants is not guaranteed",
			because: "the fallback diagnostic must expose the cross-platform cleanup limitation");
	}

	[AllureStep("Assert closing MCP stdin produces a normal shutdown")]
	private static void AssertNormalShutdown(ActResult result) {
		result.ExitCode.Should().Be(0,
			because: "closing stdin after a successful fallback handshake is a normal MCP shutdown");
	}

	private static async Task CleanupAsync(ArrangeContext context) {
		await AllureApi.Step("Clean up MCP and inherited-pipe fixture processes", async () => {
			if (context.Process is not null) {
				await StopProcessAsync(context.Process);
				context.Process.Dispose();
			}
			if (context.DescendantIdentity is null && File.Exists(context.DescendantIdentityPath)) {
				context.DescendantIdentity = JsonSerializer.Deserialize<ProcessIdentity>(
					await File.ReadAllTextAsync(context.DescendantIdentityPath));
			}
			if (context.DescendantIdentity is not null) {
				await TerminateProcessAsync(context.DescendantIdentity);
			}
			File.Delete(context.InvocationMarkerPath);
			File.Delete(context.DescendantIdentityPath);
			TryDeleteDirectory(context.ClioHome);
		});
	}

	private static async Task StopProcessAsync(Process process) {
		try {
			process.StandardInput.Close();
		} catch (InvalidOperationException) {
			// The process did not start or already released redirected input.
		}
		try {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
			}
			using CancellationTokenSource cleanupDeadline = new(TimeSpan.FromSeconds(5));
			await process.WaitForExitAsync(cleanupDeadline.Token);
		} catch (InvalidOperationException) {
			// The process exited between the state check and cleanup call.
		} catch (OperationCanceledException) {
			// Best-effort cleanup must not mask the primary test failure.
		}
	}

	private static async Task TerminateProcessAsync(ProcessIdentity identity) {
		try {
			using Process process = Process.GetProcessById(identity.ProcessId);
			if (process.HasExited || !MatchesIdentity(process, identity)) {
				return;
			}
			process.Kill(entireProcessTree: true);
			using CancellationTokenSource cleanupDeadline = new(TimeSpan.FromSeconds(5));
			await process.WaitForExitAsync(cleanupDeadline.Token);
		} catch (ArgumentException) {
			// The fixture descendant already exited.
		} catch (InvalidOperationException) {
			// The fixture descendant exited while cleanup was in progress.
		} catch (OperationCanceledException) {
			// Best-effort cleanup must not mask the primary test failure.
		}
	}

	private static bool MatchesIdentity(Process process, ProcessIdentity identity) {
		try {
			StringComparison comparison = OperatingSystem.IsWindows()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;
			return process.StartTime.ToUniversalTime().Ticks == identity.StartUtcTicks
				&& string.Equals(Path.GetFullPath(process.MainModule!.FileName),
					Path.GetFullPath(identity.ExecutablePath), comparison);
		} catch (Exception exception) when (exception is InvalidOperationException
				or System.ComponentModel.Win32Exception
				or NotSupportedException) {
			return false;
		}
	}

	private static string ResolveFixtureDirectory() {
		DirectoryInfo testDirectory = new(TestContext.CurrentContext.TestDirectory);
		string targetFramework = testDirectory.Name;
		string configuration = testDirectory.Parent?.Name
			?? throw new InvalidOperationException("The test configuration directory could not be resolved.");
		string repositoryRoot = Path.GetFullPath(Path.Combine(testDirectory.FullName, "..", "..", "..", ".."));
		string fixtureDirectory = Path.Combine(repositoryRoot, "clio.process.fixture", "bin", configuration,
			targetFramework);
		string fixtureExecutable = Path.Combine(fixtureDirectory, OperatingSystem.IsWindows() ? "git.exe" : "git");
		return File.Exists(fixtureExecutable)
			? fixtureDirectory
			: throw new FileNotFoundException("The fake Git process fixture was not built.", fixtureExecutable);
	}

	private static string CreateSettings(string knowledgeRoot) {
		return JsonSerializer.Serialize(new {
			knowledge = new {
				root_path = knowledgeRoot,
				sources = new Dictionary<string, object> {
					["creatio-curated"] = new Dictionary<string, object> {
						["library-id"] = "com.creatio.clio",
						["type"] = "git",
						["location"] = "https://github.com/Advance-Technologies-Foundation/clio-knowledge.git",
						["branch"] = "master",
						["enabled"] = true,
						["priority"] = 100,
						["participation"] = "authoritative"
					}
				}
			}
		}).Replace("root_path", "root-path", StringComparison.Ordinal);
	}

	private static async Task<JsonElement> ReadResponseAsync(Process process, CancellationToken cancellationToken) {
		while (true) {
			string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
			line.Should().NotBeNull(
				because: "mcp-server must not close stdout before answering the initialize request");
			if (string.IsNullOrWhiteSpace(line)) {
				continue;
			}
			try {
				using JsonDocument document = JsonDocument.Parse(line);
				JsonElement message = document.RootElement;
				if (message.TryGetProperty("id", out JsonElement id) && id.GetInt32() == 1) {
					return message.Clone();
				}
			} catch (JsonException) {
				// Skip non-JSON console noise and continue waiting for the initialize response.
			}
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (IOException) {
			// A cleanup failure must not hide the primary assertion failure.
		} catch (UnauthorizedAccessException) {
			// A cleanup failure must not hide the primary assertion failure.
		}
	}

	private sealed class ArrangeContext(string clioHome, string logPath, string invocationMarkerPath,
		string descendantIdentityPath, ProcessStartInfo startInfo) {
		public string ClioHome { get; } = clioHome;

		public string LogPath { get; } = logPath;

		public string InvocationMarkerPath { get; } = invocationMarkerPath;

		public string DescendantIdentityPath { get; } = descendantIdentityPath;

		public ProcessStartInfo StartInfo { get; } = startInfo;

		public Process? Process { get; set; }

		public ProcessIdentity? DescendantIdentity { get; set; }
	}

	private sealed record ActResult(JsonElement Response, string StartupLog, TimeSpan Elapsed, int ExitCode,
		string? Invocation, ProcessIdentity? DescendantIdentity);

	private sealed record ProcessIdentity(int ProcessId, long StartUtcTicks, string ExecutablePath);
}
