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
/// Hermetic happy-path end-to-end coverage for the upload-image MCP tool: the real clio MCP server
/// resolves a registered environment (isolated clio home) that points at a loopback fake of the
/// Creatio image surface (forms-auth login with the modern CSRF cookie, upload endpoint, verification
/// read), so the full
/// login → upload → byte-verified read flow runs without a live Creatio environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("upload-image")]
[NonParallelizable]
public sealed class UploadImageHappyPathE2ETests : McpContractFixtureBase {
	private const string StubEnvironmentName = "upload-image-stub";

	// Suppressed: the stub must start inside ConfigureMcpServerSettings (its URI goes into the child
	// process appsettings before the shared server starts), which the analyzer cannot track; it IS
	// disposed in the [OneTimeTearDown] StopStubAsync below.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
		Justification = "The system under test owns and disposes the factory-returned substitute.")]
	private FakeCreatioImageStub? _stub;
	private string? _imageFilePath;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_stub = FakeCreatioImageStub.Start();
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
		string fixtureDirectory = CreateFixtureDirectory("upload-image-payload");
		_imageFilePath = Path.Combine(fixtureDirectory, "background.png");
		File.WriteAllBytes(_imageFilePath, FakeCreatioImageStub.PngPayload);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	[OneTimeTearDown]
	public async Task StopStubAsync() {
		if (_stub is not null) {
			await _stub.DisposeAsync();
		}
	}

	[Test]
	[AllureTag(UploadImageTool.ToolName)]
	[AllureName("upload-image forwards the authenticated session and modern CSRF token end to end")]
	[AllureDescription("Registers an isolated environment pointing at a loopback fake that requires both the forms session cookie and matching CRT_CSRF header, calls upload-image through the real clio MCP server, and verifies the structured result plus the login → upload → authenticated byte-read sequence.")]
	[Description("Requires the real MCP upload path to forward the forms cookie and matching modern CSRF header before byte-verifying the stored image.")]
	public async Task UploadImage_Should_Upload_And_Verify_Image_When_Environment_Is_Reachable() {
		// Arrange
		_stub!.RequireCsrf = true;
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UploadImageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = StubEnvironmentName,
					["file"] = _imageFilePath
				}
			},
			context.CancellationTokenSource.Token);
		UploadImageResult result = EntitySchemaStructuredResultParser.Extract<UploadImageResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful upload must come back as a structured result, not an MCP protocol error");
		result.Success.Should().BeTrue(
			because: "the fake accepted the upload and served the exact bytes back on the verification read");
		Guid.TryParse(result.ImageId, out Guid imageId).Should().BeTrue(
			because: "the created image id is the value the caller chains into set-background-image");
		_stub!.UploadedFileId.Should().Be(imageId,
			because: "the reported image id must be the fileId the tool uploaded under");
		_stub.VerifiedFileId.Should().Be(imageId,
			because: "the tool must read the same image back to prove the binary persisted");
		_stub.UploadedBytes.Should().Equal(FakeCreatioImageStub.PngPayload,
			because: "the uploaded payload must be the exact file content");
	}

	[Test]
	[AllureTag(UploadImageTool.ToolName)]
	[AllureName("upload-image supports forms sessions on Creatio environments without CSRF tokens")]
	[AllureDescription("Runs upload-image through the real MCP server against a loopback Creatio surface that issues only .ASPXAUTH and verifies that no fabricated CSRF cookie or header is required.")]
	[Description("Requires the real MCP upload path to preserve compatibility with forms-auth environments that do not issue a CSRF token.")]
	public async Task UploadImage_Should_Upload_And_Verify_Image_When_CsrfTokenIsAbsent() {
		// Arrange
		_stub!.RequireCsrf = false;
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UploadImageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = StubEnvironmentName,
					["file"] = _imageFilePath
				}
			},
			context.CancellationTokenSource.Token);
		UploadImageResult result = EntitySchemaStructuredResultParser.Extract<UploadImageResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a tokenless forms session is a supported Creatio authentication shape");
		result.Success.Should().BeTrue(
			because: "the .ASPXAUTH-only session must complete upload and byte verification");
		_stub.SawUnexpectedCsrf.Should().BeFalse(
			because: "the client must not invent a CSRF cookie or header when the server did not issue one");
	}

	/// <summary>
	/// Loopback fake of the Creatio surface the upload needs: forms-auth login on a CSRF-disabled
	/// environment (issues only the session cookie), the image upload endpoint, and the verification
	/// read that serves the exact uploaded bytes back.
	/// </summary>
	private sealed class FakeCreatioImageStub : IAsyncDisposable {
		private const string SessionCookie = ".ASPXAUTH=stub-session";
		private const string CsrfCookie = "CRT_CSRF=stub-csrf";
		public static readonly byte[] PngPayload = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;

		private FakeCreatioImageStub(HttpListener listener, string applicationUri) {
			_listener = listener;
			ApplicationUri = applicationUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string ApplicationUri { get; }

		public Guid UploadedFileId { get; private set; }

		public Guid VerifiedFileId { get; private set; }

		public byte[]? UploadedBytes { get; private set; }

		public bool RequireCsrf { get; set; } = true;

		public bool SawUnexpectedCsrf { get; private set; }

		public static FakeCreatioImageStub Start() {
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20_000, 60_000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new FakeCreatioImageStub(listener, $"http://127.0.0.1:{port}");
				} catch (HttpListenerException) {
					listener.Close();
				}
			}
			throw new InvalidOperationException("Unable to start the Creatio image-surface loopback fake.");
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
			} else if (path.EndsWith("/ImageAPIService/upload", StringComparison.Ordinal)) {
				bool hasCsrfCookie = HasCookie(context.Request, CsrfCookie);
				bool hasCsrfHeader = context.Request.Headers["CRT_CSRF"] is not null;
				SawUnexpectedCsrf = !RequireCsrf && (hasCsrfCookie || hasCsrfHeader);
				if (!HasCookie(context.Request, SessionCookie) || RequireCsrf &&
					(!hasCsrfCookie || context.Request.Headers["CRT_CSRF"] != "stub-csrf")) {
					context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
					bytes = Encoding.UTF8.GetBytes("missing authenticated CSRF session");
				} else {
				string? fileId = context.Request.QueryString["fileId"];
				UploadedFileId = Guid.TryParse(fileId, out Guid parsed) ? parsed : Guid.Empty;
				using MemoryStream buffer = new();
				await context.Request.InputStream.CopyToAsync(buffer).ConfigureAwait(false);
				UploadedBytes = buffer.ToArray();
				context.Response.ContentType = "application/json";
				bytes = Encoding.UTF8.GetBytes("{\"success\":true}");
				}
			} else if (path.Contains("/img/entity/hash/SysImage/Data/", StringComparison.Ordinal)) {
				if (!HasCookie(context.Request, SessionCookie)) {
					context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
					bytes = Encoding.UTF8.GetBytes("missing authenticated session");
				} else {
				string idSegment = path[(path.LastIndexOf('/') + 1)..];
				VerifiedFileId = Guid.TryParse(idSegment, out Guid parsed) ? parsed : Guid.Empty;
				context.Response.ContentType = "image/png";
				bytes = UploadedBytes ?? [];
				}
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				context.Response.ContentType = "text/plain";
				bytes = Encoding.UTF8.GetBytes("Not Found");
			}
			context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			context.Response.Close();
		}

		private static bool HasCookie(HttpListenerRequest request, string expected) =>
			(request.Headers["Cookie"] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
				.Any(value => value.Trim().Equals(expected, StringComparison.Ordinal));
	}
}
