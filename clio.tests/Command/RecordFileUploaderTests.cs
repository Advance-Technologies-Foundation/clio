namespace Clio.Tests.Command;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RecordFileUploaderTests
{
	private const string FilePath = "C:/docs/contract.pdf";
	private const string UploadRoot = "https://dev.creatio.com/0/rest/FileApiService/UploadFile";
	private const string Entity = "ContactFile";
	private const string ParentColumn = "Contact";
	private static readonly Guid ParentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly byte[] Payload = [37, 80, 68, 70, 45, 49, 46, 55];

	/// <summary>
	/// Server text that must never reach the result: a stack trace line and a database constraint name.
	/// </summary>
	private const string StackLine = "at Terrasoft.Core.Entities.Entity.Save()";
	private const string ConstraintName = "FKContactFileContact";

	private static RecordFileUploadRequest Request(string filePath = FilePath, string entity = Entity,
		string parentColumn = ParentColumn, Guid? parentId = null) =>
		new(filePath, entity, parentColumn, parentId ?? ParentId);

	private static HttpResponseMessage TextResponse(HttpStatusCode statusCode = HttpStatusCode.OK,
		string body = "{\"success\":true}") => new(statusCode) { Content = new StringContent(body) };

	private static HttpResponseMessage BinaryResponse(byte[] payload,
		HttpStatusCode statusCode = HttpStatusCode.OK) =>
		new(statusCode) { Content = new ByteArrayContent(payload) };

	private static (RecordFileUploader Sut, IOwnedApplicationClient Client, IApplicationClientFactory Factory,
		EnvironmentSettings Settings, IServiceUrlBuilder UrlBuilder, IFileSystem FileSystem)
		BuildSut(HttpResponseMessage uploadResponse = null, HttpResponseMessage verifyResponse = null,
			string login = "Supervisor", string password = "secret") {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(TextResponse()));
		client.UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(uploadResponse ?? TextResponse()));
		client.ExecuteGetRequestAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(verifyResponse ?? BinaryResponse(Payload)));

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.FileApiUploadFile).Returns(UploadRoot);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => "https://dev.creatio.com/0/" + call.Arg<string>());

		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(FilePath).Returns(true);
		fileSystem.GetFileSize(FilePath).Returns(Payload.LongLength);
		fileSystem.ReadAllBytes(FilePath).Returns(Payload);

		EnvironmentSettings settings = new() {
			Uri = "https://dev.creatio.com",
			Login = login,
			Password = password
		};
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateFormsEnvironmentClient(settings).Returns(client);

		return (new RecordFileUploader(settings, factory, urlBuilder, fileSystem), client, factory,
			settings, urlBuilder, fileSystem);
	}

	private static string Rejection(string errorCode, string message) =>
		"{\"success\":false,\"errorInfo\":{\"errorCode\":\"" + errorCode + "\",\"message\":\"" + message + "\"}}";

	[Test]
	[Description("Uploads through the file API known route with every query parameter the service reads, then verifies the exact bytes through the OData Data readback.")]
	public async Task UploadAsync_ShouldUploadAndVerify_WhenRequestIsValid() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, IApplicationClientFactory factory,
			EnvironmentSettings settings, IServiceUrlBuilder urlBuilder, _) = BuildSut();

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeTrue(because: "the file API accepted the file and the bytes read back unchanged");
		result.FileId.Should().NotBeNull(because: "a successful upload returns the created file record id");
		result.Error.Should().BeNull(because: "a success carries no failure text");
		factory.Received(1).CreateFormsEnvironmentClient(settings);
		await client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
		urlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.FileApiUploadFile);
		await client.Received(1).UploadImageAsync(
			Arg.Is<string>(url => url.StartsWith(UploadRoot + "?", StringComparison.Ordinal)
				&& url.Contains($"totalFileLength={Payload.Length}")
				&& url.Contains($"fileId={result.FileId}")
				&& url.Contains("fileName=contract.pdf")
				&& url.Contains("mimeType=application%2Fpdf")
				&& url.Contains("columnName=Data")
				&& url.Contains($"entitySchemaName={Entity}")
				&& url.Contains($"parentColumnName={ParentColumn}")
				&& url.Contains($"parentColumnValue={ParentId}")),
			Arg.Is<byte[]>(bytes => bytes.SequenceEqual(Payload)), "contract.pdf", "application/pdf",
			Arg.Any<int>(), Arg.Any<CancellationToken>());
		await client.Received(1).ExecuteGetRequestAsync(
			Arg.Is<string>(url => url.EndsWith($"odata/{Entity}({result.FileId})/Data", StringComparison.Ordinal)),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
		client.Received(1).Dispose();
	}

	[TestCase("ItemNotFoundException", "Item with name \\\"ContactFileX\\\" not found. " + StackLine,
		"does not exist on the environment")]
	[TestCase("ItemNotFoundException", "Collection item with name \\\"Contact\\\" not found. " + StackLine,
		"column 'Contact' does not exist")]
	[TestCase("ItemNotFoundException", "Value \\\"Data\\\" was not found. " + StackLine,
		"is not a file entity")]
	[TestCase("DbOperationException", "23503: insert or update violates foreign key constraint \\\"" + ConstraintName + "\\\" " + StackLine,
		"parent record")]
	[TestCase("DbOperationException", "The INSERT statement conflicted with the FOREIGN KEY constraint \\\"" + ConstraintName + "\\\". " + StackLine,
		"parent record")]
	[TestCase("SecurityException", "Access denied to " + ConstraintName + ". " + StackLine,
		"SecurityException")]
	[Description("Maps each file API success:false rejection to clio's own text, never copies the server message, stack trace or constraint name, returns no file id and skips the readback.")]
	public async Task UploadAsync_ShouldDescribeRejectionInOwnWords_WhenFileApiRejects(string errorCode,
		string serverMessage, string expectedError) {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) =
			BuildSut(TextResponse(body: Rejection(errorCode, serverMessage)));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "an HTTP 200 answer with success:false is a rejection");
		result.Error.Should().Contain(expectedError, because: "the caller needs the cause in clio's own words");
		result.Error.Should().NotContain(StackLine, because: "a server stack trace must not reach the caller");
		result.Error.Should().NotContain(ConstraintName, because: "a database constraint name must not reach the caller");
		result.FileId.Should().BeNull(because: "the service refused the upload, so there is no file record to inspect");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
	}

	[Test]
	[Description("Reads a PascalCase success answer from the file API and continues to the byte readback, because the casing of the answer is not a documented contract.")]
	public async Task UploadAsync_ShouldVerifyAndSucceed_WhenSuccessAnswerIsPascalCase() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) =
			BuildSut(TextResponse(body: "{\"Success\":true,\"ErrorInfo\":null}"));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeTrue(because: "\"Success\":true is the same answer as \"success\":true");
		result.FileId.Should().NotBeNull(because: "a successful upload returns the created file record id");
		await client.Received(1).ExecuteGetRequestAsync(
			Arg.Is<string>(url => url.EndsWith($"odata/{Entity}({result.FileId})/Data", StringComparison.Ordinal)),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Maps a PascalCase rejection answer from the file API to clio's own unknown-entity text.")]
	public async Task UploadAsync_ShouldDescribeRejection_WhenRejectionAnswerIsPascalCase() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut(TextResponse(body:
			"{\"Success\":false,\"ErrorInfo\":{\"ErrorCode\":\"ItemNotFoundException\",\"Message\":\"Item with name \\\"X\\\" not found\"}}"));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "\"Success\":false is a rejection in any casing");
		result.Error.Should().Contain($"the entity '{Entity}' does not exist on the environment",
			because: "the PascalCase errorInfo must reach the same mapping as the camelCase one");
		result.FileId.Should().BeNull(because: "the service refused the upload, so there is no record to inspect");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
	}

	[Test]
	[Description("Treats an HTTP 400 {\"error\":...} answer as a file security filter rejection: no file id, and the server text is not copied.")]
	public async Task UploadAsync_ShouldReportSecurityFilterRejection_WhenFileApiAnswersBadRequestWithErrorString() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut(
			TextResponse(HttpStatusCode.BadRequest, "{\"error\":\"Extension mismatch " + StackLine + "\"}"));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the security filter rejected the file");
		result.Error.Should().Contain("file security policy", because: "the cause is the environment's file policy");
		result.Error.Should().NotContain(StackLine, because: "the filter's own text is not copied");
		result.FileId.Should().BeNull(because: "the filter runs before the file API, so nothing was stored");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
	}

	[TestCase(HttpStatusCode.BadRequest, "<html>bad request</html>")]
	[TestCase(HttpStatusCode.InternalServerError, "{\"success\":false}")]
	[TestCase(HttpStatusCode.BadGateway, "")]
	[Description("Reports any other non-2xx upload answer as not confirmed and carries the file id, so the caller checks the record before uploading again.")]
	public async Task UploadAsync_ShouldReportUnconfirmedWithFileId_WhenFileApiReturnsOtherNonSuccessStatus(
		HttpStatusCode statusCode, string body) {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) =
			BuildSut(TextResponse(statusCode, body));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "a non-2xx answer does not confirm the upload");
		result.Error.Should().Contain("not confirmed", because: "the outcome of the sent upload is unknown");
		result.Error.Should().Contain(((int)statusCode).ToString(), because: "the status identifies the failed hop");
		result.FileId.Should().NotBeNull(because: "the file record can exist after a sent upload");
		result.Error.Should().Contain(result.FileId.ToString(), because: "the text names the record to inspect");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
	}

	[TestCase("not-json")]
	[TestCase("")]
	[TestCase("{\"result\":1}")]
	[Description("Reports a 2xx upload answer that is not JSON, empty or without a success flag as not confirmed, with the file id and without a readback.")]
	public async Task UploadAsync_ShouldReportUnconfirmedWithFileId_WhenUploadAnswerIsUnexpected(string body) {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut(TextResponse(body: body));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "an unexpected answer does not confirm the upload");
		result.Error.Should().Contain("not confirmed", because: "the outcome of the sent upload is unknown");
		result.FileId.Should().NotBeNull(because: "the file record can exist after a sent upload");
		await client.DidNotReceiveWithAnyArgs().ExecuteGetRequestAsync(default, default, default, default, default);
	}

	[Test]
	[Description("Reports a readback with different bytes as not confirmed and carries the file id.")]
	public async Task UploadAsync_ShouldReportUnconfirmedWithFileId_WhenReadbackBytesDiffer() {
		// Arrange
		(RecordFileUploader sut, _, _, _, _, _) = BuildSut(verifyResponse: BinaryResponse([9, 9, 9]));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "different bytes do not prove the file was stored");
		result.Error.Should().Contain("does not match", because: "the byte comparison is the proof boundary");
		result.FileId.Should().NotBeNull(because: "the file API accepted the upload, so the record can exist");
	}

	[TestCase(HttpStatusCode.NotFound)]
	[TestCase(HttpStatusCode.InternalServerError)]
	[Description("Reports a non-success readback as not confirmed with the readback status and the file id.")]
	public async Task UploadAsync_ShouldReportUnconfirmedWithFileId_WhenReadbackFails(HttpStatusCode statusCode) {
		// Arrange
		(RecordFileUploader sut, _, _, _, _, _) = BuildSut(verifyResponse: BinaryResponse([], statusCode));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the upload is not proven until the bytes can be read back");
		result.Error.Should().Contain(((int)statusCode).ToString(), because: "the readback status identifies the failure");
		result.FileId.Should().NotBeNull(because: "the file API accepted the upload, so the record can exist");
	}

	[TestCase("", ParentColumn, "entity must be")]
	[TestCase("Contact File", ParentColumn, "entity must be")]
	[TestCase("ContactFile;drop", ParentColumn, "entity must be")]
	[TestCase(Entity, "", "parent-column must be")]
	[TestCase(Entity, "Contact&x=1", "parent-column must be")]
	[Description("Rejects an invalid entity or parent-column name before the file is read or any request is sent.")]
	public async Task UploadAsync_ShouldFailFast_WhenSchemaNameIsInvalid(string entity, string parentColumn,
		string expectedError) {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, IFileSystem fileSystem) = BuildSut();

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request(entity: entity, parentColumn: parentColumn));

		// Assert
		result.Success.Should().BeFalse(because: "the names go into the request URL and must be schema identifiers");
		result.Error.Should().Contain(expectedError, because: "the failure names the field to correct");
		result.FileId.Should().BeNull(because: "nothing was sent");
		fileSystem.DidNotReceive().ReadAllBytes(Arg.Any<string>());
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[Test]
	[Description("Rejects an empty parent id before any request is sent.")]
	public async Task UploadAsync_ShouldFailFast_WhenParentIdIsEmpty() {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, _) = BuildSut();

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request(parentId: Guid.Empty));

		// Assert
		result.Success.Should().BeFalse(because: "an empty id cannot identify a parent record");
		result.Error.Should().Contain("parent-id", because: "the failure names the field to correct");
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[Test]
	[Description("Rejects a missing local file before any request is sent.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileIsMissing() {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.ExistsFile(FilePath).Returns(false);

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "a missing file has nothing to upload");
		result.Error.Should().Contain("File not found", because: "the caller needs the local cause");
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[Test]
	[Description("Rejects an empty local file before reading it or sending any request.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileIsEmpty() {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.GetFileSize(FilePath).Returns(0);

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "an empty file cannot become a file record");
		result.Error.Should().Contain("empty", because: "the caller needs the local cause");
		fileSystem.DidNotReceive().ReadAllBytes(Arg.Any<string>());
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[Test]
	[Description("Rejects a file above the 10 MB limit without loading it into memory or sending any request.")]
	public async Task UploadAsync_ShouldFailFast_WhenFileExceedsSizeLimit() {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.GetFileSize(FilePath).Returns(RecordFileUploader.MaxFileBytes + 1);

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the limit bounds the memory one call holds");
		result.Error.Should().Contain("byte limit", because: "the enforced limit must be visible");
		fileSystem.DidNotReceive().ReadAllBytes(Arg.Any<string>());
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[Test]
	[Description("Rejects a file that grows above the limit between the size probe and the read.")]
	public async Task UploadAsync_ShouldFail_WhenFileGrowsPastLimitDuringRead() {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, IFileSystem fileSystem) = BuildSut();
		fileSystem.ReadAllBytes(FilePath).Returns(new byte[RecordFileUploader.MaxFileBytes + 1]);

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the limit must survive a local file race");
		result.Error.Should().Contain("changed while reading", because: "the caller can retry with a stable file");
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[TestCase(null, "secret")]
	[TestCase("Supervisor", null)]
	[TestCase("", "")]
	[Description("Requires forms-auth login and password before any request is sent.")]
	public async Task UploadAsync_ShouldFailFast_WhenCredentialsAreMissing(string login, string password) {
		// Arrange
		(RecordFileUploader sut, _, IApplicationClientFactory factory, _, _, _) =
			BuildSut(login: login, password: password);

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the file API needs a forms-auth session");
		result.Error.Should().Contain("login and password", because: "the caller needs to know what to configure");
		factory.DidNotReceiveWithAnyArgs().CreateFormsEnvironmentClient(default);
	}

	[Test]
	[Description("Reports an authentication failure without a file id when the login answer is not successful.")]
	public async Task UploadAsync_ShouldReportAuthenticationFailure_WhenLoginIsRejected() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(TextResponse(HttpStatusCode.Unauthorized, string.Empty)));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "an unauthenticated upload cannot succeed");
		result.Error.Should().Contain("Authentication failed", because: "the caller needs the credential remedy");
		result.FileId.Should().BeNull(because: "the file was never sent");
		await client.DidNotReceiveWithAnyArgs().UploadImageAsync(default, default, default, default, default, default);
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Reports a session rejection during the upload as not confirmed with the file id, because the file was already sent.")]
	public async Task UploadAsync_ShouldReportUnconfirmedWithFileId_WhenSessionIsRejectedDuringUpload() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		client.UploadImageAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new UnauthorizedAccessException("session expired"));

		// Act
		RecordFileUploadResult result = await sut.UploadAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the upload outcome is not confirmed");
		result.Error.Should().Contain("session was rejected", because: "the cause is the session, after the file was sent");
		result.FileId.Should().NotBeNull(because: "the file record can exist after a sent upload");
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Propagates caller cancellation while still disposing the owned client.")]
	public async Task UploadAsync_ShouldPropagateCancellation_WhenCallerCancels() {
		// Arrange
		(RecordFileUploader sut, IOwnedApplicationClient client, _, _, _, _) = BuildSut();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new OperationCanceledException(cancellation.Token));

		// Act
		Func<Task> act = () => sut.UploadAsync(Request(), cancellation.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "caller cancellation must remain observable to MCP cancellation handling");
		client.Received(1).Dispose();
	}
}
