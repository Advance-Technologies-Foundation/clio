using System.Net;
using System.Net.Sockets;
using System.Text;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for restore-workspace against an environment that does not answer. None of it needs
/// Creatio: the registered URL is either a local port nothing listens on, or a local stub that rejects every
/// login the way Creatio does.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RestoreWorkspaceTool.RestoreWorkspaceToolName)]
public sealed class RestoreWorkspaceToolE2ETests {

	private const string EnvironmentName = "restore-workspace-unreachable";
	private const string PackageName = "UsrRestoreProbe";
	private const string ReasonOpening = "the environment did not answer (";
	private const string ReasonClosing = "). Check it with ping-app";

	[Test]
	[AllureTag(RestoreWorkspaceTool.RestoreWorkspaceToolName)]
	[AllureName("restore-workspace fails with the reason when the environment does not answer")]
	[AllureDescription("Registers an environment on a closed local port in a clio home of the fixture's own, writes a workspace that lists one package, calls restore-workspace through the real MCP server, and verifies a non-zero exit code, an error that says nothing was downloaded and gives a non-empty reason, no Done line, and no package directory.")]
	[Description("Starts the real clio MCP server with an environment registered on a closed local port and a workspace that lists one package, calls restore-workspace, and verifies it fails with a non-zero exit code and an error naming why nothing was downloaded, instead of reporting Done with exit code 0 as it did when the ping failed silently.")]
	public async Task RestoreWorkspace_ShouldFailWithTheReason_WhenTheEnvironmentDoesNotAnswer() {
		// Arrange
		string tempHome = CreateTempHome();
		string workspacePath = WriteWorkspaceWithOnePackage(tempHome);
		try {
			// Act
			using Socket closedPort = HoldClosedPort(out int port);
			CommandExecutionEnvelope execution = await RestoreAsync(tempHome, workspacePath, port);

			// Assert
			execution.ExitCode.Should().NotBe(0,
				because: "a restore that downloaded nothing must not report success to an agent or a script");
			string error = SingleErrorStartingWith(execution, "Packages were not downloaded: ");
			ReasonIn(error).Should().NotBeNullOrWhiteSpace(
				because: "the reason the ping failed is the point of the error; without it the caller cannot tell a stopped site from a wrong password");
			execution.Output.Should().NotContain(message => message.Value == "Done",
				because: "Done is what the silent failure used to print");
			Directory.Exists(Path.Combine(workspacePath, "packages", PackageName)).Should().BeFalse(
				because: "nothing is unpacked from an environment that did not answer");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Test]
	[AllureTag(RestoreWorkspaceTool.RestoreWorkspaceToolName)]
	[AllureName("restore-workspace names the rejected login when the environment refuses the credentials")]
	[AllureDescription("Registers an environment on a local stub that answers every AuthService.svc/Login with Code 1, as Creatio does for a wrong password, calls restore-workspace on a workspace that lists one package, and verifies a non-zero exit code and an error whose reason is the client's own 'Unauthorized Supervisor for <url>'.")]
	[Description("Starts the real clio MCP server with an environment whose login a local stub rejects the way Creatio does (HTTP 200 with Code 1), calls restore-workspace, and verifies it fails with the rejected login as the reason - the case that used to print Done and exit 0.")]
	public async Task RestoreWorkspace_ShouldNameTheRejectedLogin_WhenTheEnvironmentRefusesTheCredentials() {
		// Arrange
		string tempHome = CreateTempHome();
		string workspacePath = WriteWorkspaceWithOnePackage(tempHome);
		await using RejectingLoginServer server = RejectingLoginServer.Start();
		try {
			// Act
			CommandExecutionEnvelope execution = await RestoreAsync(tempHome, workspacePath, server.Port);

			// Assert
			execution.ExitCode.Should().NotBe(0,
				because: "a restore whose login was rejected restored nothing and must not report success");
			string error = SingleErrorStartingWith(execution, "Packages were not downloaded: ");
			ReasonIn(error).Should().Contain($"Unauthorized Supervisor for http://127.0.0.1:{server.Port}",
				because: "the reason must be the rejected login itself, which is what ping-app reports for the same environment");
			server.LoginRequests.Should().BePositive(
				because: "the failure must come from a login the environment refused, not from an earlier step");
			execution.Output.Should().NotContain(message => message.Value == "Done",
				because: "Done is what the silent failure used to print");
			Directory.Exists(Path.Combine(workspacePath, "packages", PackageName)).Should().BeFalse(
				because: "nothing is unpacked from an environment that refused the login");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Test]
	[AllureTag(RestoreWorkspaceTool.RestoreWorkspaceToolName)]
	[AllureName("restore-workspace's create-workspace fallback writes nothing when the login is rejected")]
	[AllureDescription("Calls restore-workspace on an empty folder, which falls back to create-workspace, with an environment whose login a local stub rejects, and verifies a non-zero exit code, an error that the workspace was not created with the rejected login as the reason, and a folder that is still empty.")]
	[Description("Calls restore-workspace on an empty folder so it falls back to create-workspace, against an environment whose login a local stub rejects, and verifies the command fails before writing anything, so no .clio skeleton is left behind.")]
	public async Task RestoreWorkspace_ShouldWriteNothing_WhenItFallsBackToCreateWorkspaceAndTheLoginIsRejected() {
		// Arrange
		string tempHome = CreateTempHome();
		string workspacePath = Path.Combine(tempHome, "workspace");
		Directory.CreateDirectory(workspacePath);
		await using RejectingLoginServer server = RejectingLoginServer.Start();
		try {
			// Act
			CommandExecutionEnvelope execution = await RestoreAsync(tempHome, workspacePath, server.Port);

			// Assert
			execution.ExitCode.Should().NotBe(0,
				because: "a workspace that cannot be filled from its environment must not be reported as created");
			string error = SingleErrorStartingWith(execution, "Workspace was not created: ");
			ReasonIn(error).Should().Contain($"Unauthorized Supervisor for http://127.0.0.1:{server.Port}",
				because: "the reason must be the rejected login");
			Directory.EnumerateFileSystemEntries(workspacePath).Should().BeEmpty(
				because: "the environment is checked before the skeleton is written, so a failed fallback leaves nothing to clean up");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	private static string CreateTempHome() {
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-restore-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		return tempHome;
	}

	private static string WriteWorkspaceWithOnePackage(string tempHome) {
		string workspacePath = Path.Combine(tempHome, "workspace");
		Directory.CreateDirectory(Path.Combine(workspacePath, ".clio"));
		File.WriteAllText(Path.Combine(workspacePath, ".clio", "workspaceSettings.json"),
			$$"""{"ApplicationVersion":"8.0.0","Packages":["{{PackageName}}"]}""");
		return workspacePath;
	}

	private static async Task<CommandExecutionEnvelope> RestoreAsync(string tempHome, string workspacePath, int port) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;
		// A home of this fixture's own, so the settings replacement below never rewrites the file other fixtures read.
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			LocalEnvironmentSettings(port), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
		using CancellationTokenSource scenario = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, scenario.Token);
		CallToolResult callResult = await session.CallToolAsync(
			RestoreWorkspaceTool.RestoreWorkspaceToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = EnvironmentName,
					["workspace-path"] = workspacePath
				}
			},
			scenario.Token);
		return McpCommandExecutionParser.Extract(callResult);
	}

