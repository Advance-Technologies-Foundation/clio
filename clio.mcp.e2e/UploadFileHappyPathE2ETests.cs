using System.Collections.Specialized;
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
/// Hermetic happy-path end-to-end coverage for the upload-file MCP tool. The real clio MCP server resolves
/// a registered environment (isolated clio home) that points at a loopback fake of the Creatio file
/// surface: forms-auth login with the modern CSRF cookie, the <c>FileApiService/UploadFile</c> endpoint,
/// and the OData read of the stored <c>Data</c> column. The full login → upload → byte-verified read flow
/// runs without a live Creatio environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("upload-file")]
[NonParallelizable]
public sealed class UploadFileHappyPathE2ETests : McpContractFixtureBase {
	private const string StubEnvironmentName = "upload-file-stub";
	private const string Entity = "ContactFile";
	private const string ParentColumn = "Contact";
	private const string ParentId = "410006e1-ca4e-4502-a9ec-e54d922d2c00";

	// Suppressed: the stub must start inside ConfigureMcpServerSettings (its URI goes into the child
	// process appsettings before the shared server starts), which the analyzer cannot track; it IS
	// disposed in the [OneTimeTearDown] StopStubAsync below.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
		Justification = "The stub is disposed in the OneTimeTearDown StopStubAsync.")]
	private FakeCreatioFileStub? _stub;
	private string? _filePath;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_stub = FakeCreatioFileStub.Start();
		string clioHome = CreateIsolatedClioHome(
			$$"""
			{
			  "ActiveEnvironmentKey": "{{StubEnvironmentName}}",
			  "Autoupdate": false,
			  "Environments": {
			    "{{StubEnvironmentName}}": {
			      "Uri": "{{_stub.ApplicationUri}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			GetType().Name);
		string fixtureDirectory = CreateFixtureDirectory("upload-file-payload");
		_filePath = Path.Combine(fixtureDirectory, "contract.txt");
		File.WriteAllBytes(_filePath, FakeCreatioFileStub.TextPayload);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	[OneTimeTearDown]
	public async Task StopStubAsync() {
		if (_stub is not null) {
			await _stub.DisposeAsync();
		}
	}

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file sends the file to the file API, attaches it to the parent record and verifies the bytes")]
	[AllureDescription("Registers an isolated environment pointing at a loopback fake that requires the forms session cookie and the matching CRT_CSRF header, calls upload-file through the real clio MCP server, and verifies the structured result, the query parameters of the UploadFile call, and the byte-verified OData read of the Data column.")]
	[Description("Requires the real MCP upload-file path to call FileApiService/UploadFile with the file entity, the parent column and record, and the Data column, and to report the fileId it read back byte for byte.")]
	public async Task UploadFile_Should_Upload_Attach_And_Verify_File_When_Environment_Is_Reachable() {
		// Arrange
		_stub!.RequireCsrf = true;
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await CallUploadAsync(context);
		UploadFileResult result = EntitySchemaStructuredResultParser.Extract<UploadFileResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful upload must come back as a structured result, not an MCP protocol error");
		result.Success.Should().BeTrue(
			because: $"the fake accepted the upload and served the exact bytes back on the verification read: {result.Error}");
		Guid.TryParse(result.FileId, out Guid fileId).Should().BeTrue(
			because: "the created file id is the value the caller uses to address the file record");
		NameValueCollection query = _stub.UploadQuery!;
		query["fileId"].Should().Be(fileId.ToString(),
			because: "the reported file id must be the fileId the tool uploaded under, which becomes the record id");
		query["entitySchemaName"].Should().Be(Entity,
			because: "the file API stores the file in the file entity the caller named");
		query["parentColumnName"].Should().Be(ParentColumn,
			because: "the file API links the record through the lookup column the caller named");
		query["parentColumnValue"].Should().Be(ParentId,
			because: "the file API links the record to the parent record the caller named");
		query["columnName"].Should().Be("Data",
			because: "the bytes belong in the binary Data column of the file entity");
		query["fileName"].Should().Be("contract.txt",
			because: "the stored file keeps the local file name");
		_stub.UploadedBytes.Should().Equal(FakeCreatioFileStub.TextPayload,
			because: "the uploaded payload must be the exact file content");
		_stub.VerifiedFileId.Should().Be(fileId,
			because: "the tool must read the same record back through OData to prove the binary persisted");
	}

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file supports forms sessions on Creatio environments without CSRF tokens")]
	[AllureDescription("Runs upload-file through the real MCP server against a loopback Creatio surface that issues only .ASPXAUTH and verifies that no fabricated CSRF cookie or header is required.")]
	[Description("Requires the real MCP upload-file path to work with forms-auth environments that do not issue a CSRF token.")]
	public async Task UploadFile_Should_Upload_And_Verify_File_When_CsrfTokenIsAbsent() {
		// Arrange
		_stub!.RequireCsrf = false;
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await CallUploadAsync(context);
		UploadFileResult result = EntitySchemaStructuredResultParser.Extract<UploadFileResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a tokenless forms session is a supported Creatio authentication shape");
		result.Success.Should().BeTrue(
			because: $"the .ASPXAUTH-only session must complete upload and byte verification: {result.Error}");
		_stub.SawUnexpectedCsrf.Should().BeFalse(
			because: "the client must not invent a CSRF cookie or header when the server did not issue one");
	}

	private Task<CallToolResult> CallUploadAsync(ArrangeContext context) =>
		context.Session.CallToolAsync(
			UploadFileTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = StubEnvironmentName,
					["file"] = _filePath,
					["entity"] = Entity,
					["parent-column"] = ParentColumn,
					["parent-id"] = ParentId
				}
			},
			context.CancellationTokenSource.Token);

	/// <summary>
	/// Loopback fake of the Creatio surface the upload needs: forms-auth login, the
	/// <c>FileApiService/UploadFile</c> endpoint, and the OData read of the stored <c>Data</c> column that
	/// serves the exact uploaded bytes back.
	/// </summary>
	private sealed class FakeCreatioFileStub : IAsyncDisposable {
		private const string SessionCookie = ".ASPXAUTH=stub-session";
		private const string CsrfCookie = "CRT_CSRF=stub-csrf";
		public static readonly byte[] TextPayload = Encoding.UTF8.GetBytes("clio upload-file e2e payload\n");

		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;

		private FakeCreatioFileStub(HttpListener listener, string applicationUri) {
			_listener = listener;
			ApplicationUri = applicationUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string ApplicationUri { get; }

		public NameValueCollection? UploadQuery { get; private set; }

		public Guid VerifiedFileId { get; private set; }

		public byte[]? UploadedBytes { get; private set; }

		public bool RequireCsrf { get; set; } = true;

		public bool SawUnexpectedCsrf { get; private set; }

		public static FakeCreatioFileStub Start() {
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20_000, 60_000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new FakeCreatioFileStub(listener, $"http://127.0.0.1:{port}");
				} catch (HttpListenerException) {
					listener.Close();
				}
			}
			throw new InvalidOperationException("Unable to start the Creatio file-surface loopback fake.");
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

		private async Task RespondAsync(HttpListenerContext context) {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			byte[] bytes;
			if (path.EndsWith("/ServiceModel/AuthService.svc/Login", StringComparison.Ordinal)) {
				context.Response.Headers.Add("Set-Cookie", SessionCookie + "; path=/; HttpOnly; SameSite=Lax");
				if (RequireCsrf) {
					context.Response.Headers.Add("Set-Cookie", CsrfCookie + "; path=/; SameSite=Strict");
				}
				context.Response.ContentType = "application/json";
				bytes = Encoding.UTF8.GetBytes("{\"Code\":0}");
			} else if (context.Request.HttpMethod == "POST"
				&& path.EndsWith("/rest/FileApiService/UploadFile", StringComparison.Ordinal)) {
				bytes = await RespondToUploadAsync(context).ConfigureAwait(false);
			} else if (context.Request.HttpMethod == "GET"
				&& path.Contains($"/odata/{Entity}(", StringComparison.Ordinal)
				&& path.EndsWith(")/Data", StringComparison.Ordinal)) {
				bytes = RespondToRead(context, path);
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				context.Response.ContentType = "text/plain";
				bytes = Encoding.UTF8.GetBytes("Not Found");
			}
			context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			context.Response.Close();
		}

		private async Task<byte[]> RespondToUploadAsync(HttpListenerContext context) {
			bool hasCsrfCookie = HasCookie(context.Request, CsrfCookie);
			bool hasCsrfHeader = context.Request.Headers["CRT_CSRF"] is not null;
			SawUnexpectedCsrf = !RequireCsrf && (hasCsrfCookie || hasCsrfHeader);
			if (!HasCookie(context.Request, SessionCookie) || RequireCsrf &&
				(!hasCsrfCookie || context.Request.Headers["CRT_CSRF"] != "stub-csrf")) {
				context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
				return Encoding.UTF8.GetBytes("missing authenticated CSRF session");
			}
			UploadQuery = new NameValueCollection(context.Request.QueryString);
			using MemoryStream buffer = new();
			await context.Request.InputStream.CopyToAsync(buffer).ConfigureAwait(false);
			UploadedBytes = buffer.ToArray();
			context.Response.ContentType = "application/json";
			return Encoding.UTF8.GetBytes("{\"success\":true,\"errorInfo\":null}");
		}

		private byte[] RespondToRead(HttpListenerContext context, string path) {
			if (!HasCookie(context.Request, SessionCookie)) {
				context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
				return Encoding.UTF8.GetBytes("missing authenticated session");
			}
			int open = path.LastIndexOf('(');
			int close = path.LastIndexOf(')');
			string idSegment = open >= 0 && close > open ? path[(open + 1)..close] : string.Empty;
			VerifiedFileId = Guid.TryParse(idSegment, out Guid parsed) ? parsed : Guid.Empty;
			if (UploadQuery is null || UploadQuery["fileId"] != VerifiedFileId.ToString()) {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				return Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"\",\"message\":\"Not found\"}}");
			}
			context.Response.ContentType = "application/octet-stream";
			return UploadedBytes ?? [];
		}

		private static bool HasCookie(HttpListenerRequest request, string expected) =>
			(request.Headers["Cookie"] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
				.Any(value => value.Trim().Equals(expected, StringComparison.Ordinal));
	}
}
