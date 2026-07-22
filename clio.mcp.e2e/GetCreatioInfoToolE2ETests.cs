using System.Net;
using System.Text;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the describe-environment MCP tool. NOT part of CI — run manually against a
/// real clio mcp-server process.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetCreatioInfoTool.ToolName)]
[NonParallelizable]
public sealed class GetCreatioInfoToolE2ETests : McpContractFixtureBase {
	private sealed class NonCreatioApplicationStub : IAsyncDisposable {
		private const string ResponseMarker = "not-creatio-secret-marker";
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;

		private NonCreatioApplicationStub(HttpListener listener, string applicationUri) {
			_listener = listener;
			ApplicationUri = applicationUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string ApplicationUri { get; }

		public static NonCreatioApplicationStub Start() {
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20_000, 60_000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new NonCreatioApplicationStub(listener, $"http://127.0.0.1:{port}");
				} catch (HttpListenerException) {
					listener.Close();
				}
			}
			throw new InvalidOperationException("Unable to start the non-Creatio loopback stub.");
		}

		public async ValueTask DisposeAsync() {
			_cancellationTokenSource.Cancel();
			_listener.Stop();
			try {
				await _listenerLoop.ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Expected when the fixture stops the listener.
			} finally {
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
				} catch (OperationCanceledException) {
					return;
				} catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				} catch (ObjectDisposedException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				}
				await RespondAsync(context).ConfigureAwait(false);
			}
		}

		private static async Task RespondAsync(HttpListenerContext context) {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			string body;
			if (path.EndsWith("/ServiceModel/AuthService.svc/Login", StringComparison.Ordinal)) {
				context.Response.SetCookie(new Cookie(".ASPXAUTH", "stub-session", "/"));
				context.Response.ContentType = "application/json";
				body = "{\"Code\":0}";
			} else if (path.EndsWith("/ping", StringComparison.Ordinal)) {
				context.Response.ContentType = "application/json";
				body = "{}";
			} else if (path.EndsWith(
					"/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo", StringComparison.Ordinal)) {
				context.Response.ContentType = "text/html";
				body = $"<html><body>{ResponseMarker}</body></html>";
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				context.Response.ContentType = "text/plain";
				body = "Not Found";
			}
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			context.Response.Close();
		}
	}

	[Test]
	[Description("Exposes describe-environment via the get-tool-contract compact index with a non-destructive safety flag on the lazy tool surface.")]
	[AllureTag(GetCreatioInfoTool.ToolName)]
	[AllureName("describe-environment MCP tool is discoverable on the lazy surface")]
	public async Task DescribeEnvironment_Should_Be_Advertised() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// The lazy surface exposes hidden tools only through the compact discovery index, which carries the
		// destructive flag; the read-only hint is no longer observable for non-resident tools.
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == GetCreatioInfoTool.ToolName,
			because: "describe-environment must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "describe-environment only reads instance metadata and must not be flagged destructive");
	}

	[Test]
	[Description("Binds describe-environment arguments through the real MCP server and returns a structured exit-code-1 failure for an unknown environment.")]
	[AllureTag(GetCreatioInfoTool.ToolName)]
	[AllureName("describe-environment MCP tool binds arguments")]
	public async Task DescribeEnvironment_Should_Bind_Arguments_And_Report_Failure_For_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-describe-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid describe-environment payloads should bind and return a structured execution result, not a protocol error");
		response.ExitCode.Should().Be(1,
			because: "an unknown registered environment is an expected, caller-actionable failure (exit code 1)");
	}

	[Test]
	[Description("Classifies HTML returned by a reachable non-Creatio loopback target through the real MCP server without leaking parser or body details.")]
	[AllureTag(GetCreatioInfoTool.ToolName)]
	[AllureName("describe-environment classifies a reachable non-Creatio target")]
	[AllureDescription("Starts a local HTTP stub that completes clio's basic-auth handshake and returns HTML from ApplicationInfoService, then verifies the external MCP process returns the stable non-Creatio Error envelope.")]
	public async Task DescribeEnvironment_ShouldReportNonCreatioError_WhenBaseProbeReturnsHtml() {
		// Arrange
		await using NonCreatioApplicationStub stub = NonCreatioApplicationStub.Start();
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["uri"] = stub.ApplicationUri,
					["login"] = "stub-user",
					["password"] = "stub-password",
					["timeout"] = 5_000
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a classified command failure should use the standard execution envelope, not a protocol error");
		response.ExitCode.Should().Be(1,
			because: "a reachable non-Creatio target cannot produce a valid environment report");
		response.Output.Should().Contain(message =>
			message.MessageType == Clio.Common.LogDecoratorType.Error
			&& (message.Value ?? string.Empty).Contains(
				"does not appear to be a Creatio application", StringComparison.Ordinal),
			because: "MCP callers need the actionable non-Creatio classification from the command");
		response.Output.Should().NotContain(message =>
			(message.Value ?? string.Empty).Contains("not-creatio-secret-marker", StringComparison.Ordinal)
			|| (message.Value ?? string.Empty).Contains("JsonReaderException", StringComparison.Ordinal),
			because: "raw HTML and parser implementation details must not cross the MCP boundary");
	}
}