	private static string SingleErrorStartingWith(CommandExecutionEnvelope execution, string prefix) {
		execution.Output.Should().NotBeNull(because: "a command execution always carries its log");
		string[] errors = execution.Output!
			.Where(message => message.MessageType == LogDecoratorType.Error && message.Value != null
				&& message.Value.StartsWith(prefix, StringComparison.Ordinal))
			.Select(message => message.Value!)
			.ToArray();
		errors.Should().ContainSingle(because: $"the failure must be reported once, as an error starting with '{prefix}'");
		return errors[0];
	}

	/// <summary>The text between "did not answer (" and "). Check it with ping-app" - the ping's own failure reason.</summary>
	private static string? ReasonIn(string error) {
		int start = error.IndexOf(ReasonOpening, StringComparison.Ordinal);
		int end = error.LastIndexOf(ReasonClosing, StringComparison.Ordinal);
		return start < 0 || end < start + ReasonOpening.Length
			? null
			: error[(start + ReasonOpening.Length)..end];
	}

	/// <summary>
	/// A local port that refuses connections for as long as the returned socket lives: the socket is bound but
	/// never listens, so no other process can take the port and a connection gets a refusal, not an answer or a
	/// hang. Binding, reading and releasing the port would leave a window in which another process takes it.
	/// </summary>
	private static Socket HoldClosedPort(out int port) {
		Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		port = ((IPEndPoint)socket.LocalEndPoint!).Port;
		return socket;
	}

