using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Clio.Common;

/// <summary>
/// Windows implementation of <see cref="IConfinedFileAccess"/>: checks every directory in the path for a
/// reparse point, holds a handle on each one for the whole operation, and re-checks them afterwards.
/// </summary>
/// <remarks>
/// Windows has no <c>openat</c>, so the descent cannot be a chain of relative opens. Two mechanisms stand
/// in for it, and BOTH matter:
/// <list type="number">
/// <item>every component is checked for a reparse point before it is used, which catches a link planted
/// before the operation starts;</item>
/// <item>a handle is held on every component for the duration, which blocks the rename or delete a swap
/// needs, and the components are checked again at the end, so a swap that slipped in during the descent is
/// caught before the result is used.</item>
/// </list>
/// The reparse-point checks are NOT conditional on the handles: opening a directory handle can be refused
/// on some configurations, and an earlier version let that refusal abandon the whole descent - which threw
/// away the link checks with it and let the swap through. Handle-holding is best-effort; the link check is
/// not.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsConfinedFileAccess : IConfinedFileAccess {

	/// <inheritdoc/>
	public Stream OpenRead(string canonicalPath, long maxBytes) {
		using PinnedPath pinned = PinnedPath.Descend(canonicalPath, createMissing: false);
		// The final component is opened NO-FOLLOW and then judged on that very handle. Checking the pathname
		// and reopening it by name is a different operation on a different object: a writable parent can
		// replace the final component between the two, and the ordinary FileStream then follows the
		// replacement. Nothing about a pinned ANCESTOR prevents that - the ancestors are unchanged.
		SafeFileHandle handle = OpenFileNoFollow(canonicalPath);
		MemoryStream buffer = new();
		try {
			RejectReparsePointHandle(handle, canonicalPath);
		}
		catch {
			handle.Dispose();
			throw;
		}
		// The content is copied out while the path is still pinned, so the stream handed back never outlives
		// the checks that approved it. The ceiling is applied to the copy ITSELF, not to the result: copying
		// first and measuring afterwards means a huge (or sparse) file inside an allowed root has already
		// been pulled into memory by the time anything could reject it.
		using (FileStream source = new(handle, FileAccess.Read)) {
			if (source.Length > maxBytes) {
				throw new InputFileTooLargeException(source.Length, maxBytes);
			}
			CopyBounded(source, buffer, maxBytes);
		}
		pinned.Reverify();
		buffer.Position = 0;
		return buffer;
	}

	// FILE_FLAG_OPEN_REPARSE_POINT is what makes this no-follow: with it the handle refers to the named entry
	// itself, so a symbolic link or junction planted as the final component yields a handle ON THE LINK - which
	// RejectReparsePointHandle then refuses - instead of silently opening the target. The share mask omits
	// DELETE on purpose, so the entry cannot be renamed out from under the handle while it is read.
	private static SafeFileHandle OpenFileNoFollow(string canonicalPath) {
		SafeFileHandle handle = PinnedPath.CreateFileW(
			canonicalPath, PinnedPath.GenericRead, PinnedPath.FileShareRead, IntPtr.Zero,
			PinnedPath.OpenExisting, PinnedPath.OpenReparsePoint, IntPtr.Zero);
		if (!handle.IsInvalid) {
			return handle;
		}
		int error = Marshal.GetLastWin32Error();
		handle.Dispose();
		throw error switch {
			ErrorFileNotFound => new FileNotFoundException($"'{canonicalPath}' does not exist.", canonicalPath),
			ErrorPathNotFound => new DirectoryNotFoundException(
				$"the directory of '{canonicalPath}' does not exist."),
			_ => new IOException(
				$"could not open '{canonicalPath}' without following links (error {error}); refusing to "
				+ "continue, because reopening it by name would follow a component swapped in the meantime.")
		};
	}

	// Judges the OPEN HANDLE, not the pathname: this is the difference between proving the object being read
	// is not a link and proving that some object of that name was not a link at some earlier moment.
	private static void RejectReparsePointHandle(SafeFileHandle handle, string path) {
		if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information)) {
			int error = Marshal.GetLastWin32Error();
			throw new IOException(
				$"could not inspect the opened handle for '{path}' (error {error}); refusing to continue, "
				+ "because an uninspected handle may be a reparse point.");
		}
		if ((information.FileAttributes & FileAttributeReparsePoint) != 0) {
			throw new IOException(
				$"'{path}' is a reparse point; the path changed after it was approved, refusing to continue.");
		}
		if ((information.FileAttributes & FileAttributeDirectory) != 0) {
			throw new IOException($"'{path}' is a directory, not a file.");
		}
	}

	private const uint FileAttributeDirectory = 0x00000010;
	private const uint FileAttributeReparsePoint = 0x00000400;
	private const int ErrorFileNotFound = 2;
	private const int ErrorPathNotFound = 3;

	/// <summary>IO_REPARSE_TAG_SYMLINK: a symbolic link, the one kind of reparse point Windows follows by name.</summary>
	internal const uint ReparseTagSymlink = 0xA000000C;

	/// <summary>IO_REPARSE_TAG_MOUNT_POINT: a junction (or volume mount point), also followed by name.</summary>
	internal const uint ReparseTagMountPoint = 0xA0000003;

	/// <summary>
	/// Reads the reparse TAG of <paramref name="path"/> without following it. The tag is what separates a
	/// link Windows redirects a pathname through (a symbolic link or a junction) from every other reparse
	/// point - a cloud-files placeholder under a OneDrive-redirected folder, an app-execution alias, a WSL
	/// link - which Windows does NOT follow and which therefore cannot move a write anywhere else.
	/// </summary>
	/// <remarks>
	/// Exists because <see cref="FileSystemInfo.LinkTarget"/> returns <see langword="null"/> on Windows for a
	/// symbolic link whose target does not exist (dangling) or cannot be resolved (a cycle), while the entry
	/// still carries <see cref="FileAttributes.ReparsePoint"/>. A caller that judged only <c>LinkTarget</c>
	/// would take those links for ordinary entries; a caller that judged only the attribute would refuse every
	/// placeholder. The tag answers both.
	/// </remarks>
	/// <param name="path">Absolute path of an existing entry (the link itself, not its target).</param>
	/// <param name="tag">The reparse tag when the call succeeds; <c>0</c> for an entry that is not a reparse point.</param>
	/// <returns><see langword="true"/> when the entry could be opened and inspected; otherwise <see langword="false"/>.</returns>
	internal static bool TryGetReparseTag(string path, out uint tag) {
		tag = 0;
		SafeFileHandle handle = PinnedPath.CreateFileW(
			path, 0, PinnedPath.FileShareReadWriteDelete, IntPtr.Zero, PinnedPath.OpenExisting,
			PinnedPath.BackupSemantics | PinnedPath.OpenReparsePoint, IntPtr.Zero);
		try {
			if (handle.IsInvalid) {
				return false;
			}
			if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfoClass, out FileAttributeTagInfo info,
				(uint)Marshal.SizeOf<FileAttributeTagInfo>())) {
				return false;
			}
			tag = (info.FileAttributes & FileAttributeReparsePoint) != 0 ? info.ReparseTag : 0;
			return true;
		}
		finally {
			handle.Dispose();
		}
	}

	// FILE_INFO_BY_HANDLE_CLASS.FileAttributeTagInfo
	private const int FileAttributeTagInfoClass = 9;

	[StructLayout(LayoutKind.Sequential)]
	private struct FileAttributeTagInfo {

		public uint FileAttributes;
		public uint ReparseTag;

	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass,
		out FileAttributeTagInfo fileInformation, uint bufferSize);

	[StructLayout(LayoutKind.Sequential)]
	private struct ByHandleFileInformation {

		public uint FileAttributes;
		public FileTimeStruct CreationTime;
		public FileTimeStruct LastAccessTime;
		public FileTimeStruct LastWriteTime;
		public uint VolumeSerialNumber;
		public uint FileSizeHigh;
		public uint FileSizeLow;
		public uint NumberOfLinks;
		public uint FileIndexHigh;
		public uint FileIndexLow;

	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FileTimeStruct {

		public uint LowDateTime;
		public uint HighDateTime;

	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle file,
		out ByHandleFileInformation fileInformation);

	// Stops at maxBytes + 1 bytes, so a file whose reported length lies (a sparse or concurrently grown
	// file) cannot make the copy unbounded either.
	private static void CopyBounded(Stream source, Stream destination, long maxBytes) {
		byte[] chunk = new byte[64 * 1024];
		long total = 0;
		while (true) {
			int read = source.Read(chunk, 0, chunk.Length);
			if (read == 0) {
				return;
			}
			total += read;
			if (total > maxBytes) {
				throw new InputFileTooLargeException(total, maxBytes);
			}
			destination.Write(chunk, 0, read);
		}
	}

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) {
		// Missing parents are created BY the descent, one level at a time, each after the level above it is
		// already pinned. Directory.CreateDirectory used to run first, on the mutable absolute path: with two
		// missing segments a local racer could replace the outer one with a reparse point and have the inner
		// one created outside the allowed roots. The later descent refused the response file, but the
		// out-of-root directory was already there and could not be taken back.
		using PinnedPath pinned = PinnedPath.Descend(canonicalPath, createMissing: true);
		string temporaryPath = $"{canonicalPath}.{Guid.NewGuid():N}.tmp";
		try {
			FileStreamOptions options = new() {
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None
			};
			using (FileStream stream = new(temporaryPath, options)) {
				stream.Write(content, 0, content.Length);
				stream.Flush();
			}
			pinned.Reverify();
			File.Move(temporaryPath, canonicalPath, overwrite: false);
		}
		catch (IOException) when (File.Exists(canonicalPath) && !File.Exists(temporaryPath)) {
			throw new IOException(
				$"output-file '{canonicalPath}' already exists; refusing to overwrite it. Choose a different "
				+ "path or remove the existing file.");
		}
		catch {
			DeleteQuietly(temporaryPath);
			throw;
		}
	}

	private static void DeleteQuietly(string path) {
		try {
			if (File.Exists(path)) {
				File.Delete(path);
			}
		}
		catch (Exception) {
			// A leftover temporary file is not worth replacing the real failure with a second exception.
		}
	}

	/// <summary>Directory handles held open for the lifetime of one confined read or write.</summary>
	private sealed class PinnedPath : IDisposable {

		internal const uint GenericRead = 0x80000000;
		// READ | WRITE only. FILE_SHARE_DELETE would let the directory be RENAMED while the handle is open,
		// which is precisely the swap the handle is held to prevent - a probe with delete sharing renamed a
		// pinned directory successfully.
		private const uint FileShareReadWrite = 0x00000003;
		// READ only, for the final component: the entry must not be renamed or deleted while it is read, and
		// nothing needs to write it at the same time.
		internal const uint FileShareRead = 0x00000001;
		// Everything shared, for a METADATA-only probe that must not disturb an entry it merely inspects.
		internal const uint FileShareReadWriteDelete = 0x00000007;
		internal const uint OpenExisting = 3;
		internal const uint BackupSemantics = 0x02000000;
		internal const uint OpenReparsePoint = 0x00200000;

		private readonly List<SafeFileHandle> _handles;
		private readonly IReadOnlyList<string> _components;

		private PinnedPath(List<SafeFileHandle> handles, IReadOnlyList<string> components) {
			_handles = handles;
			_components = components;
		}

		// Lives here rather than on the outer type: the pathname reparse-point check is only ever a step of
		// the descent, and only the descent knows which components it has pinned.
		private static void RejectReparsePoint(string path) {
			if (IsReparsePoint(path)) {
				throw new IOException(
					$"'{path}' is a reparse point; the path changed after it was approved, refusing to continue.");
			}
		}

		// The directory counterpart of RejectReparsePointHandle, and the reason the pathname check alone is not
		// enough: RejectReparsePoint(component) judges a NAME, and between that call and CreateFileW a local
		// racer can replace the component with a junction. The descent would then pin the junction, and every
		// later absolute-path create under it lands outside the allowed root - Reverify only notices afterwards,
		// once the out-of-root directory already exists and cannot be taken back. Judging the handle closes the
		// interval: whatever the name resolved to, THIS object is proven to be a real directory and not a link.
		private static void RejectReparsePointDirectoryHandle(SafeFileHandle handle, string directory) {
			if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information)) {
				int error = Marshal.GetLastWin32Error();
				throw new IOException(
					$"could not inspect the pinned handle for path component '{directory}' (error {error}); "
					+ "refusing to continue, because an uninspected handle may be a reparse point.");
			}
			if ((information.FileAttributes & FileAttributeReparsePoint) != 0) {
				throw new IOException(
					$"'{directory}' is a reparse point; the path changed after it was approved, refusing to continue.");
			}
			if ((information.FileAttributes & FileAttributeDirectory) == 0) {
				throw new IOException(
					$"'{directory}' is not a directory; the path changed after it was approved, refusing to continue.");
			}
		}

		private static bool IsReparsePoint(string path) {
			try {
				FileAttributes attributes = File.GetAttributes(path);
				return (attributes & FileAttributes.ReparsePoint) != 0;
			}
			catch (FileNotFoundException) {
				return false;
			}
			catch (DirectoryNotFoundException) {
				return false;
			}
		}

		/// <summary>Checks and pins every directory component of <paramref name="canonicalPath"/>.</summary>
		/// <param name="canonicalPath">Absolute canonical path.</param>
		/// <param name="createMissing">
		/// Whether a component that does not exist is created here, after its own parent has been pinned,
		/// instead of failing the descent. Only a write asks for this; a read refuses a missing parent.
		/// </param>
		internal static PinnedPath Descend(string canonicalPath, bool createMissing) {
			List<string> components = AncestorsOf(canonicalPath);
			List<SafeFileHandle> handles = [];
			try {
				foreach (string component in components) {
					// Creating happens INSIDE the loop, so the parent of every new directory is already
					// held by a handle taken without FILE_SHARE_DELETE - it cannot be renamed or replaced,
					// which is what would otherwise redirect where the child lands. The reparse-point check
					// below still runs on the result, so a component that was created by someone else in
					// the meantime is judged exactly like a pre-existing one.
					if (createMissing && !Directory.Exists(component)) {
						CreateDirectoryComponent(component);
					}
					// Runs for EVERY component and is never skipped: this is the check that catches a link
					// planted before the operation began.
					RejectReparsePoint(component);
					handles.Add(OpenDirectoryHandle(component));
				}
				return new PinnedPath(handles, components);
			}
			catch {
				foreach (SafeFileHandle handle in handles) {
					handle.Dispose();
				}
				throw;
			}
		}

		/// <summary>Re-checks the pinned components, catching a swap that happened during the operation.</summary>
		internal void Reverify() {
			foreach (string component in _components) {
				RejectReparsePoint(component);
			}
		}

		public void Dispose() {
			foreach (SafeFileHandle handle in _handles) {
				handle.Dispose();
			}
		}

		// One level only, never a recursive create: the levels above are pinned by the time this runs, and a
		// recursive create would build them from the pathname instead. An already-existing directory is not
		// a failure - a concurrent writer may have created the same component first - and the reparse-point
		// check on the caller's side is what decides whether the result is acceptable.
		private static void CreateDirectoryComponent(string component) {
			try {
				Directory.CreateDirectory(component);
			}
			catch (IOException) when (Directory.Exists(component)) {
				// Lost the race to create it; the check that follows judges what is actually there.
			}
		}

		// A directory handle needs FILE_FLAG_BACKUP_SEMANTICS; without it CreateFile refuses a directory
		// outright, and a FileStream over a directory path throws. FILE_FLAG_OPEN_REPARSE_POINT makes the
		// handle refer to the component itself rather than a link target.
		// FAIL CLOSED: a handle that cannot be taken means the component is not pinned, and an unpinned
		// component can be swapped mid-operation. Accepting that silently is what turned the guarantee into
		// a best-effort check.
		private static SafeFileHandle OpenDirectoryHandle(string directory) {
			SafeFileHandle handle = CreateFileW(
				directory, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting,
				BackupSemantics | OpenReparsePoint, IntPtr.Zero);
			if (handle.IsInvalid) {
				int error = Marshal.GetLastWin32Error();
				handle.Dispose();
				throw new IOException(
					$"could not pin path component '{directory}' (error {error}); refusing to continue, "
					+ "because an unpinned component can be replaced while the operation runs.");
			}
			// Inspected BEFORE the handle joins the pinned list and before the descent goes any deeper, so a
			// junction swapped in after the pathname check is never pinned and never descended through.
			try {
				RejectReparsePointDirectoryHandle(handle, directory);
			}
			catch {
				handle.Dispose();
				throw;
			}
			return handle;
		}

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern SafeFileHandle CreateFileW(
			string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
			uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

		private static List<string> AncestorsOf(string path) {
			List<string> ancestors = [];
			string current = Path.GetDirectoryName(path);
			while (!string.IsNullOrEmpty(current)) {
				string parent = Path.GetDirectoryName(current);
				if (string.Equals(parent, current, StringComparison.Ordinal)) {
					break;
				}
				ancestors.Add(current);
				if (string.IsNullOrEmpty(parent)) {
					break;
				}
				current = parent;
			}
			ancestors.Reverse();
			return ancestors;
		}
	}
}
