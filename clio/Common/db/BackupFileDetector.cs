using System;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

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
	private readonly IAbstractionsFileSystem _abstractionsFileSystem;

	public BackupFileDetector(IFileSystem fileSystem, IAbstractionsFileSystem abstractionsFileSystem) {
		_fileSystem = fileSystem;
		_abstractionsFileSystem = abstractionsFileSystem;
	}

	public BackupFileType DetectBackupType(string filePath) {
		if (!_fileSystem.ExistsFile(filePath)) {
			return BackupFileType.Unknown;
		}

		string extension = _abstractionsFileSystem.Path.GetExtension(filePath).ToLowerInvariant();

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