	private static string LocalEnvironmentSettings(int port) =>
		$$"""
		{
		  "ActiveEnvironmentKey": "{{EnvironmentName}}",
		  "Environments": {
		    "{{EnvironmentName}}": {
		      "Uri": "http://127.0.0.1:{{port}}",
		      "Login": "Supervisor",
		      "Password": "Supervisor",
		      "IsNetCore": true
		    }
		  }
		}
		""";

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException) {
			// Best effort: a server process that has not exited yet can still hold a file in the temporary home.
		}
		catch (UnauthorizedAccessException) {
			// Same as above.
		}
	}

	/// <summary>
	/// A local HTTP endpoint that rejects every login the way Creatio does for a wrong password:
	/// <c>ServiceModel/AuthService.svc/Login</c> answers 200 with <c>{"Code":1}</c>, which the Creatio client turns
	/// into <c>Unauthorized &lt;user&gt; for &lt;url&gt;</c>. Every other request gets 401. A raw socket rather than
	/// HttpListener, so it needs no URL reservation on Windows agents.
	/// </summary>
	private sealed class RejectingLoginServer : IAsyncDisposable {

		private const string LoginPath = "/ServiceModel/AuthService.svc/Login";
		private const string RejectedLoginBody = """{"Code":1,"Message":"Either username or password is incorrect."}""";

		private readonly TcpListener _listener;
		private readonly CancellationTokenSource _stop = new();
		private readonly Task _acceptLoop;
		private int _loginRequests;

		private RejectingLoginServer(TcpListener listener) {
			_listener = listener;
			Port = ((IPEndPoint)listener.LocalEndpoint).Port;
			_acceptLoop = Task.Run(AcceptLoopAsync);
		}

		public int Port { get; }

		public int LoginRequests => Volatile.Read(ref _loginRequests);

		public static RejectingLoginServer Start() {
			TcpListener listener = new(IPAddress.Loopback, 0);
			listener.Start();
			return new RejectingLoginServer(listener);
		}

		public async ValueTask DisposeAsync() {
			await _stop.CancelAsync();
			_listener.Stop();
			try {
				await _acceptLoop;
			}
			catch (OperationCanceledException) {
				// Stopping the listener ends the loop.
			}
			_stop.Dispose();
		}

		private async Task AcceptLoopAsync() {
			while (!_stop.IsCancellationRequested) {
				TcpClient client;
				try {
					client = await _listener.AcceptTcpClientAsync(_stop.Token);
				}
				catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
					or SocketException) {
					return;
				}
				_ = Task.Run(() => AnswerAsync(client));
			}
		}

		private async Task AnswerAsync(TcpClient client) {
			using (client) {
				try {
					NetworkStream stream = client.GetStream();
					string requestLine = await ReadRequestAsync(stream);
					bool isLogin = requestLine.Contains(LoginPath, StringComparison.OrdinalIgnoreCase);
					if (isLogin) {
						Interlocked.Increment(ref _loginRequests);
					}
					string body = isLogin ? RejectedLoginBody : string.Empty;
					string status = isLogin ? "200 OK" : "401 Unauthorized";
					byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
					string head = $"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\n"
						+ $"Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
					await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
					await stream.WriteAsync(bodyBytes, _stop.Token);
					await stream.FlushAsync(_stop.Token);
				}
				catch (Exception exception) when (exception is IOException or SocketException
					or OperationCanceledException or ObjectDisposedException) {
					// The client went away or the test is over; nothing to answer.
				}
			}
		}

		/// <summary>Reads the request head and its Content-Length body, and returns the request line.</summary>
		private async Task<string> ReadRequestAsync(NetworkStream stream) {
			byte[] buffer = new byte[8192];
			using MemoryStream received = new();
			int headEnd;
			while ((headEnd = IndexOfHeadEnd(received)) < 0) {
				int read = await stream.ReadAsync(buffer, _stop.Token);
				if (read == 0) {
					return string.Empty;
				}
				received.Write(buffer, 0, read);
			}
			string head = Encoding.ASCII.GetString(received.GetBuffer(), 0, headEnd);
			int contentLength = ContentLengthOf(head);
			long bodyReceived = received.Length - (headEnd + 4);
			while (bodyReceived < contentLength) {
				int read = await stream.ReadAsync(buffer, _stop.Token);
				if (read == 0) {
					break;
				}
				bodyReceived += read;
			}
			int lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
			return lineEnd < 0 ? head : head[..lineEnd];
		}

		private static int IndexOfHeadEnd(MemoryStream received) {
			byte[] bytes = received.GetBuffer();
			for (int i = 0; i + 3 < received.Length; i++) {
				if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n') {
					return i;
				}
			}
			return -1;
		}

		private static int ContentLengthOf(string head) {
			foreach (string line in head.Split("\r\n")) {
				if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(line["Content-Length:".Length..].Trim(), out int length)) {
					return length;
				}
			}
			return 0;
		}
	}
}
