using System.Net;
using System.Text;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>execute-esq</c> MCP response-size boundary.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqResponseSizeToolE2ETests {

	[Test]
	[Description("Runs execute-esq through clio-run against an oversized DataService response and verifies the structured result-too-large envelope plus continued MCP session usability.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq rejects oversized responses without closing the MCP session")]
	[AllureDescription("Streams an 84 MB chunked DataService response through the real clio MCP process, verifies a bounded result-too-large envelope, and proves the same MCP session remains usable.")]
	public async Task ClioRun_ShouldKeepSessionUsable_WhenExecuteEsqResponseExceedsByteBudget() {
		// Arrange
		await using OversizedSelectQueryStub creatioStub = OversizedSelectQueryStub.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-execute-esq-size-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "oversized-esq-e2e",
			  "Environments": {
			    "oversized-esq-e2e": {
			      "Uri": "{{creatioStub.ApplicationUri}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await AllureApi.Step(
			"Arrange the real MCP process and oversized DataService stub",
			async () => await McpServerSession.StartAsync(settings, cancellationTokenSource.Token));

		try {
			// Act
			CallToolResult callResult = await AllureApi.Step(
				"Act by invoking execute-esq through clio-run",
				async () => await session.CallToolAsync(
					ClioRunTool.ToolName,
					new Dictionary<string, object?> {
						["command"] = ExecuteEsqTool.ToolName,
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = "oversized-esq-e2e",
							["query"] = new Dictionary<string, object?> {
								["rootSchemaName"] = "SysPackageReferenceAssembly",
								["allColumns"] = true,
								["rowCount"] = 200
							}
						}
					},
					cancellationTokenSource.Token));
			AllureApi.Step("Assert the stub emitted the reported failure-scale response", () =>
				creatioStub.ResponseBytesWritten.Should().Be(OversizedSelectQueryStub.ExpectedResponseBytes,
					because: "the process regression must exercise an 84 MB response rather than only the rejection boundary"));
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);
			IReadOnlyList<ToolContractIndexEntry> followUpIndex = await AllureApi.Step(
				"Act by invoking a follow-up tool on the same MCP session",
				async () => await session.GetToolContractIndexAsync(cancellationTokenSource.Token));

			// Assert
			AllureApi.Step("Assert the oversized result is not an MCP protocol error", () =>
				callResult.IsError.Should().NotBeTrue(
					because: "an oversized backend result is a structured tool failure, not an MCP invocation error"));
			AllureApi.Step("Assert execute-esq reports failure", () =>
				response.Success.Should().BeFalse(
					because: "the oversized DataService body must not cross the MCP serialization boundary"));
			AllureApi.Step("Assert execute-esq reports the stable result-too-large class", () =>
				response.ErrorClass.Should().Be(ExecuteEsqTool.ResultTooLargeErrorClass,
					because: "the caller needs a stable signal for narrowing or paging the query"));
			AllureApi.Step("Assert oversized rows are omitted", () =>
				response.Rows.Should().BeNull(
					because: "the oversized row payload must be replaced by the bounded error envelope"));
			AllureApi.Step("Assert the same MCP session remains usable", () =>
				followUpIndex.Should().Contain(entry => entry.Name == ExecuteEsqTool.ToolName,
					because: "a follow-up MCP call on the same session must succeed after the oversized result is rejected"));
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException) { /* best-effort cleanup */ }
		catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
	}

	private sealed class OversizedSelectQueryStub : IAsyncDisposable {
		private const int OversizedDataBytes = 84 * 1024 * 1024;
		private static readonly byte[] DataChunk = Enumerable.Repeat((byte)'A', 64 * 1024).ToArray();
		private static readonly byte[] ResponsePrefix = Encoding.UTF8.GetBytes("{\"rows\":[{\"Data\":\"");
		private static readonly byte[] ResponseSuffix = Encoding.UTF8.GetBytes("\"}],\"success\":true}");
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;
		private long _responseBytesWritten;

		private OversizedSelectQueryStub(HttpListener listener, string applicationUri) {
			_listener = listener;
			ApplicationUri = applicationUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string ApplicationUri { get; }
		public static long ExpectedResponseBytes => ResponsePrefix.Length + OversizedDataBytes + ResponseSuffix.Length;
		public long ResponseBytesWritten => Interlocked.Read(ref _responseBytesWritten);

		public static OversizedSelectQueryStub Start() {
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20_000, 60_000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new OversizedSelectQueryStub(listener, $"http://127.0.0.1:{port}");
				}
				catch (HttpListenerException) {
					listener.Close();
				}
			}
			throw new InvalidOperationException("Unable to start the oversized SelectQuery loopback stub.");
		}

		public async ValueTask DisposeAsync() {
			_cancellationTokenSource.Cancel();
			_listener.Stop();
			try {
				await _listenerLoop.ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				// Expected when the fixture stops the listener.
			}
			finally {
				_listener.Close();
				_cancellationTokenSource.Dispose();
			}
		}

		private async Task ListenAsync() {
			while (!_cancellationTokenSource.IsCancellationRequested) {
				HttpListenerContext context;
				try {
					context = await _listener.GetContextAsync()
						.WaitAsync(_cancellationTokenSource.Token)
						.ConfigureAwait(false);
				}
				catch (OperationCanceledException) {
					return;
				}
				catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				}
				catch (ObjectDisposedException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				}
				await RespondAsync(context).ConfigureAwait(false);
			}
		}

		private async Task RespondAsync(HttpListenerContext context) {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			if (path.EndsWith("/ServiceModel/AuthService.svc/Login", StringComparison.Ordinal)) {
				context.Response.Headers.Add("Set-Cookie", ".ASPXAUTH=stub-session; path=/");
				context.Response.Headers.Add("Set-Cookie", "BPMCSRF=stub-csrf; path=/");
				await WriteResponseAsync(context.Response, Encoding.UTF8.GetBytes("{\"Code\":0}"))
					.ConfigureAwait(false);
			} else if (path.EndsWith("/DataService/json/SyncReply/SelectQuery", StringComparison.Ordinal)) {
				await WriteOversizedResponseAsync(context.Response).ConfigureAwait(false);
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				await WriteResponseAsync(context.Response, Encoding.UTF8.GetBytes("Not Found"))
					.ConfigureAwait(false);
			}
		}

		private async Task WriteOversizedResponseAsync(HttpListenerResponse response) {
			response.ContentType = "application/json";
			response.SendChunked = true;
			await WriteCountedAsync(response, ResponsePrefix).ConfigureAwait(false);
			int remaining = OversizedDataBytes;
			while (remaining > 0) {
				int count = Math.Min(remaining, DataChunk.Length);
				await response.OutputStream.WriteAsync(DataChunk.AsMemory(0, count)).ConfigureAwait(false);
				Interlocked.Add(ref _responseBytesWritten, count);
				remaining -= count;
			}
			await WriteCountedAsync(response, ResponseSuffix).ConfigureAwait(false);
			response.Close();
		}

		private async Task WriteCountedAsync(HttpListenerResponse response, byte[] bytes) {
			await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			Interlocked.Add(ref _responseBytesWritten, bytes.Length);
		}

		private static async Task WriteResponseAsync(HttpListenerResponse response, byte[] bytes) {
			response.ContentType = "application/json";
			response.ContentLength64 = bytes.Length;
			await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			response.Close();
		}
	}
}
