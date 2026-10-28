namespace Clio.Tests.Command;

using System;
using System.Threading;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class UploadFileCommandTests : BaseCommandTests<UploadFileOptions>
{
	private const string FilePath = "C:/docs/contract.pdf";
	private static readonly Guid UploadedFileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
	private static readonly Guid ParentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private IRecordFileUploader _uploader;
	private ILogger _logger;
	private UploadFileCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UploadFileCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_uploader.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_uploader = Substitute.For<IRecordFileUploader>();
		containerBuilder.AddTransient<IRecordFileUploader>(_ => _uploader);
	}

	private static UploadFileOptions Options(string parentId = null) => new() {
		File = FilePath,
		Entity = "ContactFile",
		ParentColumn = "Contact",
		ParentId = parentId ?? ParentId.ToString()
	};

	[Test, Category("Unit")]
	[Description("Forwards the file, entity, parent column and parsed parent id to the uploader and prints the created file id on success.")]
	public void Execute_ShouldReportFileId_WhenUploadSucceeds() {
		// Arrange
		_uploader.UploadAsync(Arg.Any<RecordFileUploadRequest>(), Arg.Any<CancellationToken>())
			.Returns(RecordFileUploadResult.Successful(UploadedFileId));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "a verified upload is a success");
		_uploader.Received(1).UploadAsync(
			new RecordFileUploadRequest(FilePath, "ContactFile", "Contact", ParentId), Arg.Any<CancellationToken>());
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains(UploadedFileId.ToString())));
	}

	[Test, Category("Unit")]
	[Description("Reports failure with the uploader's message and a non-zero exit code when the upload fails.")]
	public void Execute_ShouldReportError_WhenUploadFails() {
		// Arrange
		_uploader.UploadAsync(Arg.Any<RecordFileUploadRequest>(), Arg.Any<CancellationToken>())
			.Returns(RecordFileUploadResult.Failure("File upload failed: the entity 'ContactFile' does not exist on the environment."));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "a failed upload must surface as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("does not exist")));
	}

	[Test, Category("Unit")]
	[Description("Rejects a parent id that is not a GUID before calling the uploader.")]
	public void Execute_ShouldFailWithoutUpload_WhenParentIdIsNotGuid() {
		// Arrange
		UploadFileOptions options = Options("not-a-guid");

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a malformed parent id cannot identify a record");
		_uploader.DidNotReceive().UploadAsync(Arg.Any<RecordFileUploadRequest>(), Arg.Any<CancellationToken>());
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("parent-id")));
	}
}
