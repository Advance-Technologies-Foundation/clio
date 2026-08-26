using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Clio.Common.IIS;

internal static class DirectoryPathIdentity {
	private const uint FileFlagBackupSemantics = 0x02000000;

	internal static string Normalize(string path, bool expandEnvironmentVariables = false) {
		string candidate = expandEnvironmentVariables ? Environment.ExpandEnvironmentVariables(path) : path;
		string normalized = TrimTrailingSeparators(Path.GetFullPath(candidate));
		if (!OperatingSystem.IsWindows()) {
			return normalized;
		}
		string root = Path.GetPathRoot(normalized)
			?? throw new InvalidOperationException($"Cannot resolve the root of directory '{path}'.");
		string relativePathFromRoot = Path.GetRelativePath(root, normalized);
		if (relativePathFromRoot != "." && relativePathFromRoot
			.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
				StringSplitOptions.RemoveEmptyEntries)
			.Any(component => !IisSiteName.IsSafeLeaf(component))) {
			throw new InvalidOperationException(
				$"Directory '{path}' contains a Win32-ambiguous or unsafe path component.");
		}
		string existingAncestor = normalized;
		while (!Directory.Exists(existingAncestor)) {
			DirectoryInfo parent = Directory.GetParent(existingAncestor);
			if (parent is null) {
				throw new InvalidOperationException($"Cannot resolve the physical identity of directory '{path}'.");
			}
			existingAncestor = parent.FullName;
		}
		string finalAncestor = TryGetFinalDirectoryPath(existingAncestor)
			?? throw new InvalidOperationException(
				$"Cannot resolve the physical identity of directory '{path}'.");
		string relativePath = Path.GetRelativePath(existingAncestor, normalized);
		return relativePath == "."
			? finalAncestor
			: TrimTrailingSeparators(Path.GetFullPath(Path.Combine(finalAncestor, relativePath)));
	}

	private static string TrimTrailingSeparators(string path) {
		string root = Path.GetPathRoot(path);
		return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
			? path
			: path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	private static string TryGetFinalDirectoryPath(string path) {
		if (!Directory.Exists(path)) {
			return null;
		}
		using SafeFileHandle handle = CreateFile(path, 0,
			FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
			FileFlagBackupSemantics, IntPtr.Zero);
		if (handle.IsInvalid) {
			return null;
		}
		StringBuilder finalPath = new(512);
		uint length = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
		if (length == 0) {
			return null;
		}
		if (length >= finalPath.Capacity) {
			finalPath.EnsureCapacity((int)length + 1);
			length = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
			if (length == 0 || length >= finalPath.Capacity) {
				return null;
			}
		}
		string result = finalPath.ToString();
		result = result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
			? @"\\" + result[8..]
			: result.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? result[4..] : result;
		return TrimTrailingSeparators(result);
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
		FileShare shareMode, IntPtr securityAttributes, FileMode creationDisposition,
		uint flagsAndAttributes, IntPtr templateFile);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder filePath,
		uint filePathLength, uint flags);
}
