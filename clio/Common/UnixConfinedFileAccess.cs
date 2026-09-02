using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Clio.Common;

/// <summary>
/// Unix implementation of <see cref="IConfinedFileAccess"/>: descends the path with <c>openat</c>, one
/// directory descriptor at a time, and refuses to follow a symbolic link at any component.
/// </summary>
/// <remarks>
/// Every step is taken RELATIVE TO THE PREVIOUS DIRECTORY DESCRIPTOR, so the identity of each directory is
/// fixed the moment it is opened. Replacing a directory in the path after it was opened no longer affects
/// the descent - the descriptor still refers to the original directory - and replacing one before it is
/// opened is refused by <c>O_NOFOLLOW</c>. That is what makes this handle-bound rather than pathname-based:
/// no step ever re-resolves a name that was already checked.
/// </remarks>
internal sealed class UnixConfinedFileAccess : IConfinedFileAccess {

	/// <inheritdoc/>
	public Stream OpenRead(string canonicalPath, long maxBytes) {
		using DirectoryDescriptor directory = OpenParent(canonicalPath, out string fileName, createMissing: false);
		int fd = Interop.OpenAt(directory.Value, fileName, Flags.ReadOnly | Flags.NoFollow | Flags.CloseOnExec);
		if (fd < 0) {
			throw LastError(canonicalPath, "open");
		}
		FileStream stream = new(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Read);
		// The length comes from the SAME open descriptor, so the file that is measured is the file that will
		// be read - and it is measured before anything is copied out of it.
		long length = stream.Length;
		if (length > maxBytes) {
			stream.Dispose();
			throw new IOException(ConfinedFileAccess.DescribeTooLarge(length, maxBytes));
		}
		return stream;
	}

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) {
		// Missing parents are created BY the descent, each one relative to the descriptor of the directory
		// above it. Directory.CreateDirectory used to run first, on the mutable absolute path: with two
		// missing segments a local racer could replace the outer one with a symlink and have the inner one
		// created outside the allowed roots. The later descent refused the response file, but the
		// out-of-root directory was already there and could not be taken back.
		using DirectoryDescriptor directory = OpenParent(canonicalPath, out string fileName, createMissing: true);
		// The content is completed in a sibling temporary file and only then given its final name, both
		// created and renamed RELATIVE TO THE SAME directory descriptor. Writing straight into the final
		// path left a truncated file behind whenever the write failed part-way, with the call reported as
		// failed and the no-overwrite guard then refusing every retry against the wreckage.
		string temporaryName = $"{fileName}.{Guid.NewGuid():N}.tmp";
		int fd = Interop.OpenAt(
			directory.Value,
			temporaryName,
			Flags.WriteOnly | Flags.Create | Flags.Exclusive | Flags.NoFollow | Flags.CloseOnExec);
		if (fd < 0) {
			throw LastError(canonicalPath, "create");
		}
		// Wrapped IMMEDIATELY, not on the success path: a failing fchmod below used to leave the raw
		// descriptor open for the lifetime of the process, since nothing owned it yet.
		SafeFileHandle handle = new((IntPtr)fd, ownsHandle: true);
		try {
			// The permissions are narrowed to owner-only on the OPEN DESCRIPTOR, before a single byte of the
			// payload is written. The mode cannot be passed to the create itself: openat is a VARIADIC
			// function, and on Apple silicon a variadic argument is passed on the stack, so a P/Invoke that
			// declares it as an ordinary fourth parameter hands libc whatever happens to be there - which
			// produced files with unreadable, unpredictable permissions. What matters for confidentiality is
			// that the file is empty until this call returns: an output file is legitimately allowed under
			// the SHARED OS temp root and holds a raw service response, and no byte of it exists while the
			// mode is still undetermined.
			if (Interop.FChmod(fd, OwnerReadWrite) != 0) {
				throw LastError(canonicalPath, "restrict permissions on");
			}
			using (FileStream stream = new(handle, FileAccess.Write)) {
				stream.Write(content, 0, content.Length);
				stream.Flush();
			}
			// linkat, NOT rename: rename REPLACES an existing entry, so a check-then-rename pair leaves a
			// window in which a second writer creates the target between the two steps and is then silently
			// overwritten - two concurrent calls would both report success while one result was destroyed.
			// linkat fails with EEXIST if the name is taken, and that test-and-create is ONE atomic operation,
			// which is what the non-destructive contract actually requires. The temporary entry is then
			// unlinked, leaving exactly the published file.
			if (Interop.LinkAt(directory.Value, temporaryName, directory.Value, fileName, 0) != 0) {
				int error = Marshal.GetLastWin32Error();
				if (error == FileAlreadyExists) {
					throw new IOException(
						$"output-file '{canonicalPath}' already exists; refusing to overwrite it. Choose a "
						+ "different path or remove the existing file.");
				}
				throw LastError(canonicalPath, "publish");
			}
			Interop.UnlinkAt(directory.Value, temporaryName);
		}
		catch {
			// The FileStream disposes the handle on the success path; on a failure before it is constructed
			// this is the only owner there is.
			handle.Dispose();
			Interop.UnlinkAt(directory.Value, temporaryName);
			throw;
		}
	}

	/// <summary>Opens the parent directory of <paramref name="canonicalPath"/> component by component.</summary>
	/// <param name="canonicalPath">Absolute canonical path.</param>
	/// <param name="fileName">The final path component.</param>
	/// <param name="createMissing">
	/// Whether a component that does not exist is created relative to the descriptor already held, instead
	/// of failing the descent. Only a write asks for this; a read refuses a missing parent.
	/// </param>
	/// <returns>A descriptor on the parent directory, owned by the caller.</returns>
	private static DirectoryDescriptor OpenParent(string canonicalPath, out string fileName, bool createMissing) {
		fileName = Path.GetFileName(canonicalPath);
		if (string.IsNullOrEmpty(fileName)) {
			throw new IOException($"'{canonicalPath}' does not name a file.");
		}
		int rootFd = Interop.Open("/", Flags.ReadOnly | Flags.Directory | Flags.CloseOnExec);
		if (rootFd < 0) {
			throw LastError("/", "open");
		}
		DirectoryDescriptor current = new(rootFd);
		try {
			foreach (string component in DirectoryComponents(canonicalPath)) {
				int next = OpenComponent(current.Value, component);
				if (next < 0 && createMissing && Marshal.GetLastWin32Error() == NoSuchEntry) {
					// mkdirat, so the new directory lands inside the directory whose identity is already
					// fixed by the descriptor - there is no name for a racer to redirect. EEXIST means
					// another writer created the same component first, which is fine: the reopen below
					// still has to prove it is a real directory and not a symbolic link.
					if (Interop.MkdirAt(current.Value, component, OwnerAll) != 0
							&& Marshal.GetLastWin32Error() != FileAlreadyExists) {
						throw LastError(canonicalPath, $"create '{component}' in");
					}
					next = OpenComponent(current.Value, component);
				}
				if (next < 0) {
					// ELOOP is the interesting one: the component IS a symbolic link, which on an already
					// canonical path means it was replaced after the path was approved.
					throw LastError(canonicalPath, $"descend into '{component}'");
				}
				current = current.Replace(next);
			}
			DirectoryDescriptor opened = current;
			current = DirectoryDescriptor.None;
			return opened;
		}
		finally {
			current.Dispose();
		}
	}

	private static int OpenComponent(int directoryFd, string component) => Interop.OpenAt(
		directoryFd, component, Flags.ReadOnly | Flags.Directory | Flags.NoFollow | Flags.CloseOnExec);

	/// <summary>The directory components of an absolute path, outermost first.</summary>
	/// <param name="canonicalPath">Absolute canonical path.</param>
	private static IEnumerable<string> DirectoryComponents(string canonicalPath) {
		string directory = Path.GetDirectoryName(canonicalPath);
		return string.IsNullOrEmpty(directory)
			? []
			: directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
	}

	private static IOException LastError(string path, string operation) {
		int error = Marshal.GetLastWin32Error();
		bool linkInTheWay = error == Loop || error == NotDirectory;
		return linkInTheWay
			? new IOException(
				$"could not {operation} '{path}': a path component is a symbolic link. The path changed after "
				+ "it was approved; refusing to continue.")
			: new IOException($"could not {operation} '{path}' (error {error}).");
	}

	// EEXIST - the publish name was taken by someone else first.
	private const int FileAlreadyExists = 17;

	// ELOOP / ENOTDIR are what O_NOFOLLOW and O_DIRECTORY report for a symlinked component. ELOOP is NOT the
	// same number on both: 62 on Darwin/BSD, 40 on Linux (asm-generic/errno.h). With the Darwin value
	// hardcoded, a symlinked component on Linux still failed closed - the fd is negative either way - but
	// LastError could not recognize it, so the caller got "error 40" instead of being told the path had a
	// symbolic link in it. ENOTDIR is 20 on both.
	private static int Loop =>
		OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD() ? 62 : 40;

	private const int NotDirectory = 20;

	private const int OwnerReadWrite = 0x180; // 0600

	// 0700 for a directory clio creates itself: it has to be traversable to reach the file inside it, and
	// the file is already owner-only, so widening the directory would only widen who can list the name.
	private const int OwnerAll = 0x1C0;

	// ENOENT - the component does not exist yet; the only errno a write is allowed to create through.
	private const int NoSuchEntry = 2;

	/// <summary>
	/// An open directory file descriptor. Kept as a raw descriptor rather than a <c>SafeFileHandle</c>
	/// because every use of it is an argument to another <c>*at</c> call, and going through a safe handle
	/// for that means unwrapping it again at each one.
	/// </summary>
	private readonly struct DirectoryDescriptor(int value) : IDisposable {

		/// <summary>A descriptor that owns nothing.</summary>
		internal static DirectoryDescriptor None => new(-1);

		/// <summary>The raw file descriptor.</summary>
		internal int Value { get; } = value;

		/// <summary>Closes this descriptor and returns one owning <paramref name="next"/>.</summary>
		/// <param name="next">The newly opened descriptor to take ownership of.</param>
		internal DirectoryDescriptor Replace(int next) {
			Dispose();
			return new DirectoryDescriptor(next);
		}

		public void Dispose() {
			if (Value >= 0) {
				Interop.Close(Value);
			}
		}
	}

	/// <summary>
	/// The <c>O_*</c> flag values, which differ per platform - and, on Linux, per architecture.
	/// </summary>
	/// <remarks>
	/// A wrong value here fails SILENTLY in the dangerous direction: the open simply succeeds without the
	/// no-follow guarantee. <c>ConfinedFileAccessTests</c> therefore opens a path whose final component IS a
	/// symbolic link and requires it to fail, which is a direct behavioural check of these constants on
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

		internal static int NoFollow => PerPlatform(darwin: 0x0100, linuxArm: 0x8000, linux: 0x20000);

		internal static int Directory => PerPlatform(darwin: 0x100000, linuxArm: 0x4000, linux: 0x10000);

		internal static int CloseOnExec => IsDarwin ? 0x1000000 : 0x80000;

		private static int PerPlatform(int darwin, int linuxArm, int linux) {
			if (IsDarwin) {
				return darwin;
			}
			return IsLinuxArm ? linuxArm : linux;
		}
	}

	private static class Interop {
		// THREE arguments, never four. open/openat are variadic (`int openat(int, const char *, int, ...)`),
		// and the mode is the variadic part: on Apple silicon a variadic argument is passed on the stack
		// while a P/Invoke declares it as a register argument, so a four-parameter declaration silently
		// hands libc garbage. The mode is set with fchmod on the open descriptor instead.
		[DllImport("libc", EntryPoint = "open", SetLastError = true)]
		internal static extern int Open(string path, int flags);

		[DllImport("libc", EntryPoint = "openat", SetLastError = true)]
		internal static extern int OpenAt(int dirFd, string path, int flags);

		// mkdirat is NOT variadic - `int mkdirat(int, const char *, mode_t)` - so the mode is an ordinary
		// third parameter here, unlike openat above. mode_t is 16-bit on Darwin and 32-bit on Linux; 0700
		// fits either, and the callee reads only the low bits it declares.
		[DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
		internal static extern int MkdirAt(int dirFd, string path, int mode);

		[DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
		internal static extern int FChmod(int fd, int mode);

		[DllImport("libc", EntryPoint = "linkat", SetLastError = true)]
		internal static extern int LinkAt(int oldDirFd, string oldPath, int newDirFd, string newPath, int flags);

		[DllImport("libc", EntryPoint = "close", SetLastError = true)]
		internal static extern int Close(int fd);

		[DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
		private static extern int UnlinkAtNative(int dirFd, string path, int flags);

		/// <summary>Removes a temporary sibling. Best-effort: a cleanup failure never replaces the real one.</summary>
		/// <param name="dirFd">Descriptor of the directory holding the entry.</param>
		/// <param name="path">Entry name, relative to <paramref name="dirFd"/>.</param>
		internal static void UnlinkAt(int dirFd, string path) {
			try {
				UnlinkAtNative(dirFd, path, 0);
			}
			catch (Exception) {
				// Nothing to do: the caller is already reporting a failure, or has just succeeded.
			}
		}
	}
}
