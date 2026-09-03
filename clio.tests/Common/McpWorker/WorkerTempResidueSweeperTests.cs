using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using Clio.Common.McpWorker;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.McpWorker;

/// <summary>
/// ENG-95262: unit coverage for the sweep that removes the working directories a KILLED clio process
/// could not remove itself.
/// </summary>
/// <remarks>
/// Driven against <see cref="MockFileSystem"/> rather than a real temporary directory, because the age
/// gate reads BOTH the creation stamp and the last-write stamp and only an in-memory file system lets a
/// test set the two independently on every platform: on Linux the birth time of a directory created a
/// moment ago cannot be moved into the past, so a real-file-system version of these tests would assert
/// nothing on the CI agent that runs them.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class WorkerTempResidueSweeperTests {

	private const string TempRoot = "/clio-temp/clio";
	private static readonly TimeSpan ResidueAge = TimeSpan.FromDays(1);

	private ILogger _logger;
	private IWorkingDirectoriesProvider _workingDirectories;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		_workingDirectories = Substitute.For<IWorkingDirectoriesProvider>();
		_workingDirectories.BaseTempDirectory.Returns(TempRoot);
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_workingDirectories.ClearReceivedCalls();
	}

	[Test]
	[Description("A working directory left behind by a killed worker is removed once it is older than the safety age, because CreateTempDirectory deletes in a finally and a killed process runs no finally.")]
	public void Sweep_ShouldRemoveTheDirectory_WhenItIsAbandonedResidue() {
		// Arrange
		MockFileSystem fileSystem = new();
		string abandoned = AddWorkingDirectory(fileSystem, "0123456789abcdef0123456789abcdef", ageDays: 3);
		WorkerTempResidueSweeper sut = new(_workingDirectories, fileSystem, _logger, ResidueAge);

		// Act
		WorkerTempResidueSweepReport report = sut.Sweep();

		// Assert
		fileSystem.Directory.Exists(abandoned).Should().BeFalse(
			because: "every killed worker leaves one of these behind, so a host that never removes them grows an unpacked package tree per kill until somebody notices the disk");
		report.Removed.Should().Be(1,
			because: "the report is what the host logs, and a sweep that removed something must be able to say so");
	}

	[Test]
	[Description("A working directory younger than the safety age is left alone, because it may belong to a clio process that is running right now and carries no owner to ask.")]
	public void Sweep_ShouldLeaveTheDirectory_WhenItIsYoungerThanTheSafetyAge() {
		// Arrange
		MockFileSystem fileSystem = new();
		string inUse = AddWorkingDirectory(fileSystem, "fedcba9876543210fedcba9876543210", ageDays: 0);
		WorkerTempResidueSweeper sut = new(_workingDirectories, fileSystem, _logger, ResidueAge);

		// Act
		WorkerTempResidueSweepReport report = sut.Sweep();

		// Assert
		fileSystem.Directory.Exists(inUse).Should().BeTrue(
			because: "deleting the working directory of a live clio process destroys the operation it is in the middle of, which is far worse than one leftover tree");
		report.Retained.Should().Be(1,
			because: "a directory deliberately left in place is retained, not silently ignored");
	}

	[Test]
	[Description("An old directory whose contents were written recently is left alone, because a long operation keeps writing into a tree it created days ago.")]
	public void Sweep_ShouldLeaveTheDirectory_WhenItWasCreatedLongAgoButWrittenJustNow() {
		// Arrange - the creation stamp says abandoned, the write stamp says busy. Reading only the former
		// deletes an active tree out from under its owner.
		MockFileSystem fileSystem = new();
		string busy = AddWorkingDirectory(fileSystem, "aaaaaaaabbbbbbbbccccccccdddddddd", ageDays: 5);
		fileSystem.Directory.SetLastWriteTimeUtc(busy, DateTime.UtcNow);
		WorkerTempResidueSweeper sut = new(_workingDirectories, fileSystem, _logger, ResidueAge);

		// Act
		WorkerTempResidueSweepReport report = sut.Sweep();

		// Assert
		fileSystem.Directory.Exists(busy).Should().BeTrue(
			because: "the age gate must read the LATER of the two stamps, or a slow restore that has been unpacking for a day loses its working tree mid-operation");
		report.Removed.Should().Be(0,
			because: "nothing in this arrangement is abandoned");
	}

	[Test]
	[Description("A directory in the same root whose name is not the 32-hex form CreateTempDirectory generates is never touched, however old it is.")]
	public void Sweep_ShouldNotTouchTheDirectory_WhenTheNameIsNotOneItGenerated() {
		// Arrange - the same root also holds whatever else clio puts there (marketplace downloads today,
		// something else tomorrow), and a sweep that deletes what it does not recognise is a data-loss bug.
		MockFileSystem fileSystem = new();
		string foreign = AddWorkingDirectory(fileSystem, "marketplace-cache", ageDays: 40);
		WorkerTempResidueSweeper sut = new(_workingDirectories, fileSystem, _logger, ResidueAge);

		// Act
		WorkerTempResidueSweepReport report = sut.Sweep();

		// Assert
		fileSystem.Directory.Exists(foreign).Should().BeTrue(
			because: "only directories this sweep's own producer could have created are its business - anything else belongs to a feature it knows nothing about");
		report.Removed.Should().Be(0,
			because: "an unrecognised name is skipped entirely rather than counted as retained residue");
		report.Retained.Should().Be(0,
			because: "retained counts what was considered and kept, and a foreign directory was never a candidate");
	}

	[Test]
	[Description("A missing temporary root is an empty sweep rather than a failure, because a host that has never created a working directory must still start.")]
	public void Sweep_ShouldReportNothing_WhenTheTemporaryRootDoesNotExist() {
		// Arrange
		MockFileSystem fileSystem = new();
		WorkerTempResidueSweeper sut = new(_workingDirectories, fileSystem, _logger, ResidueAge);

		// Act
		WorkerTempResidueSweepReport report = sut.Sweep();

		// Assert
		report.Should().Be(new WorkerTempResidueSweepReport(0, 0),
			because: "startup clean-up is a courtesy and must never be the reason an MCP host fails to serve");
	}

	private static string AddWorkingDirectory(MockFileSystem fileSystem, string name, int ageDays) {
		string path = Path.Combine(TempRoot, name);
		fileSystem.AddDirectory(path);
		DateTime stamp = DateTime.UtcNow.AddDays(-ageDays);
		fileSystem.Directory.SetCreationTimeUtc(path, stamp);
		fileSystem.Directory.SetLastWriteTimeUtc(path, stamp);
		return path;
	}
}
