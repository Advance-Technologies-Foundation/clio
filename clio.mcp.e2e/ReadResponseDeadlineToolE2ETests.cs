using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the read-only / retry-safe response deadline (ENG-93373): a read tool driven
/// against a stalling endpoint with a tiny deadline must return the structured <c>creatio-timeout</c>
/// envelope before the client's request ceiling, instead of hanging indefinitely as
/// <c>list-pages</c> did in the ENG-93373 session (7 min 33 s, killed by the user).
/// </summary>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class ReadResponseDeadlineToolE2ETests {
	private const string ListPagesToolName = PageListTool.ToolName;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Starts the real clio MCP server against a stalling endpoint with a 1s read deadline and verifies list-pages returns a structured error-class=creatio-timeout / read-response-timed-out envelope instead of hanging indefinitely.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("Read-only tool returns creatio-timeout when the read deadline elapses")]
	[AllureDescription("Points the environment at a TCP endpoint that accepts the connection but never responds, sets CLIO_MCP_READ_DEADLINE_SECONDS=1, invokes list-pages, and verifies the read deadline yields a structured error-class=creatio-timeout / read-response-timed-out result with retry guidance (ENG-93373) rather than blocking indefinitely.")]
	public async Task ListPages_Should_Return_ReadTimeout_When_Response_Deadline_Elapses() {
		// Arrange — a loopback listener that accepts the TCP connection but never sends a response, so the
		// backend read hangs past the tiny read deadline (a refused port would instead fail fast as a
		// transport error, classified before the deadline).
		using TcpListener stallListener = new(IPAddress.Loopback, 0);
		stallListener.Start();
		int stallPort = ((IPEndPoint)stallListener.LocalEndpoint).Port;
		using CancellationTokenSource acceptCts = new();
		List<TcpClient> heldConnections = new();
		Task acceptLoop = Task.Run(async () => {
			try {
				while (!acceptCts.IsCancellationRequested) {
					TcpClient client = await stallListener.AcceptTcpClientAsync(acceptCts.Token);
					heldConnections.Add(client); // hold the socket open and never write a response
				}
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			catch (ObjectDisposedException) { /* expected when the listener stops */ }
			catch (SocketException) { /* expected when the listener stops */ }
		});

		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-read-deadline-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		// Fresh clio MCP process reads this at startup, so the static default picks up the 1s override.
		settings.ProcessEnvironmentVariables[McpReadResponseDeadline.ReadDeadlineOverrideEnvVar] = "1";
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "stalling-read-e2e",
			  "Environments": {
			    "stalling-read-e2e": {
			      "Uri": "http://127.0.0.1:{{stallPort}}",
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
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ListPagesToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = "stalling-read-e2e",
						["package-name"] = "UsrStallPackage"
					}
				},
				cancellationTokenSource.Token);

			// Assert
			callResult.StructuredContent.Should().NotBeNull(
				because: "a read timeout must return a machine-readable structured envelope");
			JsonElement structured = callResult.StructuredContent!.Value;
			structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
				because: "the read deadline reuses the creatio-timeout class so existing client guidance applies");
			structured.GetProperty("read-response-timed-out").GetBoolean().Should().BeTrue(
				because: "the read envelope must be distinguishable from the write in-progress envelope");
			structured.GetProperty("retry-guidance").GetString().Should().NotBeNullOrWhiteSpace(
				because: "the agent must be told the read is safe to retry instead of blocking indefinitely");
			structured.GetProperty("tool").GetString().Should().Be(ListPagesToolName,
				because: "the envelope must name the tool that timed out");
		}
		finally {
			// Join the accept loop before touching heldConnections: it is the only writer
			// (heldConnections.Add), so awaiting it first turns the subsequent iteration into a
			// single-threaded read and removes the latent List<T> data race.
			await acceptCts.CancelAsync();
			try {
				await acceptLoop;
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			foreach (TcpClient client in heldConnections) {
				client.Dispose();
			}

			stallListener.Stop();
			TryDeleteDirectory(tempHome);
		}
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Starts the real clio MCP server against a stalling endpoint with a 1s read deadline and verifies a NON-resident read tool (execute-esq) dispatched THROUGH clio-run also returns the structured creatio-timeout envelope — proving the clio-run inner-dispatch bounding, the primary long-tail read vector (ENG-93373).")]
	[AllureFeature(ClioRunTool.ToolName)]
	[AllureTag(ClioRunTool.ToolName)]
	[AllureName("Read tool dispatched through clio-run returns creatio-timeout when the read deadline elapses")]
	[AllureDescription("Points the environment at a TCP endpoint that never responds, sets CLIO_MCP_READ_DEADLINE_SECONDS=1, invokes clio-run{command=execute-esq} (a non-resident ReadOnly tool), and verifies the clio-run inner dispatch is bounded by the read deadline and yields error-class=creatio-timeout rather than blocking indefinitely.")]
	public async Task ClioRun_Should_Return_ReadTimeout_When_Dispatching_ReadTool_Past_Deadline() {
		// Arrange — same stalling loopback as above.
		using TcpListener stallListener = new(IPAddress.Loopback, 0);
		stallListener.Start();
		int stallPort = ((IPEndPoint)stallListener.LocalEndpoint).Port;
		using CancellationTokenSource acceptCts = new();
		List<TcpClient> heldConnections = new();
		Task acceptLoop = Task.Run(async () => {
			try {
				while (!acceptCts.IsCancellationRequested) {
					TcpClient client = await stallListener.AcceptTcpClientAsync(acceptCts.Token);
					heldConnections.Add(client);
				}
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			catch (ObjectDisposedException) { /* expected when the listener stops */ }
			catch (SocketException) { /* expected when the listener stops */ }
		});

		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-read-deadline-clr-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		settings.ProcessEnvironmentVariables[McpReadResponseDeadline.ReadDeadlineOverrideEnvVar] = "1";
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "stalling-read-clr-e2e",
			  "Environments": {
			    "stalling-read-clr-e2e": {
			      "Uri": "http://127.0.0.1:{{stallPort}}",
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
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		try {
			// Act — call the clio-run executor explicitly so the dispatch goes through the inner-dispatch
			// bounding, targeting execute-esq (a non-resident ReadOnly tool: the long-tail path).
			CallToolResult callResult = await session.CallToolAsync(
				ClioRunTool.ToolName,
				new Dictionary<string, object?> {
					["command"] = ExecuteEsqTool.ToolName,
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = "stalling-read-clr-e2e",
						// A minimal but VALID SelectQuery object (non-empty rootSchemaName) so execute-esq passes
						// validation and reaches the network POST — which hangs on the stalling endpoint until the
						// 1s read deadline fires (execute-esq's own timeout is 30s, so the read deadline wins).
						["query"] = new Dictionary<string, object?> {
							["rootSchemaName"] = "Contact"
						}
					}
				},
				cancellationTokenSource.Token);

			// Assert
			callResult.StructuredContent.Should().NotBeNull(
				because: "a read timeout through clio-run must still return a machine-readable structured envelope");
			JsonElement structured = callResult.StructuredContent!.Value;
			structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
				because: "the clio-run inner dispatch must bound a retry-safe read with the same creatio-timeout class");
			structured.GetProperty("read-response-timed-out").GetBoolean().Should().BeTrue(
				because: "the read timeout envelope must be recognizable regardless of dispatch vector");
		}
		finally {
			await acceptCts.CancelAsync();
			try {
				await acceptLoop;
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			foreach (TcpClient client in heldConnections) {
				client.Dispose();
			}

			stallListener.Stop();
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
}
