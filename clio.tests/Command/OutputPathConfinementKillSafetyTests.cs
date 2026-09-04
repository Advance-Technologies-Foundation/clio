using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-95262 — <c>get-schema</c> ships in the stage-6 worker cohort, so its <c>--output-file</c> write
/// is bounded by the parent KILLING the worker. A kill runs no <c>finally</c>, so a write that creates
/// the destination and then fills it leaves a truncated — typically empty — file at the destination.
/// <para>
/// That is not merely untidy: <see cref="OutputPathConfinement.Resolve"/> refuses an output path that
/// already exists, so the truncated file the kill left BLOCKS the retry that would repair it. A
/// transient kill becomes a state the user has to clean up by hand.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class OutputPathConfinementKillSafetyTests {

	private const string Content = "namespace Usr { public class Handler { } }";

	private MockFileSystem _fileSystem;
	private InterruptionObservingFileSystem _observing;
	private List<string> _snapshots;
	private string _outputDirectory;
	private string _outputFile;

	[SetUp]
	public void SetUp() {
		_fileSystem = new MockFileSystem();
		_snapshots = [];
		_observing = new InterruptionObservingFileSystem(_fileSystem, CaptureSnapshot);
		_outputDirectory = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/ws", "out"));
		_outputFile = _fileSystem.Path.Combine(_outputDirectory, "UsrHandler.cs");
	}

	[Test]
	[Description("A kill landing between any two filesystem operations of an --output-file write must leave the target either absent or complete — never a truncated file, which the confinement guard would then refuse to overwrite on retry.")]
	public void WriteAtomic_ShouldLeaveTheTargetAbsentOrComplete_WhenInterruptedAtAnyPoint() {
		// Arrange
		CaptureSnapshot();

		// Act
		OutputPathConfinement.WriteAtomic(_observing.FileSystem, _outputFile, Content);
		CaptureSnapshot();

		// Assert
		_snapshots.Count.Should().BeGreaterThan(2,
			because: "the write performs several observable filesystem operations, so anything fewer means the interruption points were not sampled at all");
		foreach (string snapshot in _snapshots) {
			if (snapshot is null) {
				continue; // an absent target is the state a retry can repair without any manual cleanup
			}
			snapshot.Should().Be(Content,
				because: "an output-file that exists with anything other than the complete body is a file the confinement guard refuses to overwrite, so the kill turns a retryable read into manual cleanup");
		}
	}

	[Test]
	[Description("An --output-file write must write no content directly to the target path, so a kill landing mid-write cannot truncate the published file.")]
	public void WriteAtomic_ShouldWriteNoContentDirectlyToTheTarget_WhenPublishing() {
		// Act
		OutputPathConfinement.WriteAtomic(_observing.FileSystem, _outputFile, Content);

		// Assert
		_observing.ContentWriteTargets.Should().NotBeEmpty(because: "the body must actually be written somewhere");
		foreach (string target in _observing.ContentWriteTargets) {
			_fileSystem.Path.GetFullPath(target).Should().NotBe(_fileSystem.Path.GetFullPath(_outputFile),
				because: "content written straight to the target can be observed half-written by a kill, which no snapshot between operations can catch — the body must be completed elsewhere and moved into place");
		}
	}

	[Test]
	[Description("A completed --output-file write must leave the target complete and no temporary file beside it.")]
	public void WriteAtomic_ShouldPublishTheContentAndLeaveNoResidue_WhenTheWriteSucceeds() {
		// Act
		OutputPathConfinement.WriteAtomic(_observing.FileSystem, _outputFile, Content);

		// Assert
		_fileSystem.File.ReadAllText(_outputFile).Should().Be(Content,
			because: "the whole point of the write is that the body lands at the requested path");
		_fileSystem.Directory.GetFiles(_outputDirectory).Should().ContainSingle(
			because: "a successful write cleans up after itself; only a kill may leave a temporary file behind");
	}

	[Test]
	[Description("An --output-file write must still refuse a target that appeared after the confinement check, so the Destructive=false contract survives the staged write.")]
	public void WriteAtomic_ShouldRefuseToOverwrite_WhenTheTargetAppearedAfterResolve() {
		// Arrange — the target did not exist when Resolve checked, and exists by the time the body arrives.
		_fileSystem.AddFile(_outputFile, new MockFileData("someone else's file"));

		// Act
		Action write = () => OutputPathConfinement.WriteAtomic(_observing.FileSystem, _outputFile, Content);

		// Assert
		write.Should().Throw<IOException>()
			.WithMessage("*already exists*",
				because: "an explicit output-file is additive: a target that appeared between the check and the write must be reported, never overwritten");
		_fileSystem.File.ReadAllText(_outputFile).Should().Be("someone else's file",
			because: "the pre-existing file must be left exactly as it was");
	}

	// -------------------------------------------------------------------------------------------

	private void CaptureSnapshot() =>
		_snapshots.Add(_fileSystem.File.Exists(_outputFile) ? _fileSystem.File.ReadAllText(_outputFile) : null);
}
