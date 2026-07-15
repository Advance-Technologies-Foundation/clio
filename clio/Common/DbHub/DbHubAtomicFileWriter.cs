using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;

namespace Clio.Common.DbHub;

/// <summary>Commits validated dbHub TOML content through an atomic sibling-file replacement.</summary>
public interface IDbHubAtomicFileWriter {
	/// <summary>Atomically replaces or creates the target while preserving existing permissions.</summary>
	void Commit(string path, string content);
}

/// <inheritdoc />
public sealed class DbHubAtomicFileWriter : IDbHubAtomicFileWriter {
	/// <inheritdoc />
	public void Commit(string path, string content) {
		bool exists = File.Exists(path);
		FileSecurity windowsSecurity = null;
		UnixFileMode? unixMode = null;
		if (exists) {
			FileInfo info = new(path);
			windowsSecurity = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? info.GetAccessControl() : null;
			unixMode = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? null : File.GetUnixFileMode(path);
		}
		string temp = Path.Combine(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
			$".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
		try {
			FileStreamOptions options = new() {
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None
			};
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				options.UnixCreateMode = unixMode ?? (UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			using (FileStream stream = new(temp, options)) {
				if (windowsSecurity is not null) {
					new FileInfo(temp).SetAccessControl(windowsSecurity);
				}
				using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true);
				writer.Write(content);
				writer.Flush();
				stream.Flush(flushToDisk: true);
			}
			if (exists) {
				File.Replace(temp, path, null, ignoreMetadataErrors: true);
			} else {
				File.Move(temp, path);
			}
		}
		finally {
			if (File.Exists(temp)) {
				File.Delete(temp);
			}
		}
	}
}
