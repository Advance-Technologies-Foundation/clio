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

	/// <summary>
	/// Marker embedded in the HTML body the stub returns for a SelectQuery against
	/// <see cref="RuntimeDetectionStubServerConfiguration.HtmlSelectQuerySchemaName"/>. Tests assert it is
	/// absent from the surfaced error so a leaked response body is caught.
	/// </summary>
	public const string SelectQueryHtmlBodyMarker = "select-query-secret-marker";

	/// <summary>
	/// Marker embedded in the HTML body the stub returns for any odata request (GET/POST/PATCH/DELETE)
	/// against <see cref="RuntimeDetectionStubServerConfiguration.ODataNonJsonEntity"/>. Mirrors the
	/// IIS-served 404/401 pages observed in ENG-95971: the request reaches the stub but never reaches a
	/// real OData controller, so the body is plain HTML instead of any recognized JSON shape.
	/// </summary>
	public const string ODataNonJsonBodyMarker = "odata-nonjson-secret-marker";

	/// <summary>
	/// The exact body the stub returns for a GET against
	/// <see cref="RuntimeDetectionStubServerConfiguration.ODataEchoEntity"/>. Held as a single constant so a
	/// file-mode test can assert the persisted bytes are byte-for-byte the response, not a re-serialization.
	/// </summary>
	public const string ODataEchoCollectionBody =
		"{\"@odata.count\":2,\"value\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Alpha\"},"
		+ "{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Name\":\"Beta\"}]}";

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
    if (request.method === "POST"
      && (url === "/DataService/json/SyncReply/SelectQuery" || url === "/0/DataService/json/SyncReply/SelectQuery")
      && config.HtmlSelectQuerySchemaName
      && body.includes('"' + config.HtmlSelectQuerySchemaName + '"')) {
      // ENG-93365: the stand answered a specific SelectQuery with an HTML error page instead of JSON.
      // Keyed on the queried schema so the runtime-detection probe (SysAdminUnit) still gets valid JSON
      // and environment registration is unaffected.
      response.writeHead(200, { "Content-Type": "text/html" });
      response.end("<!DOCTYPE html><html><head><title>Runtime Error</title></head><body>Server Error in '/' Application. {{SelectQueryHtmlBodyMarker}}</body></html>");
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
    if (config.ODataEchoEntity && url.includes("/odata/" + config.ODataEchoEntity)) {
      // A minimal but REAL OData endpoint: GET answers a fixed collection byte-for-byte, POST echoes the
      // row's Name back as the created record Id, and PATCH succeeds only when the request body carries the
      // expected marker. That is what makes a successful file-mode call provable end to end - the response
      // bytes on disk, and the fact that a file-backed payload actually reached the write request.
      if (request.method === "GET") {
        const oversized = config.ODataOversizedBytes || 0;
        if (oversized > 0) {
          // Streams a body past clio's ceiling WITHOUT a Content-Length, so the rejection can only come
          // from the running total as the bytes arrive.
          response.writeHead(200, { "Content-Type": "application/json" });
          response.write("{\"value\":[{\"Id\":\"1\",\"Filler\":\"");
          const chunk = "x".repeat(64 * 1024);
          let written = 0;
          while (written < oversized) {
            response.write(chunk);
            written += chunk.length;
          }
          response.end("\"}]}");
          return;
        }
        response.writeHead(200, { "Content-Type": "application/json" });
        response.end({{JsonSerializer.Serialize(ODataEchoCollectionBody)}});
        return;
      }
      if (request.method === "POST") {
        let name = null;
        try { name = JSON.parse(body).Name; } catch (error) { name = null; }
        if (!name) {
          response.writeHead(200, { "Content-Type": "text/html" });
          response.end("<html><body>post body did not carry a Name</body></html>");
          return;
        }
        sendJson(response, 200, { Id: String(name) });
        return;
      }
      if (request.method === "PATCH") {
        if (!config.ODataWriteRequiredMarker || !body.includes(config.ODataWriteRequiredMarker)) {
          response.writeHead(200, { "Content-Type": "text/html" });
          response.end("<html><body>patch body did not carry the expected marker</body></html>");
          return;
        }
        response.writeHead(204);
        response.end();
        return;
      }
      sendText(response, 405, "Method Not Allowed");
      return;
    }
    if (config.ODataNonJsonEntity && url.includes("/odata/" + config.ODataNonJsonEntity)) {
      // ENG-95971: the stand answered an odata request (read or write) with an IIS-style HTML error
      // page instead of JSON or a recognized error shape - the request reached the stub but never
      // reached a real OData controller. Any HTTP method is matched: the same failure was observed on
      // GET, PATCH, and DELETE alike.
      response.writeHead(200, { "Content-Type": "text/html" });
      response.end("<!DOCTYPE html><html><head><title>404 - File or directory not found.</title></head><body>{{ODataNonJsonBodyMarker}}</body></html>");
      return;
    }
    if ((request.method === "GET" || request.method === "POST") && config.ODataRoutingErrorEntity && (url.includes("/odata/" + config.ODataRoutingErrorEntity + "?") || url.endsWith("/odata/" + config.ODataRoutingErrorEntity))) {
      // ASP.NET Web API 404 routing error shape for an unregistered/uncompiled OData controller.
      // Creatio returns this with HTTP 200 in the analyzed session, masking the failure as data.
      sendJson(response, 200, {
        Message: "No HTTP resource was found that matches the request URI '" + url + "'.",
        MessageDetail: "No type was found that matches the controller named '" + config.ODataRoutingErrorEntity + "'."
      });
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
	bool NetFrameworkUiMarkerEnabled = false,
	string? ODataRoutingErrorEntity = null,
	string? HtmlSelectQuerySchemaName = null,
	string? ODataNonJsonEntity = null,
	string? ODataEchoEntity = null,
	string? ODataWriteRequiredMarker = null,
	int ODataOversizedBytes = 0);
