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
	/// Marker embedded in every HTML body the stub returns from <c>GetSchemaDesignItem</c> when
	/// <see cref="RuntimeDetectionStubServerConfiguration.DesignerHtmlMode"/> is set. Tests assert it is
	/// absent from the surfaced error, so a designer error page leaking into an agent transcript is caught
	/// (issue #722).
	/// </summary>
	public const string DesignerHtmlBodyMarker = "designer-html-secret-marker";

	/// <summary>ASP.NET server-error page: the shape a <c>SchemaIsNotAvailableException</c> arrives in.</summary>
	public const string DesignerHtmlServerError = "server-error";

	/// <summary>Rendered sign-in page: an expired session, which is an authentication failure, not a package problem.</summary>
	public const string DesignerHtmlLoginPage = "login-page";

	/// <summary>Bare markup fragment, the IIS/WAF/proxy shape the old doctype-prefixed test did not classify as markup.</summary>
	public const string DesignerHtmlFragment = "fragment";

	/// <summary>
	/// Marker embedded in the HTML body the stub returns for any odata request (GET/POST/PATCH/DELETE)
	/// against <see cref="RuntimeDetectionStubServerConfiguration.ODataNonJsonEntity"/>. Mirrors the
	/// IIS-served 404/401 pages observed in ENG-95971: the request reaches the stub but never reaches a
	/// real OData controller, so the body is plain HTML instead of any recognized JSON shape.
	/// </summary>
	public const string ODataNonJsonBodyMarker = "odata-nonjson-secret-marker";

	/// <summary>
	/// Plain-text marker in the non-JSON body the stub returns for the pre-write <c>$metadata</c> and
	/// <c>$select</c> probes when <see cref="RuntimeDetectionStubServerConfiguration.ODataPreWriteMode"/>
	/// is <see cref="ODataPreWriteUnverified"/>. A prefix of the body IS deliberately surfaced as
	/// diagnostics - the same contract as <c>ODataKeyedWrite.ValidateWriteResponse</c> - so tests assert
	/// this marker is PRESENT and that only the sensitive parts below are scrubbed.
	/// </summary>
	public const string ODataPreWriteUnverifiedBodyMarker = "odata-prewrite-body-marker";

	/// <summary>
	/// Credential embedded in the URI inside the <see cref="ODataPreWriteUnverified"/> body. Tests assert it
	/// is absent from the surfaced error: a proxy/IIS or SSO page is the realistic carrier of credentials and
	/// redirect tokens, so the redactor must scrub it before the text reaches the MCP transcript.
	/// </summary>
	public const string ODataPreWriteUnverifiedSecret = "Sup3rS3cretPreWrite";

	/// <summary>
	/// Internal host embedded in the URI inside the <see cref="ODataPreWriteUnverified"/> body. Tests assert
	/// it is absent from the surfaced error for the same reason as <see cref="ODataPreWriteUnverifiedSecret"/>.
	/// </summary>
	public const string ODataPreWriteUnverifiedHost = "prewrite-stub.internal";

	/// <summary>
	/// <see cref="RuntimeDetectionStubServerConfiguration.ODataPreWriteMode"/> value that serves a CSDL 4.0
	/// document at the SERVICE-ROOT <c>odata/$metadata</c> declaring
	/// <see cref="RuntimeDetectionStubServerConfiguration.ODataEntity"/> with <c>Id</c> and <c>Name</c>,
	/// answers a keyed <c>$select</c> probe with the addressed record, and acks a PATCH with an empty 204.
	/// </summary>
	public const string ODataPreWriteMetadata = "metadata";

	/// <summary>
	/// <see cref="RuntimeDetectionStubServerConfiguration.ODataPreWriteMode"/> value that answers BOTH
	/// pre-write reads (<c>$metadata</c> and the keyed <c>$select</c> probe) with a non-JSON body, so the
	/// payload can be neither confirmed nor refuted. A PATCH is still acked, so a tool that wrote anyway
	/// is caught by the recorded-request assertions rather than by a transport failure.
	/// </summary>
	public const string ODataPreWriteUnverified = "unverified";

	/// <summary>
	/// <see cref="RuntimeDetectionStubServerConfiguration.ODataPreWriteMode"/> value that answers
	/// <c>$metadata</c> with an HTML page - so validation degrades to the keyed <c>$select</c> probe -
	/// and answers that probe with a bare <c>{}</c>: valid JSON, no recognized error shape, and no proof
	/// that any field exists. A PATCH is still acked, so a tool that treated "no error" as verification
	/// is caught by the recorded-request assertions. This is the fail-open shape behind issue #1212.
	/// </summary>
	public const string ODataPreWriteEmptyRecord = "emptyrecord";

	/// <summary>
	/// Path of the stub's own introspection endpoint. A GET returns a JSON array of
	/// <c>{ "method": ..., "url": ... }</c> for every request the stub has served, letting a test prove
	/// which URL the pre-write validation actually requested and that no PATCH was issued.
	/// </summary>
	public const string RecordedRequestsPath = "/__stub/requests";

	/// <summary>Absolute URL of <see cref="RecordedRequestsPath"/> on this stub instance.</summary>
	public string RecordedRequestsUrl => BaseUrl + RecordedRequestsPath;

	/// <summary>
	/// Reads the requests the stub has served so far, in arrival order.
	/// </summary>
	/// <param name="cancellationToken">Token that cancels the introspection call.</param>
	/// <returns>Every request the stub served, oldest first.</returns>
	public async Task<IReadOnlyList<RecordedStubRequest>> GetRecordedRequestsAsync(
		CancellationToken cancellationToken = default) {
		using HttpClient client = new();
		string json = await client.GetStringAsync(RecordedRequestsUrl, cancellationToken);
		return JsonSerializer.Deserialize<List<RecordedStubRequest>>(json,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
	}

	private static string BuildScript(RuntimeDetectionStubServerConfiguration configuration, int port) {
		string configJson = JsonSerializer.Serialize(configuration);
		return $$"""
const http = require("http");
const config = {{configJson}};
const port = {{port.ToString(CultureInfo.InvariantCulture)}};
const recordedRequests = [];

// CSDL 4.0 served at the SERVICE-ROOT odata/$metadata. Declares only Id and Name, so any other
// field name in an odata-update payload must be rejected before the PATCH.
function metadataCsdl(entity) {
  return '<?xml version="1.0" encoding="utf-8" standalone="no"?>'
    + '<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">'
    + '<edmx:DataServices>'
    + '<Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">'
    + '<EntityType Name="' + entity + '">'
    + '<Key><PropertyRef Name="Id" /></Key>'
    + '<Property Name="Id" Type="Edm.Guid" Nullable="false" />'
    + '<Property Name="Name" Type="Edm.String" />'
    + '</EntityType>'
    + '</Schema>'
    + '</edmx:DataServices></edmx:Edmx>';
}

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
    if (request.method === "GET" && url === "{{RecordedRequestsPath}}") {
      // The stub's own introspection endpoint: deliberately NOT recorded, so reading it does not
      // perturb what the test is asserting about.
      sendJson(response, 200, recordedRequests);
      return;
    }
    recordedRequests.push({ method: request.method, url: url });
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
    if (config.DesignerHtmlMode && request.method === "POST"
      && url.endsWith("/ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem")) {
      // Issue #722: the designer answers a schema the target package cannot reach with an HTML page. Three
      // shapes are served, because clio must classify them differently: a server-error page, a sign-in page
      // (an expired session, which says nothing about packages), and a bare markup fragment.
      response.writeHead(200, { "Content-Type": "text/html" });
      if (config.DesignerHtmlMode === "{{DesignerHtmlLoginPage}}") {
        response.end("<!DOCTYPE html><html><head><title>Creatio</title></head><body>"
          + "<form action=\"/Login/NuiLogin.aspx\">{{DesignerHtmlBodyMarker}}</form></body></html>");
        return;
      }
      if (config.DesignerHtmlMode === "{{DesignerHtmlFragment}}") {
        response.end("<div>Request blocked by the gateway. {{DesignerHtmlBodyMarker}}</div>");
        return;
      }
      response.end("<!DOCTYPE html><html><head><title>Runtime Error</title></head><body>"
        + "Server Error in '/' Application. {{DesignerHtmlBodyMarker}}</body></html>");
      return;
    }
    if (config.DesignerPackageName && !config.AuthRejectedSelectQuerySchemaName && !config.HtmlSelectQuerySchemaName
      && request.method === "POST"
      && (url === "/DataService/json/SyncReply/SelectQuery" || url === "/0/DataService/json/SyncReply/SelectQuery")) {
      // The designer scenario needs three reads answered with real rows: the package the request is scoped
      // to, the packages that contribute the schema, and the installed applications used to rank them.
      if (body.includes('"SysPackage"')) {
        sendJson(response, 200, { success: true, rows: [
          { Id: "1", Name: config.DesignerPackageName, UId: "aaaaaaaa-0000-0000-0000-000000000001", Maintainer: "Customer", Version: "1.0.0" },
          { Id: "2", Name: "StubOwnerApp", UId: "aaaaaaaa-0000-0000-0000-000000000002", Maintainer: "Creatio", Version: "1.0.0" },
          { Id: "3", Name: "StubCoreOwner", UId: "aaaaaaaa-0000-0000-0000-000000000003", Maintainer: "Creatio", Version: "1.0.0" }
        ] });
        return;
      }
      if (body.includes('"SysSchema"')) {
        sendJson(response, 200, { success: true, rows: [
          { Name: config.DesignerSchemaName, UId: "bbbbbbbb-0000-0000-0000-000000000001", PackageName: "StubOwnerApp", PackageMaintainer: "Creatio", ParentSchemaName: null },
          { Name: config.DesignerSchemaName, UId: "bbbbbbbb-0000-0000-0000-000000000002", PackageName: "StubCoreOwner", PackageMaintainer: "Creatio", ParentSchemaName: null }
        ] });
        return;
      }
      if (body.includes('"SysInstalledApp"')) {
        sendJson(response, 200, { success: true, rows: [{ Id: "9", Code: "StubOwnerApp", Name: "Stub Owner App" }] });
        return;
      }
    }
    if (config.DesignerPackageName && request.method === "POST"
      && url.endsWith("/ServiceModel/PackageService.svc/GetPackageProperties")) {
      sendJson(response, 200, { success: true, package: {
        uId: "aaaaaaaa-0000-0000-0000-000000000001",
        name: config.DesignerPackageName,
        dependsOnPackages: []
      } });
      return;
    }
    if (request.method === "POST"
      && (url === "/DataService/json/SyncReply/SelectQuery" || url === "/0/DataService/json/SyncReply/SelectQuery")
      && config.AuthRejectedSelectQuerySchemaName
      && body.includes('"' + config.AuthRejectedSelectQuerySchemaName + '"')) {
      // Issue #1222: an expired password makes Creatio answer the authenticated SelectQuery with a
      // DataService fault envelope (ErrorCode 5) under HTTP 200. The repository provider collapses that
      // to an empty successful collection, which is the false-success this PR removes. Keyed on the
      // queried schema so the runtime-detection probe (SysAdminUnit) still gets valid JSON and
      // environment registration is unaffected.
      sendJson(response, 200, {
        responseStatus: { ErrorCode: "5", Message: "Your password has expired.", Errors: [] },
        rows: [],
        success: false
      });
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
    if (config.ODataPreWriteMode && config.ODataEntity) {
      const isMetadata = request.method === "GET" && url.endsWith("/odata/$metadata");
      const isKeyedProbe = request.method === "GET"
        && url.includes("/odata/" + config.ODataEntity + "(")
        && url.includes("$select=");
      const isPatch = request.method === "PATCH" && url.includes("/odata/" + config.ODataEntity + "(");
      if (isMetadata && config.ODataPreWriteMode === "{{ODataPreWriteMetadata}}") {
        response.writeHead(200, { "Content-Type": "application/xml" });
        response.end(metadataCsdl(config.ODataEntity));
        return;
      }
      if (isMetadata && config.ODataPreWriteMode === "{{ODataPreWriteEmptyRecord}}") {
        // Not a CSDL document, so the CSDL validator cannot answer and the tool degrades to the probe.
        response.writeHead(200, { "Content-Type": "text/html" });
        response.end("<!DOCTYPE html><html><head><title>404 - File or directory not found.</title></head></html>");
        return;
      }
      if (isKeyedProbe && config.ODataPreWriteMode === "{{ODataPreWriteEmptyRecord}}") {
        // Valid JSON that proves nothing: no Id, no selected column, no error shape.
        sendJson(response, 200, {});
        return;
      }
      if ((isMetadata || isKeyedProbe) && config.ODataPreWriteMode === "{{ODataPreWriteUnverified}}") {
        // Neither a CSDL document nor any recognized JSON error shape: the pre-write validation can
        // neither confirm nor refute the payload, which is the "unverified" envelope under test.
        sendText(response, 200,
          "IIS: the request could not be mapped to an application. {{ODataPreWriteUnverifiedBodyMarker}}"
            + " See http://admin:{{ODataPreWriteUnverifiedSecret}}@{{ODataPreWriteUnverifiedHost}}:80/trace for details.");
        return;
      }
      if (isKeyedProbe) {
        // The record the $select probe addressed, echoed back with the OData context annotation and
        // EVERY column the probe selected - what a conforming service answers, and what the probe now
        // requires as proof that those fields exist.
        const selected = decodeURIComponent(url.split("$select=")[1].split("&")[0]).split(",");
        const record = {
          "@odata.context": "http://127.0.0.1/odata/$metadata#" + config.ODataEntity,
          Id: "00000000-0000-0000-0000-000000000001"
        };
        for (const column of selected) {
          if (column && column !== "Id") {
            record[column] = "probe";
          }
        }
        sendJson(response, 200, record);
        return;
      }
      if (isPatch) {
        // Empty 204 ack, the shape Creatio returns for a successful PATCH.
        response.writeHead(204);
        response.end();
        return;
      }
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
	string? ODataEntity = null,
	string? ODataPreWriteMode = null,
	string? AuthRejectedSelectQuerySchemaName = null,
	string? DesignerHtmlMode = null,
	string? DesignerPackageName = null,
	string? DesignerSchemaName = null);

/// <summary>
/// One request served by <see cref="RuntimeDetectionStubServer"/>, as reported by
/// <see cref="RuntimeDetectionStubServer.RecordedRequestsPath"/>.
/// </summary>
/// <param name="Method">HTTP method of the request.</param>
/// <param name="Url">Request URL, path and query, as the stub received it.</param>
internal sealed record RecordedStubRequest(string Method, string Url);
