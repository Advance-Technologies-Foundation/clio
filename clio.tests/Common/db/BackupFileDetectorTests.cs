using Clio.Common;
using Clio.Common.db;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.db;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
public class BackupFileDetectorTests
{
	private IFileSystem _fileSystem;

	[SetUp]
	public void Setup() {
		_fileSystem = Substitute.For<IFileSystem>();
	}

	[Test]
	[Description("Should detect .backup files as PostgreSQL backups")]
	public void DetectBackupType_WithBackupExtension_ReturnsPostgresBackup() {
		// Arrange
		_fileSystem.ExistsFile("database.backup").Returns(true);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.DetectBackupType("database.backup");

		// Assert
		result.Should().Be(BackupFileType.PostgresBackup, "because .backup extension indicates PostgreSQL");
	}

	[Test]
	[Description("Should detect .bak files as MSSQL backups")]
	public void DetectBackupType_WithBakExtension_ReturnsMssqlBackup() {
		// Arrange
		_fileSystem.ExistsFile("database.bak").Returns(true);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.DetectBackupType("database.bak");

		// Assert
		result.Should().Be(BackupFileType.MssqlBackup, "because .bak extension indicates MSSQL");
	}

	[Test]
	[Description("Should return Unknown for unrecognized file extensions")]
	public void DetectBackupType_WithUnknownExtension_ReturnsUnknown() {
		// Arrange
		_fileSystem.ExistsFile("database.unknown").Returns(true);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.DetectBackupType("database.unknown");

		// Assert
		result.Should().Be(BackupFileType.Unknown, "because extension is not recognized");
	}

	[Test]
	[Description("Should return Unknown when file does not exist")]
	public void DetectBackupType_WhenFileDoesNotExist_ReturnsUnknown() {
		// Arrange
		_fileSystem.ExistsFile("nonexistent.backup").Returns(false);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.DetectBackupType("nonexistent.backup");

		// Assert
		result.Should().Be(BackupFileType.Unknown, "because file does not exist");
	}

	[Test]
	[Description("Should handle case-insensitive extensions")]
	public void DetectBackupType_WithMixedCase_DetectsCorrectly() {
		// Arrange
		_fileSystem.ExistsFile("database.BACKUP").Returns(true);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.DetectBackupType("database.BACKUP");

		// Assert
		result.Should().Be(BackupFileType.PostgresBackup, "because detection should be case-insensitive");
	}

	[Test]
	[Description("Should return true for valid backup files")]
	public void IsValidBackupFile_WithValidFile_ReturnsTrue() {
		// Arrange
		_fileSystem.ExistsFile("database.backup").Returns(true);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.IsValidBackupFile("database.backup");

		// Assert
		result.Should().BeTrue("because .backup is a valid backup file type");
	}

	[Test]
	[Description("Should return false for invalid backup files")]
	public void IsValidBackupFile_WithInvalidFile_ReturnsFalse() {
		// Arrange
		_fileSystem.ExistsFile("database.txt").Returns(true);
		var sut = new BackupFileDetector(_fileSystem);

		// Act
		var result = sut.IsValidBackupFile("database.txt");

		// Assert
		result.Should().BeFalse("because .txt is not a valid backup file type");
	}
}
