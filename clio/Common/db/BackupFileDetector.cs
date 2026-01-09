using System;
using System.IO;

namespace Clio.Common.db;

public interface IBackupFileDetector
{
	BackupFileType DetectBackupType(string filePath);
	bool IsValidBackupFile(string filePath);
}

public enum BackupFileType
{
	Unknown,
	PostgresBackup,
	MssqlBackup
}

public class BackupFileDetector : IBackupFileDetector
{
	private readonly IFileSystem _fileSystem;

	public BackupFileDetector(IFileSystem fileSystem) {
		_fileSystem = fileSystem;
	}

	public BackupFileType DetectBackupType(string filePath) {
		if (!_fileSystem.ExistsFile(filePath)) {
			return BackupFileType.Unknown;
		}

		string extension = Path.GetExtension(filePath).ToLowerInvariant();

		return extension switch {
			".backup" => BackupFileType.PostgresBackup,
			".bak" => BackupFileType.MssqlBackup,
			_ => BackupFileType.Unknown
		};
	}

	public bool IsValidBackupFile(string filePath) {
		return DetectBackupType(filePath) != BackupFileType.Unknown;
	}
}
