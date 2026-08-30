using System.Net;
using System.Text;
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
		await using McpServerSession session = await McpServerSession.StartAsync(
			settings,
			cancellationTokenSource.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
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
				cancellationTokenSource.Token);
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);
			IReadOnlyList<ToolContractIndexEntry> followUpIndex = await session.GetToolContractIndexAsync(
				cancellationTokenSource.Token);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "an oversized backend result is a structured tool failure, not an MCP invocation error");
			response.Success.Should().BeFalse(
				because: "the oversized DataService body must not cross the MCP serialization boundary");
			response.ErrorClass.Should().Be(ExecuteEsqTool.ResultTooLargeErrorClass,
				because: "the caller needs a stable signal for narrowing or paging the query");
			response.Rows.Should().BeNull(
				because: "the oversized row payload must be replaced by the bounded error envelope");
			followUpIndex.Should().Contain(entry => entry.Name == ExecuteEsqTool.ToolName,
				because: "a follow-up MCP call on the same session must succeed after the oversized result is rejected");
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
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;

		private OversizedSelectQueryStub(HttpListener listener, string applicationUri) {
			_listener = listener;
			ApplicationUri = applicationUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string ApplicationUri { get; }

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

		private static async Task RespondAsync(HttpListenerContext context) {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			string body;
			if (path.EndsWith("/ServiceModel/AuthService.svc/Login", StringComparison.Ordinal)) {
				context.Response.Headers.Add("Set-Cookie", ".ASPXAUTH=stub-session; path=/");
				context.Response.Headers.Add("Set-Cookie", "BPMCSRF=stub-csrf; path=/");
				body = "{\"Code\":0}";
			} else if (path.EndsWith("/DataService/json/SyncReply/SelectQuery", StringComparison.Ordinal)) {
				body = $"{{\"rows\":[{{\"Data\":\"{new string('A', ExecuteEsqTool.MaxResponseSizeBytes)}\"}}],\"success\":true}}";
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				body = "Not Found";
			}
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			context.Response.ContentType = "application/json";
			context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			context.Response.Close();
		}
	}
}
