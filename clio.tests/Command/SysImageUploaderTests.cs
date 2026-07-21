namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SysImageUploaderTests
{
	private static readonly byte[] PngPayload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

	private sealed class RecordingHandler : HttpMessageHandler {
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

		public List<HttpRequestMessage> Requests { get; } = [];

		public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			Requests.Add(request);
			return Task.FromResult(_responder(request));
		}
	}

	private static HttpResponseMessage Ok(string body = "{\"success\":true}") =>
		new(HttpStatusCode.OK) { Content = new StringContent(body) };

	private static HttpResponseMessage OkBytes(byte[] payload) =>
		new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

	// Default responder for the happy path: the upload POST answers success, the verification GET
	// serves the exact uploaded bytes back (mirroring the live img/entity read endpoint).
	private static HttpResponseMessage RespondUploadThenEcho(HttpRequestMessage request) =>
		request.Method == HttpMethod.Post ? Ok() : OkBytes(PngPayload);

	private static StorageStateResult Session(bool withCsrf = true) {
		List<BrowserCookie> cookies = [
			new(".ASPXAUTH", "auth-token", "dev.creatio.com", "/", true, false, "Lax", -1)
		];
		if (withCsrf) {
			cookies.Add(new BrowserCookie("BPMCSRF", "csrf-token", "dev.creatio.com", "/", false, false, "Lax", -1));
		}
		return new StorageStateResult(cookies);
	}

	private static (SysImageUploader sut, RecordingHandler handler, IFileSystem fileSystem) BuildSut(
		bool isNetCore, Func<HttpRequestMessage, HttpResponseMessage> responder = null,
		StorageStateResult session = null) {
		RecordingHandler handler = new(responder ?? RespondUploadThenEcho);
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(SysImageUploader.HttpClientName).Returns(_ => new HttpClient(handler));
		ICreatioAuthClient authClient = Substitute.For<ICreatioAuthClient>();
		authClient.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(session ?? Session());
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("C:/brand/background.png").Returns(true);
		fileSystem.GetFileSize("C:/brand/background.png").Returns(PngPayload.LongLength);
		fileSystem.ReadAllBytes("C:/brand/background.png").Returns(PngPayload);
		EnvironmentSettings settings = new() {
			Uri = "https://dev.creatio.com/", Login = "Supervisor", Password = "Supervisor", IsNetCore = isNetCore
		};
		SysImageUploader sut = new(settings, authClient, factory, fileSystem);
		return (sut, handler, fileSystem);
	}

	[Test]
	[Description("On a .NET Framework environment the upload posts to the /0-prefixed image API with the fileId, totalFileLength, and mimeType query values, the inclusive Content-Range, and the BPMCSRF header, then verifies the image through the read endpoint and returns the fileId it generated.")]
	public async Task UploadAsync_ShouldPostToPrefixedImageApiAndVerify_WhenNetFrameworkEnvironment() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, _) = BuildSut(isNetCore: false);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeTrue(because: "the upload returned 2xx and the verification read returned 2xx");
		handler.Requests.Should().HaveCount(2, because: "the uploader sends one upload POST and one verification GET");
		HttpRequestMessage upload = handler.Requests[0];
		upload.Method.Should().Be(HttpMethod.Post, because: "the image API accepts the payload via POST");
		upload.RequestUri!.AbsolutePath.Should().Be("/0/ImageAPIService/upload",
			because: "on .NET Framework the image API is served under the /0 WebAppAlias, with no /rest/ segment");
		string query = upload.RequestUri.Query;
		query.Should().StartWith("?fileapi", because: "the platform upload URL carries the fileapi cache-buster fragment first");
		query.Should().Contain($"totalFileLength={PngPayload.Length}",
			because: "the image API needs the total payload length to accept the single-request upload");
		query.Should().Contain($"fileId={result.ImageId}",
			because: "the client-generated fileId becomes the created SysImage record id and must match the reported result");
		query.Should().Contain("mimeType=image%2Fpng", because: "the mime type is URL-encoded into the query");
		upload.Headers.GetValues("BPMCSRF").Single().Should().Be("csrf-token",
			because: "the CSRF token from the login cookies must accompany the write");
		upload.Content!.Headers.ContentRange!.ToString().Should().Be(
			$"bytes 0-{PngPayload.Length - 1}/{PngPayload.Length}",
			because: "the File API range end is zero-indexed and inclusive, so a 10-byte file is bytes 0-9/10");
		upload.Content.Headers.ContentType!.MediaType.Should().Be("image/png",
			because: "the content type must match the uploaded file's mime type");
		upload.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment",
			because: "the image API expects an attachment content disposition carrying the file name");
		HttpRequestMessage verify = handler.Requests[1];
		verify.Method.Should().Be(HttpMethod.Get, because: "verification reads the image back");
		verify.RequestUri!.AbsolutePath.Should().Be($"/0/img/entity/hash/SysImage/Data/{result.ImageId}",
			because: "the read endpoint (literal 'hash' segment) is the authoritative proof the SysImage binary persisted");
	}

	[Test]
	[Description("On a .NET Core environment the upload posts to the image API at the site root — no /0 WebAppAlias segment.")]
	public async Task UploadAsync_ShouldPostToRootImageApi_WhenNetCoreEnvironment() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, _) = BuildSut(isNetCore: true);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeTrue(because: "the runtime-aware URL must work on .NET Core too");
		handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/ImageAPIService/upload",
			because: ".NET Core environments serve the image API at the site root with no /0 alias");
		handler.Requests[1].RequestUri!.AbsolutePath.Should().Be($"/img/entity/hash/SysImage/Data/{result.ImageId}",
			because: "the verification read must use the same runtime-aware root");
	}

	[Test]
	[Description("Fails with a file-not-found message, without logging in or making any HTTP request, when the file does not exist.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileDoesNotExist() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, IFileSystem fileSystem) = BuildSut(isNetCore: false);
		fileSystem.ExistsFile("C:/missing.png").Returns(false);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/missing.png");

		// Assert
		result.Success.Should().BeFalse(because: "a missing file cannot be uploaded");
		result.Error.Should().Contain("File not found", because: "the failure must name the actionable cause");
		handler.Requests.Should().BeEmpty(because: "validation failures must not reach the network");
	}

	[Test]
	[Description("Fails with an unsupported-extension message listing the supported formats when the file extension has no image mime mapping.")]
	public async Task UploadAsync_ShouldFailFast_WhenExtensionIsNotAnImage() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, IFileSystem fileSystem) = BuildSut(isNetCore: false);
		fileSystem.ExistsFile("C:/brand/logo.pdf").Returns(true);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/logo.pdf");

		// Assert
		result.Success.Should().BeFalse(because: "only image formats are accepted");
		result.Error.Should().Contain("Unsupported image extension", because: "the failure must name the rejected extension");
		result.Error.Should().Contain(".webp", because: "the failure must list the supported formats so the caller can recover");
		result.Error.Should().NotContain(".svg", because: "svg is not supported by the other branding surfaces and must not be advertised");
		handler.Requests.Should().BeEmpty(because: "validation failures must not reach the network");
	}

	[Test]
	[Description("Fails on an empty file, since a zero-length payload cannot carry a valid inclusive Content-Range.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileIsEmpty() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, IFileSystem fileSystem) = BuildSut(isNetCore: false);
		fileSystem.GetFileSize("C:/brand/background.png").Returns(0L);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "an empty file is not a valid image");
		result.Error.Should().Contain("empty", because: "the failure must name the actionable cause");
		handler.Requests.Should().BeEmpty(because: "validation failures must not reach the network");
	}

	[Test]
	[Description("Fails when the file exceeds the shared branding size cap, without reading the payload or reaching the network.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileExceedsSizeCap() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, IFileSystem fileSystem) = BuildSut(isNetCore: false);
		fileSystem.GetFileSize("C:/brand/background.png").Returns(SysImageUploader.MaxImageBytes + 1);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "the cap guards against a mistaken large file exhausting memory");
		result.Error.Should().Contain("byte limit", because: "the failure must name the enforced cap");
		fileSystem.DidNotReceive().ReadAllBytes(Arg.Any<string>());
		handler.Requests.Should().BeEmpty(because: "validation failures must not reach the network");
	}

	[Test]
	[Description("Fails with the server's message when the image API returns 2xx with success=false (e.g. the file-security policy rejected the file).")]
	public async Task UploadAsync_ShouldFail_WhenImageApiReportsSuccessFalse() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, _) = BuildSut(isNetCore: false,
			responder: _ => Ok("{\"success\":false,\"errorInfo\":{\"message\":\"File type is not allowed.\"}}"));

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "a 2xx body carrying success=false is a server-side rejection");
		result.Error.Should().Contain("File type is not allowed",
			because: "the server-supplied reason is the actionable message for the caller");
		handler.Requests.Should().HaveCount(1, because: "a rejected upload must not be verified");
	}

	[Test]
	[Description("Fails when the verification read of the uploaded image does not return 2xx, so a silently-empty SysImage record is never reported as success.")]
	public async Task UploadAsync_ShouldFail_WhenVerificationReadFails() {
		// Arrange
		(SysImageUploader sut, _, _) = BuildSut(isNetCore: false,
			responder: request => request.Method == HttpMethod.Post
				? Ok()
				: new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) });

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "an upload that cannot be read back did not persist");
		result.Error.Should().Contain("could not be verified", because: "the failure must say the persistence check failed");
	}

	[Test]
	[Description("Fails when the verification read returns 2xx with content that differs from the uploaded bytes — an expired session answers HTTP 200 with the login-page HTML, so a status-only check would report a false success.")]
	public async Task UploadAsync_ShouldFail_WhenVerificationReadReturnsDifferentBytes() {
		// Arrange
		(SysImageUploader sut, _, _) = BuildSut(isNetCore: false,
			responder: request => request.Method == HttpMethod.Post
				? Ok()
				: Ok("<html>login page</html>"));

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(
			because: "a 200 response whose body is not the uploaded image is not proof of persistence");
		result.Error.Should().Contain("does not match the uploaded file",
			because: "the failure must say the byte comparison failed so the caller does not trust a phantom image");
	}

	[Test]
	[Description("Surfaces a PascalCase server rejection ({\"Success\":false,\"ErrorInfo\":{...}}) the same as the camelCase shape, since the image API's property casing is not a documented contract.")]
	public async Task UploadAsync_ShouldFail_WhenImageApiReportsSuccessFalse_InPascalCase() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, _) = BuildSut(isNetCore: false,
			responder: _ => Ok("{\"Success\":false,\"ErrorInfo\":{\"Message\":\"File type is not allowed.\"}}"));

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "a 2xx body carrying Success=false is a server-side rejection regardless of casing");
		result.Error.Should().Contain("File type is not allowed",
			because: "the server-supplied reason must survive a PascalCase response shape");
		handler.Requests.Should().HaveCount(1, because: "a rejected upload must not be verified");
	}

	[Test]
	[Description("A 2xx upload response with a non-JSON body (e.g. a truncated payload or an unexpected plain-text response) is not treated as a hard rejection: the JsonException is swallowed and the flow proceeds to the authoritative verification read, which byte-verifies persistence.")]
	public async Task UploadAsync_ShouldProceedToVerification_WhenUploadBodyIsNotJson() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, _) = BuildSut(isNetCore: false,
			responder: request => request.Method == HttpMethod.Post
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json {truncated") }
				: OkBytes(PngPayload));

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeTrue(
			because: "an unparseable 2xx upload body is not a confirmed rejection — the byte-verified read is the authoritative persistence proof");
		handler.Requests.Should().HaveCount(2,
			because: "the flow must fall through the JSON parse to the verification GET rather than failing on the parse");
		handler.Requests[1].Method.Should().Be(HttpMethod.Get,
			because: "the second request is the verification read that actually proves persistence");
	}

	[Test]
	[Description("Fails when the file grows past the size cap between the size probe and the read, so the cap cannot be raced (bounded-read discipline shared with the Binary sys-setting upload path).")]
	public async Task UploadAsync_ShouldFail_WhenFileGrowsPastCapBetweenProbeAndRead() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, IFileSystem fileSystem) = BuildSut(isNetCore: false);
		fileSystem.GetFileSize("C:/brand/background.png").Returns(PngPayload.LongLength);
		fileSystem.ReadAllBytes("C:/brand/background.png").Returns(new byte[SysImageUploader.MaxImageBytes + 1]);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "the cap must hold against a file that changed after the size probe");
		result.Error.Should().Contain("changed while reading",
			because: "the failure must name the race so the caller retries with a stable file");
		handler.Requests.Should().BeEmpty(because: "an over-cap payload must never reach the network");
	}

	[Test]
	[Description("Fails with an explicit message when the login response carries no BPMCSRF cookie, since the image API rejects CSRF-less writes.")]
	public async Task UploadAsync_ShouldFail_WhenLoginCarriesNoCsrfCookie() {
		// Arrange
		(SysImageUploader sut, RecordingHandler handler, _) = BuildSut(isNetCore: false, session: Session(withCsrf: false));

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "the image API write cannot be made without the CSRF token");
		result.Error.Should().Contain("BPMCSRF", because: "the failure must name the missing prerequisite");
		handler.Requests.Should().BeEmpty(because: "without the CSRF token no request is attempted");
	}

	[Test]
	[Description("Surfaces an authentication failure (e.g. OAuth-only environment without forms credentials) as a structured failure message instead of an exception.")]
	public async Task UploadAsync_ShouldFail_WhenAuthenticationFails() {
		// Arrange
		RecordingHandler handler = new(_ => Ok());
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(SysImageUploader.HttpClientName).Returns(_ => new HttpClient(handler));
		ICreatioAuthClient authClient = Substitute.For<ICreatioAuthClient>();
		authClient.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<StorageStateResult>(
				CreatioAuthenticationException.MissingFormsCredentials("https://dev.creatio.com")));
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("C:/brand/background.png").Returns(true);
		fileSystem.GetFileSize("C:/brand/background.png").Returns(PngPayload.LongLength);
		fileSystem.ReadAllBytes("C:/brand/background.png").Returns(PngPayload);
		EnvironmentSettings settings = new() { Uri = "https://dev.creatio.com", IsNetCore = false };
		SysImageUploader sut = new(settings, authClient, factory, fileSystem);

		// Act
		SysImageUploadResult result = await sut.UploadAsync("C:/brand/background.png");

		// Assert
		result.Success.Should().BeFalse(because: "an unauthenticated upload cannot proceed");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the sanitized authentication message must reach the caller");
		handler.Requests.Should().BeEmpty(because: "no image-API request is attempted without a session");
	}
}
