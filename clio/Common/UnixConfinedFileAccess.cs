using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Clio.Common;

/// <summary>
/// Unix implementation of <see cref="IConfinedFileAccess"/>: descends the path with <c>openat</c>, one
/// directory handle at a time, and refuses to follow a symbolic link at any component.
/// </summary>
/// <remarks>
/// Every step is taken RELATIVE TO THE PREVIOUS DIRECTORY HANDLE, so the identity of each directory is
/// fixed the moment it is opened. Replacing a directory in the path after it was opened no longer affects
/// the descent - the handle still refers to the original directory - and replacing one before it is opened
/// is refused by <c>O_NOFOLLOW</c>. That is what makes this handle-bound rather than pathname-based: no
/// step ever re-resolves a name that was already checked.
/// </remarks>
internal sealed class UnixConfinedFileAccess : IConfinedFileAccess {

	/// <inheritdoc/>
	public Stream OpenRead(string canonicalPath) {
		(SafeFileHandle directory, string fileName) = OpenParent(canonicalPath);
		using (directory) {
			int fd = Interop.OpenAt(directory, fileName, Flags.ReadOnly | Flags.NoFollow | Flags.CloseOnExec);
			if (fd < 0) {
				throw LastError(canonicalPath, "open");
			}
			return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Read);
		}
	}

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) {
		string directoryPath = Path.GetDirectoryName(canonicalPath);
		if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath)) {
			Directory.CreateDirectory(directoryPath);
		}
		(SafeFileHandle directory, string fileName) = OpenParent(canonicalPath);
		using (directory) {
			// The content is completed in a sibling temporary file and only then given its final name, both
			// created and renamed RELATIVE TO THE SAME directory handle. Writing straight into the final path
			// left a truncated file behind whenever the write failed part-way, with the call reported as
			// failed and the no-overwrite guard then refusing every retry against the wreckage.
			string temporaryName = $"{fileName}.{Guid.NewGuid():N}.tmp";
			int fd = Interop.OpenAt(
				directory,
				temporaryName,
				Flags.WriteOnly | Flags.Create | Flags.Exclusive | Flags.NoFollow | Flags.CloseOnExec);
			if (fd < 0) {
				throw LastError(canonicalPath, "create");
			}
			try {
				// The permissions are narrowed to owner-only on the OPEN HANDLE, before a single byte of the
				// payload is written. The mode cannot be passed to the create itself: openat is a VARIADIC
				// function, and on Apple silicon a variadic argument is passed on the stack, so a P/Invoke
				// that declares it as an ordinary fourth parameter hands libc whatever happens to be there -
				// which produced files with unreadable, unpredictable permissions. What matters for
				// confidentiality is that the file is empty until this call returns: an output file is
				// legitimately allowed under the SHARED OS temp root and holds a raw service response, and no
				// byte of it exists while the mode is still undetermined.
				if (Interop.FChmod(fd, OwnerReadWrite) != 0) {
					throw LastError(canonicalPath, "restrict permissions on");
				}
				using (FileStream stream = new(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Write)) {
					stream.Write(content, 0, content.Length);
					stream.Flush();
				}
				// linkat(AT_EMPTY_PATH) would publish without a window, but it needs CAP_DAC_READ_SEARCH on
				// Linux and does not exist on macOS. renameat against the same handle is the portable form;
				// the target-exists check below keeps the additive contract honest, since rename() itself
				// would happily replace an existing file.
				if (Interop.FAccessAt(directory, fileName, FileExists, 0) == 0) {
					Interop.UnlinkAt(directory, temporaryName, 0);
					throw new IOException(
						$"output-file '{canonicalPath}' already exists; refusing to overwrite it. Choose a "
						+ "different path or remove the existing file.");
				}
				if (Interop.RenameAt(directory, temporaryName, directory, fileName) != 0) {
					throw LastError(canonicalPath, "publish");
				}
			}
			catch {
				Interop.UnlinkAt(directory, temporaryName, 0);
				throw;
			}
		}
	}

	/// <summary>Opens the parent directory of <paramref name="canonicalPath"/> component by component.</summary>
	/// <param name="canonicalPath">Absolute canonical path.</param>
	/// <returns>The parent directory handle and the final path component.</returns>
	private static (SafeFileHandle directory, string fileName) OpenParent(string canonicalPath) {
		string fileName = Path.GetFileName(canonicalPath);
		if (string.IsNullOrEmpty(fileName)) {
			throw new IOException($"'{canonicalPath}' does not name a file.");
		}
		int rootFd = Interop.Open("/", Flags.ReadOnly | Flags.Directory | Flags.CloseOnExec);
		if (rootFd < 0) {
			throw LastError("/", "open");
		}
		SafeFileHandle current = new((IntPtr)rootFd, ownsHandle: true);
		try {
			foreach (string component in DirectoryComponents(canonicalPath)) {
				int next = Interop.OpenAt(
					current, component, Flags.ReadOnly | Flags.Directory | Flags.NoFollow | Flags.CloseOnExec);
				if (next < 0) {
					// ELOOP is the interesting one: the component IS a symbolic link, which on an already
					// canonical path means it was replaced after the path was approved.
					throw LastError(canonicalPath, $"descend into '{component}'");
				}
				current.Dispose();
				current = new SafeFileHandle((IntPtr)next, ownsHandle: true);
			}
			return (current, fileName);
		}
		catch {
			current.Dispose();
			throw;
		}
	}

	/// <summary>The directory components of an absolute path, outermost first.</summary>
	/// <param name="canonicalPath">Absolute canonical path.</param>
	private static IEnumerable<string> DirectoryComponents(string canonicalPath) {
		string directory = Path.GetDirectoryName(canonicalPath);
		if (string.IsNullOrEmpty(directory)) {
			return [];
		}
		return directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
	}

	private static IOException LastError(string path, string operation) {
		int error = Marshal.GetLastWin32Error();
		return error == Loop || error == NotDirectory
			? new IOException(
				$"could not {operation} '{path}': a path component is a symbolic link. The path changed after "
				+ "it was approved; refusing to continue.")
			: new IOException($"could not {operation} '{path}' (error {error}).");
	}

	// F_OK - existence only, no permission bits requested.
	private const int FileExists = 0;

	// ELOOP / ENOTDIR are what O_NOFOLLOW and O_DIRECTORY report for a symlinked component; the numbers are
	// the same on Linux and macOS.
	private const int Loop = 62;
	private const int NotDirectory = 20;

	private const int OwnerReadWrite = 0x180; // 0600

	/// <summary>
	/// The <c>O_*</c> flag values, which differ per platform - and, on Linux, per architecture.
	/// </summary>
	/// <remarks>
	/// A wrong value here fails SILENTLY in the dangerous direction: the open simply succeeds without the
	/// no-follow guarantee. <c>UnixConfinedFileAccessTests</c> therefore opens a path whose final component
	/// IS a symbolic link and requires it to fail, which is a direct behavioural check of these constants on
	/// whatever platform the suite runs on.
	/// </remarks>
	private static class Flags {
		private static readonly bool IsDarwin = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();

		private static readonly bool IsLinuxArm =
			RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64;

		internal const int ReadOnly = 0x0000;
		internal const int WriteOnly = 0x0001;

		internal static int Create => IsDarwin ? 0x0200 : 0x40;
		internal static int Exclusive => IsDarwin ? 0x0800 : 0x80;
		internal static int NoFollow => IsDarwin ? 0x0100 : IsLinuxArm ? 0x8000 : 0x20000;
		internal static int Directory => IsDarwin ? 0x100000 : IsLinuxArm ? 0x4000 : 0x10000;
		internal static int CloseOnExec => IsDarwin ? 0x1000000 : 0x80000;
	}

	private static class Interop {
		// THREE arguments, never four. open/openat are variadic (`int openat(int, const char *, int, ...)`),
		// and the mode is the variadic part: on Apple silicon a variadic argument is passed on the stack
		// while a P/Invoke declares it as a register argument, so a four-parameter declaration silently
		// hands libc garbage. The mode is set with fchmod on the open handle instead.
		[DllImport("libc", EntryPoint = "open", SetLastError = true)]
		private static extern int OpenNative(string path, int flags);

		[DllImport("libc", EntryPoint = "openat", SetLastError = true)]
		private static extern int OpenAtNative(int dirFd, string path, int flags);

		[DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
		private static extern int FChmodNative(int fd, int mode);

		[DllImport("libc", EntryPoint = "renameat", SetLastError = true)]
		private static extern int RenameAtNative(int oldDirFd, string oldPath, int newDirFd, string newPath);

		[DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
		private static extern int UnlinkAtNative(int dirFd, string path, int flags);

		[DllImport("libc", EntryPoint = "faccessat", SetLastError = true)]
		private static extern int FAccessAtNative(int dirFd, string path, int mode, int flags);

		internal static int Open(string path, int flags) => OpenNative(path, flags);

		internal static int OpenAt(SafeFileHandle directory, string path, int flags) =>
			OpenAtNative((int)directory.DangerousGetHandle(), path, flags);

		internal static int FChmod(int fd, int mode) => FChmodNative(fd, mode);

		internal static int RenameAt(SafeFileHandle oldDirectory, string oldPath, SafeFileHandle newDirectory, string newPath) =>
			RenameAtNative(
				(int)oldDirectory.DangerousGetHandle(), oldPath, (int)newDirectory.DangerousGetHandle(), newPath);

		internal static int UnlinkAt(SafeFileHandle directory, string path, int flags) {
			try {
				return UnlinkAtNative((int)directory.DangerousGetHandle(), path, flags);
			}
			catch (Exception) {
				// Best-effort cleanup of a temporary file; never replace the real failure with a second one.
				return -1;
			}
		}

		internal static int FAccessAt(SafeFileHandle directory, string path, int mode, int flags) =>
			FAccessAtNative((int)directory.DangerousGetHandle(), path, mode, flags);
	}
}
