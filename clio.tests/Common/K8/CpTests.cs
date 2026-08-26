using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.K8;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.K8;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class CpTests {

	private string _sourceFilePath;

	[SetUp]
	public void SetUp() {
		_sourceFilePath = Path.GetTempFileName();
		File.WriteAllText(_sourceFilePath, "clio K8 copy cancellation test payload");
	}

	[TearDown]
	public void TearDown() {
		if (File.Exists(_sourceFilePath)) {
			File.Delete(_sourceFilePath);
		}
	}

	[Test]
	[Description("Propagates OperationCanceledException distinctly instead of wrapping it as IOException, so a caller can tell an interrupted upload apart from a genuine copy failure (review #1143 on PR #1143).")]
	public async Task HandleExecStreamsAsync_ShouldPropagateOperationCanceledException_WhenTokenIsAlreadyCanceled() {
		// Arrange
		using MemoryStream stdIn = new();
		using MemoryStream stdError = new();
		using CancellationTokenSource cts = new();
		cts.Cancel();

		// Act
		Func<Task> act = () => Cp.HandleExecStreamsAsync(stdIn, stdError, _sourceFilePath, "destination.txt", cts.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "cancellation is a caller-initiated abort, not a copy failure, and must not surface as IOException");
	}

	[Test]
	[Description("Still wraps a genuine (non-cancellation) copy failure as IOException, so the cancellation fix above does not change behavior for real errors.")]
	public async Task HandleExecStreamsAsync_ShouldWrapGenuineFailureAsIOException_WhenSourceFileIsMissing() {
		// Arrange
		using MemoryStream stdIn = new();
		using MemoryStream stdError = new();
		string missingSourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".missing");

		// Act
		Func<Task> act = () => Cp.HandleExecStreamsAsync(stdIn, stdError, missingSourcePath, "destination.txt", CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<IOException>(
			because: "a genuine failure to open the source file must still be reported as a copy failure"))
			.WithMessage("Copy command failed:*", because: "the wrapped message must retain the original failure detail");
	}

	[Test]
	[Description("Surfaces stderr text written by the remote exec session as IOException, independent of the cancellation-classification change.")]
	public async Task HandleExecStreamsAsync_ShouldThrowIOException_WhenRemoteStdErrorIsNotEmpty() {
		// Arrange
		using MemoryStream stdIn = new();
		byte[] errorBytes = System.Text.Encoding.UTF8.GetBytes("tar: destination folder missing");
		using MemoryStream stdError = new(errorBytes);

		// Act
		Func<Task> act = () => Cp.HandleExecStreamsAsync(stdIn, stdError, _sourceFilePath, "destination.txt", CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<IOException>(
			because: "a non-empty remote stderr indicates the exec command reported a failure"))
			.WithMessage("Copy command failed:*tar: destination folder missing*",
				because: "the remote error text must be included verbatim for diagnosability");
	}
}
