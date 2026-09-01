using System;
using System.Collections.Generic;
using System.IO;

namespace Clio.Common;

/// <summary>
/// Windows implementation of <see cref="IConfinedFileAccess"/>: opens a handle on every directory in the
/// path and holds them for the whole operation, refusing any component that is a reparse point.
/// </summary>
/// <remarks>
/// Windows has no <c>openat</c>, so the descent cannot be expressed as a chain of relative opens. What it
/// does have is stronger sharing: while a handle on a directory is open, that directory cannot be renamed
/// or deleted. Opening every component and KEEPING the handles until the read or write completes therefore
/// freezes the path - the swap the Unix implementation refuses with <c>O_NOFOLLOW</c> simply cannot happen
/// here while the handles are held. Each component is also checked for a reparse point before it is opened,
/// so a link planted before the descent is refused rather than followed.
/// </remarks>
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
		// The content is copied out while the directory handles are still held, so the stream handed back
		// never outlives the frozen path.
		using FileStream source = new(canonicalPath, options);
		MemoryStream buffer = new();
		source.CopyTo(buffer);
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
			File.Move(temporaryPath, canonicalPath, overwrite: false);
		}
		catch (IOException) when (File.Exists(canonicalPath)) {
			DeleteQuietly(temporaryPath);
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

		private readonly List<FileStream> _handles;

		private PinnedPath(List<FileStream> handles) => _handles = handles;

		/// <summary>Opens every directory component of <paramref name="canonicalPath"/> and holds it.</summary>
		/// <param name="canonicalPath">Absolute canonical path.</param>
		internal static PinnedPath Descend(string canonicalPath) {
			List<FileStream> handles = [];
			try {
				foreach (string component in AncestorsOf(canonicalPath)) {
					RejectReparsePoint(component);
					// FileMode.Open with backup semantics is what lets a DIRECTORY be opened as a handle.
					handles.Add(new FileStream(
						component,
						new FileStreamOptions {
							Mode = FileMode.Open,
							Access = FileAccess.Read,
							Share = FileShare.ReadWrite,
							Options = FileOptions.None
						}));
				}
				return new PinnedPath(handles);
			}
			catch (UnauthorizedAccessException) {
				// Opening a directory as a stream is refused on some configurations. The reparse-point checks
				// above still ran, so fail open on the HANDLE-holding only, not on the link rule.
				foreach (FileStream handle in handles) {
					handle.Dispose();
				}
				return new PinnedPath([]);
			}
			catch {
				foreach (FileStream handle in handles) {
					handle.Dispose();
				}
				throw;
			}
		}

		public void Dispose() {
			foreach (FileStream handle in _handles) {
				handle.Dispose();
			}
		}

		private static IEnumerable<string> AncestorsOf(string path) {
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
