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
			throw new InputFileTooLargeException(length, maxBytes);
		}
		return stream;
	}

	/// <inheritdoc/>
	/// <summary>
	/// Called with the staged file's name relative to the published file's own parent directory, once the
	/// staging directory exists and before any payload byte is written. Exists solely so that ordering can
	/// be pinned by a test; it is never set in production.
	/// </summary>
	internal static Action<string> NotifyTemporaryFileRestricted { get; set; }

	public void WriteNew(string canonicalPath, byte[] content) {
		// Missing parents are created BY the descent, each one relative to the descriptor of the directory
		// above it. Directory.CreateDirectory used to run first, on the mutable absolute path: with two
		// missing segments a local racer could replace the outer one with a symlink and have the inner one
		// created outside the allowed roots. The later descent refused the response file, but the
		// out-of-root directory was already there and could not be taken back.
		using DirectoryDescriptor directory = OpenParent(canonicalPath, out string fileName, createMissing: true);
		// The content is completed OUT OF SIGHT and only then given its final name, everything relative to
		// directory descriptors that are already fixed. Writing straight into the final path left a truncated
		// file behind whenever the write failed part-way, with the call reported as failed and the
		// no-overwrite guard then refusing every retry against the wreckage.
		//
		// Staging happens inside a directory this call creates 0700, rather than beside the target. openat is
		// VARIADIC - `int openat(int, const char *, int, ...)` - and the mode is the variadic part, so a
		// P/Invoke cannot pass it: on Apple silicon a variadic argument travels on the stack while the
		// declaration would put it in a register, which hands libc whatever happens to be there. The create
		// therefore runs with an UNCONTROLLED mode (a Linux x64 probe of this exact P/Invoke observed 0040),
		// and the fchmod that follows cannot revoke a descriptor another account already holds: under the
		// shared OS temp root, anyone allowed by those initial bits could open the empty file, keep the
		// descriptor, and read every byte written afterwards. mkdirat is NOT variadic, so 0700 IS passed at
		// creation - and a directory nobody else may traverse is a boundary no accidental file mode can
		// undo (PR #1229 review).
		string stagingName = $"{fileName}.{Guid.NewGuid():N}.tmp";
		if (Interop.MkdirAt(directory.Value, stagingName, OwnerAll) != 0) {
			throw LastError(canonicalPath, "create the staging directory for");
		}
		try {
			// Reopened NoFollow + Directory: the name was just created inside a fixed descriptor, and the
			// reopen still has to prove what it got back is that directory and not something substituted.
			int stagingFd = OpenComponent(directory.Value, stagingName);
			if (stagingFd < 0) {
				throw LastError(canonicalPath, "open the staging directory for");
			}
			using DirectoryDescriptor staging = new(stagingFd);
			WriteThroughStaging(canonicalPath, content, directory, staging, fileName, stagingName);
		}
		finally {
			Interop.RemoveDirectoryAt(directory.Value, stagingName);
		}
	}

	/// <summary>
	/// Writes the payload into the private staging directory and publishes it under its final name.
	/// </summary>
	/// <param name="canonicalPath">Absolute canonical path of the published file, used in diagnostics.</param>
	/// <param name="content">Bytes to publish.</param>
	/// <param name="directory">Descriptor of the published file's parent directory.</param>
	/// <param name="staging">Descriptor of the owner-only staging directory.</param>
	/// <param name="fileName">Final name of the published file, relative to <paramref name="directory"/>.</param>
	/// <param name="stagingName">Name of the staging directory, relative to <paramref name="directory"/>.</param>
	private static void WriteThroughStaging(string canonicalPath, byte[] content, DirectoryDescriptor directory,
			DirectoryDescriptor staging, string fileName, string stagingName) {
		int fd = Interop.OpenAt(
			staging.Value,
			StagedFileName,
			Flags.WriteOnly | Flags.Create | Flags.Exclusive | Flags.NoFollow | Flags.CloseOnExec);
		if (fd < 0) {
			throw LastError(canonicalPath, "create");
		}
		// Wrapped IMMEDIATELY, not on the success path: a failing fchmod below used to leave the raw
		// descriptor open for the lifetime of the process, since nothing owned it yet.
		SafeFileHandle handle = new((IntPtr)fd, ownsHandle: true);
		try {
			// Still narrowed on the OPEN DESCRIPTOR before a byte is written. It is no longer the
			// confidentiality boundary - the 0700 staging directory is - but it is what the PUBLISHED inode
			// inherits: linkat below gives the final name the same inode, mode included.
			if (Interop.FChmod(fd, OwnerReadWrite) != 0) {
				throw LastError(canonicalPath, "restrict permissions on");
			}
			// The ONLY point from which the creation-time guarantee can be observed. The published inode is
			// owner-only whether the mode is narrowed before or after the write, so a regression that
			// reordered these two statements - leaving the staged file readable for the whole transfer -
			// would pass every assertion made on the finished file.
			NotifyTemporaryFileRestricted?.Invoke(Path.Combine(stagingName, StagedFileName));
			using (FileStream stream = new(handle, FileAccess.Write)) {
				stream.Write(content, 0, content.Length);
				stream.Flush();
			}
			// linkat, NOT rename: rename REPLACES an existing entry, so a check-then-rename pair leaves a
			// window in which a second writer creates the target between the two steps and is then silently
			// overwritten - two concurrent calls would both report success while one result was destroyed.
			// linkat fails with EEXIST if the name is taken, and that test-and-create is ONE atomic operation,
			// which is what the non-destructive contract actually requires. The staged entry is then
			// unlinked, leaving exactly the published file.
			if (Interop.LinkAt(staging.Value, StagedFileName, directory.Value, fileName, 0) != 0) {
				int error = Marshal.GetLastWin32Error();
				if (error == FileAlreadyExists) {
					throw new OutputFileAlreadyExistsException(canonicalPath);
				}
				throw LastError(canonicalPath, "publish");
			}
			Interop.UnlinkAt(staging.Value, StagedFileName);
		}
		catch {
			// The FileStream disposes the handle on the success path; on a failure before it is constructed
			// this is the only owner there is.
			handle.Dispose();
			Interop.UnlinkAt(staging.Value, StagedFileName);
			throw;
		}
	}

	/// <summary>Name the payload carries inside the staging directory, which is private to this call.</summary>
	private const string StagedFileName = "response";

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
	internal static class Flags {
		private static readonly bool IsDarwin = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();

		// 32-bit ARM ONLY. arch/arm has its own O_NOFOLLOW/O_DIRECTORY values, but arm64 Linux uses the
		// asm-generic set - the same values as x64. Folding Arm64 in here handed the openat descent and the
		// final open the 32-bit values on every arm64 Linux host, where 0x8000 is O_LARGEFILE (a no-op on
		// 64-bit) and 0x4000 is O_DIRECT: both opens then ran WITHOUT no-follow, so a symlinked path
		// component was followed and the escape this class exists to prevent was reopened. It fails in the
		// dangerous direction and it fails silently, on Graviton, arm64 containers and linux/arm64 images.
		private static readonly bool IsLinuxArm32 = RuntimeInformation.ProcessArchitecture is Architecture.Arm;

		internal const int ReadOnly = 0x0000;
		internal const int WriteOnly = 0x0001;

		internal static int Create => IsDarwin ? 0x0200 : 0x40;

		internal static int Exclusive => IsDarwin ? 0x0800 : 0x80;

		internal static int NoFollow => PerPlatform(darwin: 0x0100, linuxArm32: 0x8000, linux: 0x20000);

		internal static int Directory => PerPlatform(darwin: 0x100000, linuxArm32: 0x4000, linux: 0x10000);

		internal static int CloseOnExec => IsDarwin ? 0x1000000 : 0x80000;

		private static int PerPlatform(int darwin, int linuxArm32, int linux) =>
			SelectFlag(IsDarwin, RuntimeInformation.ProcessArchitecture, darwin, linuxArm32, linux);

		/// <summary>
		/// Picks the open-flag value for one platform and architecture. Split out from the properties, and
		/// with both inputs passed in, so the architecture mapping can be pinned from any host - the wrong
		/// column costs a silently unenforced no-follow, and no CI leg runs on Linux arm64.
		/// </summary>
		/// <param name="isDarwin">Whether the host is Darwin.</param>
		/// <param name="architecture">Process architecture of the host.</param>
		/// <param name="darwin">Darwin value.</param>
		/// <param name="linuxArm32">Value for 32-bit ARM Linux (arch/arm), which has its own constants.</param>
		/// <param name="linux">Value for every other Linux architecture (asm-generic, including arm64).</param>
		internal static int SelectFlag(
			bool isDarwin, Architecture architecture, int darwin, int linuxArm32, int linux) {
			if (isDarwin) {
				return darwin;
			}
			return architecture is Architecture.Arm ? linuxArm32 : linux;
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

		/// <summary>Removes a staged entry. Best-effort: a cleanup failure never replaces the real one.</summary>
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

		// AT_REMOVEDIR is 0x80 on Darwin and 0x200 on Linux (asm-generic/fcntl.h). A wrong value here fails
		// LOUDLY rather than silently - unlinkat returns EINVAL and the staging directory is left behind -
		// which is why it is safe to keep as a constant while the O_* flags need behavioural coverage.
		private static int RemoveDirectory =>
			OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD()
				? 0x80
				: 0x200;

		/// <summary>Removes the staging directory once it is empty. Best-effort, like <see cref="UnlinkAt"/>.</summary>
		/// <param name="dirFd">Descriptor of the directory holding the staging directory.</param>
		/// <param name="path">Staging directory name, relative to <paramref name="dirFd"/>.</param>
		internal static void RemoveDirectoryAt(int dirFd, string path) {
			try {
				UnlinkAtNative(dirFd, path, RemoveDirectory);
			}
			catch (Exception) {
				// Nothing to do: the caller is already reporting a failure, or has just succeeded.
			}
		}
	}
}
