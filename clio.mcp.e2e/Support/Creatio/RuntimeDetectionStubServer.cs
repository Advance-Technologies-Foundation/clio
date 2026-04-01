using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Clio.Mcp.E2E.Support.Creatio;

internal sealed class RuntimeDetectionStubServer : IAsyncDisposable {
	private readonly Process _process;
	private readonly string _scriptPath;

	private RuntimeDetectionStubServer(Process process, string scriptPath, string baseUrl) {
		_process = process;
		_scriptPath = scriptPath;
		BaseUrl = baseUrl;
	}

	public string BaseUrl { get; }

	public static RuntimeDetectionStubServer Start(RuntimeDetectionStubServerConfiguration configuration) {
		int port = GetFreePort();
		string scriptPath = Path.Combine(Path.GetTempPath(), $"clio-runtime-detection-stub-{Guid.NewGuid():N}.js");
		File.WriteAllText(scriptPath, BuildScript(configuration, port));
		ProcessStartInfo startInfo = new("node", scriptPath) {
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Unable to start the runtime detection stub server.");
		string readyLine = process.StandardOutput.ReadLine()
			?? throw new InvalidOperationException("Runtime detection stub server did not report a listening address.");
		string expectedReadyLine = $"LISTEN {port.ToString(CultureInfo.InvariantCulture)}";
		if (!string.Equals(readyLine, expectedReadyLine, StringComparison.Ordinal)) {
			string standardError = process.StandardError.ReadToEnd();
			throw new InvalidOperationException(
				$"Runtime detection stub server failed to initialize. Stdout: {readyLine}. Stderr: {standardError}");
		}

		return new RuntimeDetectionStubServer(process, scriptPath, $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}");
	}

	public async ValueTask DisposeAsync() {
		try {
			if (!_process.HasExited) {
				_process.Kill(entireProcessTree: true);
				await _process.WaitForExitAsync();
			}
		} catch (InvalidOperationException) {
		}

		_process.Dispose();
		if (File.Exists(_scriptPath)) {
			File.Delete(_scriptPath);
		}
	}

	private static string BuildScript(RuntimeDetectionStubServerConfiguration configuration, int port) {
		string configJson = JsonSerializer.Serialize(configuration);
		return $$"""
const http = require("http");
const config = {{configJson}};
const port = {{port.ToString(CultureInfo.InvariantCulture)}};

function sendJson(response, statusCode, body, headers = {}) {
  response.writeHead(statusCode, { "Content-Type": "application/json", ...headers });
  response.end(JSON.stringify(body));
}

function sendText(response, statusCode, body) {
  response.writeHead(statusCode, { "Content-Type": "text/plain" });
  response.end(body);
}

http.createServer((request, response) => {
  let body = "";
  request.on("data", chunk => { body += chunk; });
  request.on("end", () => {
    const url = request.url || "";
    if (request.method === "POST" && url === "/ServiceModel/AuthService.svc/Login") {
      sendJson(
        response,
        200,
        { RedirectUrl: null, PasswordChangeUrl: null, Exception: null, UserType: "General", Code: 0, Message: "" },
        {
          "Set-Cookie": [
            "UserType=General; Path=/; SameSite=Lax",
            ".ASPXAUTH=stub-auth; Path=/; SameSite=Lax; HttpOnly",
            "CsrfToken=stub-csrf; Path=/; SameSite=Lax; HttpOnly",
            "CRT_CSRF=stub-csrf; Path=/; SameSite=Lax",
            "BPMCSRF=stub-csrf; Path=/; SameSite=Lax"
          ]
        }
      );
      return;
    }
    if (request.method === "GET" && url === "/api/HealthCheck/Ping") {
      sendText(response, config.NetCoreHealthEnabled ? 200 : 404, config.NetCoreHealthEnabled ? "OK" : "Not Found");
      return;
    }
    if (request.method === "GET" && url === "/0/api/HealthCheck/Ping") {
      sendText(response, config.NetFrameworkHealthEnabled ? 200 : 404, config.NetFrameworkHealthEnabled ? "OK" : "Not Found");
      return;
    }
    if (request.method === "GET" && url === "/Login/Login.html") {
      sendText(response, config.NetCoreUiMarkerEnabled ? 200 : 404, config.NetCoreUiMarkerEnabled ? "OK" : "Not Found");
      return;
    }
    if (request.method === "GET" && url === "/0/Login/NuiLogin.aspx") {
      sendText(response, config.NetFrameworkUiMarkerEnabled ? 200 : 404, config.NetFrameworkUiMarkerEnabled ? "OK" : "Not Found");
      return;
    }
    if (request.method === "POST" && url === "/DataService/json/SyncReply/SelectQuery") {
      if (config.NetCoreServiceEnabled) {
        sendJson(response, 200, { success: true, rows: [{ Id: "1" }] });
        return;
      }
      sendText(response, 404, "Not Found");
      return;
    }
    if (request.method === "POST" && url === "/0/DataService/json/SyncReply/SelectQuery") {
      if (config.NetFrameworkServiceEnabled) {
        sendJson(response, 200, { success: true, rows: [{ Id: "1" }] });
        return;
      }
      sendText(response, 404, "Not Found");
      return;
    }
    sendText(response, 404, "Not Found");
  });
}).listen(port, "127.0.0.1", () => {
  console.log(`LISTEN ${port}`);
});
""";
	}

	private static int GetFreePort() {
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}
}

internal sealed record RuntimeDetectionStubServerConfiguration(
	bool NetCoreHealthEnabled,
	bool NetFrameworkHealthEnabled,
	bool NetCoreServiceEnabled,
	bool NetFrameworkServiceEnabled,
	bool NetCoreUiMarkerEnabled = false,
	bool NetFrameworkUiMarkerEnabled = false);
