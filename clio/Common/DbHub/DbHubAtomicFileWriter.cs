using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Clio.Common.DbHub;

/// <summary>Commits validated dbHub TOML content through an atomic sibling-file replacement.</summary>
public interface IDbHubAtomicFileWriter {
	/// <summary>Validates that an existing credential file is a safe filesystem target.</summary>
	void ValidateExistingPermissions(string path);

	/// <summary>Atomically replaces or creates the target while preserving existing permissions.</summary>
	void Commit(string path, string content);
}

/// <inheritdoc />
public sealed class DbHubAtomicFileWriter : IDbHubAtomicFileWriter {
	/// <inheritdoc />
	public void ValidateExistingPermissions(string path) {
		DbHubPathSafety.RefuseUnsafeTarget(path);
	}

	/// <inheritdoc />
	public void Commit(string path, string content) {
		DbHubPathSafety.RefuseUnsafeTarget(path);
		bool exists = File.Exists(path);
		ValidateExistingPermissions(path);
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
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					new FileInfo(temp).SetAccessControl(windowsSecurity ?? CreateProtectedWindowsSecurity());
				}
				using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true);
				writer.Write(content);
				writer.Flush();
				stream.Flush(flushToDisk: true);
			}
			DbHubPathSafety.RefuseUnsafeTarget(path);
			if (exists) {
				File.Replace(temp, path, null, ignoreMetadataErrors: true);
			} else {
				File.Move(temp, path);
			}
			if (windowsSecurity is not null) {
				new FileInfo(path).SetAccessControl(windowsSecurity);
			} else if (unixMode is not null) {
				File.SetUnixFileMode(path, unixMode.Value);
			}
		}
		finally {
			if (File.Exists(temp)) {
				File.Delete(temp);
			}
		}
	}

	private static FileSecurity CreateProtectedWindowsSecurity() {
		SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
			?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
		FileSecurity security = new();
		security.SetOwner(owner);
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
			AccessControlType.Allow));
		security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
			FileSystemRights.FullControl, AccessControlType.Allow));
		security.AddAccessRule(new FileSystemAccessRule(
			new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl,
			AccessControlType.Allow));
		return security;
	}

}
