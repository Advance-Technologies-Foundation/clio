namespace Clio.Tests.Command;

using System;
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
	private const string FilePath = "C:/brand/background.png";
	private const string UploadRoot = "https://dev.creatio.com/0/ImageAPIService/upload";
	private static readonly byte[] PngPayload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

	private static HttpResponseMessage TextResponse(HttpStatusCode statusCode = HttpStatusCode.OK,
		string body = "{\"success\":true}") => new(statusCode) { Content = new StringContent(body) };

	private static HttpResponseMessage BinaryResponse(byte[] payload,
		HttpStatusCode statusCode = HttpStatusCode.OK) =>
		new(statusCode) { Content = new ByteArrayContent(payload) };

	private static (SysImageUploader Sut, IOwnedApplicationClient Client, IApplicationClientFactory Factory,
		EnvironmentSettings Settings, IServiceUrlBuilder UrlBuilder, IFileSystem FileSystem)
		BuildSut(HttpResponseMessage uploadResponse = null,
		HttpResponseMessage verifyResponse = null) {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(TextResponse()));
		client.UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(uploadResponse ?? TextResponse()));
		client.ExecuteGetRequestAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(verifyResponse ?? BinaryResponse(PngPayload)));

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ImageApiUpload).Returns(UploadRoot);
		urlBuilder.Build(Arg.Is<string>(route => route.StartsWith("/img/entity/hash/SysImage/Data/")))
			.Returns(call => "https://dev.creatio.com/0" + call.Arg<string>());

		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(FilePath).Returns(true);
		fileSystem.GetFileSize(FilePath).Returns(PngPayload.LongLength);
		fileSystem.ReadAllBytes(FilePath).Returns(PngPayload);

		EnvironmentSettings settings = new() {
			Uri = "https://dev.creatio.com",
			Login = "Supervisor",
			Password = "secret"
		};
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateFormsEnvironmentClient(settings).Returns(client);

		return (new SysImageUploader(settings, factory, urlBuilder, fileSystem), client, factory,
			settings, urlBuilder, fileSystem);
	}

	[Test]
	[Description("Uploads through IApplicationClient using the known Image API route and verifies the exact bytes.")]
	public async Task UploadAsync_ShouldUseApplicationClientAndKnownRoute_WhenImageIsValid() {
		// Arrange
		(SysImageUploader sut, IOwnedApplicationClient client, IApplicationClientFactory factory,
			EnvironmentSettings settings, IServiceUrlBuilder urlBuilder, _) = BuildSut();

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeTrue(because: "the Image API and byte-for-byte verification both succeeded");
		factory.Received(1).CreateFormsEnvironmentClient(settings);
		await client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
		await client.Received(1).UploadImageAsync(
			Arg.Is<string>(url => url.StartsWith(UploadRoot + "?fileapi", StringComparison.Ordinal)
				&& url.Contains($"totalFileLength={PngPayload.Length}")
				&& url.Contains($"fileId={result.ImageId}")
				&& url.Contains("mimeType=image%2Fpng")),
			Arg.Is<byte[]>(bytes => Enumerable.SequenceEqual(bytes, PngPayload)), "background.png", "image/png",
			100_000, Arg.Any<CancellationToken>());
		urlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.ImageApiUpload);
		await client.Received(1).ExecuteGetRequestAsync(
			Arg.Is<string>(url => url.EndsWith($"/img/entity/hash/SysImage/Data/{result.ImageId}",
				StringComparison.Ordinal)), 100_000, 1, 1, Arg.Any<CancellationToken>());
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Forwards a Unicode file name unchanged so CreatioClient owns its Image API header encoding.")]
	public async Task UploadAsync_ShouldForwardUnicodeFileName_WhenPathContainsUnicode() {
		// Arrange
		const string unicodePath = "C:/brand/логотип.png";
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.ExistsFile(unicodePath).Returns(true);
		fileSystem.GetFileSize(unicodePath).Returns(PngPayload.LongLength);
		fileSystem.ReadAllBytes(unicodePath).Returns(PngPayload);

		// Act
		SysImageUploadResult result = await sut.UploadAsync(unicodePath);

		// Assert
		result.Success.Should().BeTrue(because: "CreatioClient accepts and encodes the original file name");
		await client.Received(1).UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), "логотип.png",
			"image/png", 100_000, Arg.Any<CancellationToken>());
	}

	[TestCase("C:/missing.png", "File not found")]
	[TestCase("C:/brand/logo.pdf", "Unsupported image extension")]
	[Description("Rejects invalid local input before any Creatio request is made.")]
	public async Task UploadAsync_ShouldFailFast_WhenLocalInputIsInvalid(string path, string expectedError) {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.ExistsFile(path).Returns(path.EndsWith(".pdf", StringComparison.Ordinal));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(path);

		// Assert
		result.Success.Should().BeFalse(because: "the local file cannot be sent safely");
		result.Error.Should().Contain(expectedError, because: "the caller needs the actionable validation cause");
		await client.DidNotReceiveWithAnyArgs().UploadImageAsync(default, default, default, default, default, default);
	}

	[Test]
	[Description("Rejects an empty image before reading or sending its contents.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileIsEmpty() {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.GetFileSize(FilePath).Returns(0);

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "an empty payload cannot become a valid SysImage record");
		result.Error.Should().Contain("empty", because: "the local validation cause should be actionable");
		fileSystem.DidNotReceive().ReadAllBytes(Arg.Any<string>());
		await client.DidNotReceiveWithAnyArgs().UploadImageAsync(default, default, default, default, default, default);
	}

	[TestCase(HttpStatusCode.BadRequest)]
	[TestCase(HttpStatusCode.InternalServerError)]
	[Description("Reports a non-success verification read even when the upload endpoint accepted the payload.")]
	public async Task UploadAsync_ShouldFail_WhenVerificationReadIsNotSuccessful(HttpStatusCode statusCode) {
		// Arrange
		(SysImageUploader sut, _, _, _, _, _) = BuildSut(
			verifyResponse: BinaryResponse([], statusCode));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "the upload is not proven until the image can be read back");
		result.Error.Should().Contain(((int)statusCode).ToString(),
			because: "the verification status identifies the server-side failure");
	}

	[Test]
	[Description("Rejects a file above the shared binary cap without loading it into memory.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileExceedsSizeCap() {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.GetFileSize(FilePath).Returns(SysImageUploader.MaxImageBytes + 1);

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "the cap prevents an unbounded in-memory payload");
		result.Error.Should().Contain("byte limit", because: "the enforced limit must be visible");
		fileSystem.DidNotReceive().ReadAllBytes(Arg.Any<string>());
		await client.DidNotReceiveWithAnyArgs().UploadImageAsync(default, default, default, default, default, default);
	}

	[Test]
	[Description("Rejects a file that grows above the cap between the size probe and the read.")]
	public async Task UploadAsync_ShouldFail_WhenFileGrowsPastCapDuringRead() {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.ReadAllBytes(FilePath).Returns(new byte[SysImageUploader.MaxImageBytes + 1]);

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "the cap must survive a local file race");
		result.Error.Should().Contain("changed while reading", because: "the caller can retry with a stable file");
		await client.DidNotReceiveWithAnyArgs().UploadImageAsync(default, default, default, default, default, default);
	}

	[TestCase("{\"error\":\"File is not an image.\"}", "File is not an image")]
	[TestCase("{\"success\":false,\"errorInfo\":{\"errorCode\":500}}", "500")]
	[TestCase("{\"Success\":false,\"ErrorInfo\":{\"Message\":\"File type is not allowed.\"}}",
		"File type is not allowed")]
	[Description("Surfaces every observed Image API rejection envelope and skips verification.")]
	public async Task UploadAsync_ShouldSurfaceServerError_WhenImageApiRejects(string body,
		string expectedError) {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, _) = BuildSut(TextResponse(body: body));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "a 2xx error envelope is still a rejected upload");
		result.Error.Should().Contain(expectedError, because: "the server's reason is actionable");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
	}

	[Test]
	[Description("Adds a credential and CSRF diagnostic when the Image API rejects authentication.")]
	public async Task UploadAsync_ShouldExplainAuthenticationAndCsrf_WhenImageApiReturnsForbidden() {
		// Arrange
		(SysImageUploader sut, _, _, _, _, _) = BuildSut(TextResponse(HttpStatusCode.Forbidden, string.Empty));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "the environment rejected the authenticated write");
		result.Error.Should().Contain("CSRF cookie", because: "the proxy and token boundary is the likely remedy");
	}

	[Test]
	[Description("Falls through an unparseable 2xx upload body to the authoritative byte verification.")]
	public async Task UploadAsync_ShouldVerifyBytes_WhenUploadBodyIsNotJson() {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, _) = BuildSut(TextResponse(body: "not-json"));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeTrue(because: "the read-back bytes, not response JSON, prove persistence");
		await client.Received(1).ExecuteGetRequestAsync(Arg.Any<string>(), 100_000, 1, 1,
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Fails when the verification read is non-success or returns different bytes.")]
	public async Task UploadAsync_ShouldFail_WhenVerificationDoesNotProvePersistence() {
		// Arrange
		(SysImageUploader sut, _, _, _, _, _) = BuildSut(verifyResponse: BinaryResponse([9, 9, 9]));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "a different payload may be a login page or corrupt record");
		result.Error.Should().Contain("does not match", because: "the failed byte comparison is the proof boundary");
	}

	[Test]
	[Description("Turns a Creatio authentication rejection into the uploader's structured failure result.")]
	public async Task UploadAsync_ShouldFail_WhenApplicationClientRejectsAuthentication() {
		// Arrange
		(SysImageUploader sut, ICreatioApplicationClient client, _, _, _, _) = BuildSut();
		client.UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new UnauthorizedAccessException("secret-free"));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "an unauthenticated upload cannot succeed");
		result.Error.Should().Contain("check username and password",
			because: "the user receives a sanitized recovery action");
	}

	[Test]
	[Description("The legacy constructor maps its authentication facade rejection to a structured upload failure.")]
	public async Task UploadAsync_ShouldFail_WhenLegacyAuthFacadeRejectsAuthentication() {
		// Arrange
		EnvironmentSettings settings = new() {
			Uri = "https://dev.creatio.com",
			Login = "Supervisor",
			Password = "secret"
		};
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(FilePath).Returns(true);
		fileSystem.GetFileSize(FilePath).Returns(PngPayload.LongLength);
		fileSystem.ReadAllBytes(FilePath).Returns(PngPayload);
		#pragma warning disable CS0618 // The regression intentionally exercises the obsolete public constructor.
		ICreatioAuthClient authClient = Substitute.For<ICreatioAuthClient>();
		authClient.LoginAsync(settings, Arg.Any<CancellationToken>())
			.Returns<Task<StorageStateResult>>(_ => throw CreatioAuthenticationException.InvalidCredentials(settings.Uri));
		SysImageUploader sut = new(settings, authClient, Substitute.For<IHttpClientFactory>(), fileSystem);
		#pragma warning restore CS0618

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "a rejected legacy forms login cannot upload an image");
		result.Error.Should().Contain("check username and password",
			because: "legacy callers retain the uploader's structured recovery guidance");
	}

	[Test]
	[Description("Propagates caller cancellation while still disposing the owned CreatioClient.")]
	public async Task UploadAsync_ShouldPropagateCancellation_WhenCallerCancels() {
		// Arrange
		(SysImageUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new OperationCanceledException(cancellation.Token));

		// Act
		Func<Task> act = () => sut.UploadAsync(FilePath, cancellation.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "caller cancellation must remain observable to MCP cancellation handling");
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Maps an uncancelled transport cancellation to the uploader's timeout result.")]
	public async Task UploadAsync_ShouldReturnTimeoutFailure_WhenTransportTimesOut() {
		// Arrange
		(SysImageUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new OperationCanceledException());

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "a transport timeout cannot produce a valid image id");
		result.Error.Should().Contain("timed out", because: "the caller needs the established timeout diagnosis");
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Maps CreatioClient HTTP failures to a structured upload failure and disposes the client.")]
	public async Task UploadAsync_ShouldReturnStructuredFailure_WhenTransportFails() {
		// Arrange
		(SysImageUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		client.UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new HttpRequestException("loopback unavailable"));

		// Act
		SysImageUploadResult result = await sut.UploadAsync(FilePath);

		// Assert
		result.Success.Should().BeFalse(because: "an HTTP transport failure cannot persist the image");
		result.Error.Should().Contain("loopback unavailable",
			because: "the non-secret transport diagnosis remains actionable");
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Propagates the exact caller cancellation token when cancellation occurs during image upload.")]
	public async Task UploadAsync_ShouldPropagateExactCancellation_WhenUploadIsCanceled() {
		// Arrange
		(SysImageUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		client.UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), cancellation.Token)
			.Returns(Task.FromCanceled<HttpResponseMessage>(cancellation.Token));

		// Act
		Func<Task> act = () => sut.UploadAsync(FilePath, cancellation.Token);

		// Assert
		OperationCanceledException exception = (await act.Should().ThrowAsync<OperationCanceledException>(
			because: "the caller's cancellation must remain distinguishable from an internal timeout")).Which;
		exception.CancellationToken.Should().Be(cancellation.Token,
			because: "the exact token is required for cooperative MCP cancellation correlation");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
		client.Received(1).Dispose();
	}
}
