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
	public Stream OpenRead(string canonicalPath) {
		using PinnedPath pinned = PinnedPath.Descend(canonicalPath);
		RejectReparsePoint(canonicalPath);
		FileStreamOptions options = new() {
			Mode = FileMode.Open,
			Access = FileAccess.Read,
			Share = FileShare.Read
		};
		// The content is copied out while the path is still pinned, so the stream handed back never
		// outlives the checks that approved it.
		MemoryStream buffer = new();
		using (FileStream source = new(canonicalPath, options)) {
			source.CopyTo(buffer);
		}
		pinned.Reverify();
		buffer.Position = 0;
		return buffer;
	}

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) {
		string directory = Path.GetDirectoryName(canonicalPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
			Directory.CreateDirectory(directory);
		}
		using PinnedPath pinned = PinnedPath.Descend(canonicalPath);
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

	private static void RejectReparsePoint(string path) {
		if (IsReparsePoint(path)) {
			throw new IOException(
				$"'{path}' is a reparse point; the path changed after it was approved, refusing to continue.");
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

		private const uint GenericRead = 0x80000000;
		private const uint FileShareAll = 0x00000007; // read | write | delete
		private const uint OpenExisting = 3;
		private const uint BackupSemantics = 0x02000000;
		private const uint OpenReparsePoint = 0x00200000;

		private readonly List<SafeFileHandle> _handles;
		private readonly IReadOnlyList<string> _components;

		private PinnedPath(List<SafeFileHandle> handles, IReadOnlyList<string> components) {
			_handles = handles;
			_components = components;
		}

		/// <summary>Checks and pins every directory component of <paramref name="canonicalPath"/>.</summary>
		/// <param name="canonicalPath">Absolute canonical path.</param>
		internal static PinnedPath Descend(string canonicalPath) {
			List<string> components = AncestorsOf(canonicalPath);
			List<SafeFileHandle> handles = [];
			try {
				foreach (string component in components) {
					// Runs for EVERY component and is never skipped: this is the check that catches a link
					// planted before the operation began.
					RejectReparsePoint(component);
					SafeFileHandle handle = OpenDirectoryHandle(component);
					if (handle is not null) {
						handles.Add(handle);
					}
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

		// A directory handle needs FILE_FLAG_BACKUP_SEMANTICS; without it CreateFile refuses a directory
		// outright, and a FileStream over a directory path throws. FILE_FLAG_OPEN_REPARSE_POINT makes the
		// handle refer to the component itself rather than a link target. Returns null - never throws - when
		// the handle cannot be taken: holding it is a hardening measure, and the link checks stand on their
		// own without it.
		private static SafeFileHandle OpenDirectoryHandle(string directory) {
			try {
				SafeFileHandle handle = CreateFileW(
					directory, GenericRead, FileShareAll, IntPtr.Zero, OpenExisting,
					BackupSemantics | OpenReparsePoint, IntPtr.Zero);
				return handle.IsInvalid ? null : handle;
			}
			catch (Exception) {
				return null;
			}
		}

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFileW(
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
