using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Serialises access to a shared file across every clio process on the machine, and across every
/// thread inside one clio process, by holding an exclusive OS handle on a sentinel lock file for the
/// duration of a caller-supplied action.
/// <para>
/// <b>Why an exclusive handle and not a presence-based lock file.</b> Exclusion is expressed as
/// <see cref="FileShare.None"/> on an open handle, never as "the lock file exists". A handle is owned
/// by a process, so the operating system releases it when that process dies — including when it is
/// killed rather than asked to exit. That property is load-bearing: the MCP worker execution boundary
/// bounds a call by <b>killing</b> the child that runs it, and a presence-based lock would leave the
/// sentinel behind on the first such kill and strand the guarded resource permanently. The lock file
/// itself is deliberately never deleted — only the handle matters.
/// </para>
/// <para>
/// <b>Two layers.</b> Threads inside one process contend on a monitor keyed by the lock path, because
/// a second <see cref="FileShare.None"/> open from the SAME process would fail just as it does from a
/// stranger and turn an ordinary in-process overlap into a timeout. Processes contend on the file
/// handle. Same-thread nesting is admitted straight through (a gated read inside a gated
/// read-modify-write is a normal composition), so a caller cannot deadlock against itself.
/// </para>
/// </summary>
public interface IInterprocessFileGate {

	/// <summary>
	/// Runs <paramref name="action"/> while holding the interprocess lock represented by
	/// <paramref name="lockFilePath"/> and returns its result.
	/// </summary>
	/// <param name="lockFilePath">
	/// Full path of the sentinel lock file. It must live OUTSIDE any directory the guarded action may
	/// delete or recreate: a sentinel inside a deleted subtree is unlinked from under its holder on
	/// Unix, and on Windows makes the delete fail against the open exclusive handle.
	/// The parent directory is created when missing.
	/// </param>
	/// <param name="action">The guarded work. Must not perform network calls — see the remarks.</param>
	/// <typeparam name="T">Result type of the guarded work.</typeparam>
	/// <returns>Whatever <paramref name="action"/> returned.</returns>
	/// <remarks>
	/// Scope the action to the disk touch and nothing more. Holding this lock across a Creatio
	/// round trip serialises unrelated callers on network latency and rebuilds — across processes,
	/// where no monitor can bound it — the head-of-line stall the worker execution boundary exists to
	/// remove; and a budget kill mid-round-trip would release the handle at an arbitrary point.
	/// </remarks>
	/// <exception cref="TimeoutException">
	/// The lock could not be acquired within the gate's timeout, either because another thread of this
	/// process held it or because another process held the file handle.
	/// </exception>
	T Enter<T>(string lockFilePath, Func<T> action);

	/// <summary>
	/// Runs <paramref name="action"/> while holding the interprocess lock represented by
	/// <paramref name="lockFilePath"/>.
	/// </summary>
	/// <param name="lockFilePath">Full path of the sentinel lock file; see <see cref="Enter{T}"/>.</param>
	/// <param name="action">The guarded work.</param>
	/// <exception cref="TimeoutException">The lock could not be acquired within the gate's timeout.</exception>
	void Enter(string lockFilePath, Action action);
}

/// <inheritdoc />
public sealed class InterprocessFileGate : IInterprocessFileGate {

	/// <summary>Default maximum time a caller waits for the lock before a <see cref="TimeoutException"/>.</summary>
	internal const int DefaultTimeoutSeconds = 30;

	private const int SpinMilliseconds = 25;

	// Keyed by the normalised lock path and shared process-wide: two gate instances (production DI
	// resolves one singleton, but tests and any second container must not become a second lock domain)
	// have to contend on the same monitor, or the file-handle layer would have to absorb in-process
	// overlap and every concurrent call inside one process would burn the timeout.
	private static readonly ConcurrentDictionary<string, object> ProcessLocks = new(StringComparer.Ordinal);

	// Reentrancy set, per thread: a gated read nested inside a gated read-modify-write returns straight
	// through instead of trying to open a second exclusive handle on a file this thread already holds.
	[ThreadStatic]
	private static HashSet<string> _heldLocks;

	// Static rather than inlined into Enter: the set is [ThreadStatic] state, so it is initialized once per
	// thread and never from instance state — writing it from an instance member is what Sonar S2696 warns
	// about, and doing it here keeps the write where it belongs.
	private static HashSet<string> EnsureThreadHeldLocks() =>
		_heldLocks ??= new HashSet<string>(StringComparer.Ordinal);

	private readonly IFileSystem _fileSystem;
	private readonly TimeSpan _timeout;

	/// <summary>
	/// Initializes a new instance of the <see cref="InterprocessFileGate"/> class with the default
	/// <see cref="DefaultTimeoutSeconds"/> timeout.
	/// </summary>
	/// <param name="fileSystem">
	/// File-system abstraction used to open the sentinel handle. Injected rather than reached through
	/// static <c>System.IO</c> calls so the gate is unit-testable and obeys the repository DI policy.
	/// </param>
	public InterprocessFileGate(IFileSystem fileSystem)
		: this(fileSystem, TimeSpan.FromSeconds(DefaultTimeoutSeconds)) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InterprocessFileGate"/> class with an explicit
	/// timeout. Used by tests that need to observe the timeout without waiting for it.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used to open the sentinel handle.</param>
	/// <param name="timeout">Maximum time to wait for both the in-process monitor and the file handle.</param>
	internal InterprocessFileGate(IFileSystem fileSystem, TimeSpan timeout) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_timeout = timeout;
	}

	/// <inheritdoc />
	public void Enter(string lockFilePath, Action action) {
		ArgumentNullException.ThrowIfNull(action);
		Enter(lockFilePath, () => {
			action();
			return true;
		});
	}

	/// <inheritdoc />
	public T Enter<T>(string lockFilePath, Func<T> action) {
		ArgumentNullException.ThrowIfNull(action);
		if (string.IsNullOrWhiteSpace(lockFilePath)) {
			throw new ArgumentException("A lock file path is required.", nameof(lockFilePath));
		}
		string key = NormalizeKey(lockFilePath);
		object processLock = ProcessLocks.GetOrAdd(key, _ => new object());
		// ONE deadline across BOTH layers. The gate waits twice — first for the in-process monitor, then
		// for the cross-process file handle — and each used to start its own clock, so a caller that spent
		// almost the whole timeout on the monitor was then granted a second full timeout on the handle. A
		// gate documented as bounded at 30 s could block for nearly 60. Started here, before the first
		// wait, and what remains is handed to the second.
		long startedAt = Stopwatch.GetTimestamp();
		if (!Monitor.TryEnter(processLock, _timeout)) {
			throw new TimeoutException(
				$"Timed out waiting for the file lock '{lockFilePath}'. Another operation in this clio process is still using the guarded file.");
		}
		try {
			HashSet<string> heldLocks = EnsureThreadHeldLocks();
			if (heldLocks.Contains(key)) {
				// Already held by this thread — the outer Enter owns the handle and its release.
				return action();
			}
			using FileSystemStream lockStream = AcquireLockHandle(lockFilePath, startedAt);
			heldLocks.Add(key);
			try {
				return action();
			} finally {
				heldLocks.Remove(key);
			}
		} finally {
			Monitor.Exit(processLock);
		}
	}

	private FileSystemStream AcquireLockHandle(string lockFilePath, long startedAt) {
		EnsureLockDirectory(lockFilePath);
		while (true) {
			try {
				return _fileSystem.File.Open(
					lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			// ONLY contention is worth waiting out. A denied ACL, a path that is too long, a full disk or an
			// invalid path reads the same on every retry, so spinning on one burns the whole budget and then
			// reports "another clio process may still be using the guarded file" — which is untrue, and sends
			// diagnosis after a process that was never there. Anything that is not contention propagates
			// immediately and unchanged, so the caller sees the real cause.
			} catch (IOException exception) when (IsLockContention(exception)) {
				// Measured from when the CALLER started waiting, not from when this loop began: the monitor wait
				// above already spent part of the budget.
				if (Stopwatch.GetElapsedTime(startedAt) >= _timeout) {
					// The deadline expired while the handle was still held elsewhere. Translate rather than
					// letting the raw IOException escape, so every caller sees one failure type for "the lock
					// was not available" regardless of which of the two layers ran out of time.
					throw new TimeoutException(
						$"Timed out waiting for the file lock '{lockFilePath}'. Another clio process may still be using the guarded file.",
						exception);
				}
				Thread.Sleep(SpinMilliseconds);
			}
		}
	}

	// Windows surfaces a conflicting FileShare.None open as ERROR_SHARING_VIOLATION (32) or
	// ERROR_LOCK_VIOLATION (33), wrapped into an HRESULT under FACILITY_WIN32.
	private const int ErrorSharingViolationHResult = unchecked((int)0x80070020);
	private const int ErrorLockViolationHResult = unchecked((int)0x80070021);

	// On Unix the exclusive open goes through flock(LOCK_EX | LOCK_NB) and .NET carries the RAW errno in
	// HResult, so the contention code is EWOULDBLOCK/EAGAIN — 11 on Linux, 35 on macOS/BSD. EACCES is
	// deliberately NOT listed: on Unix that is a permissions failure, which is exactly the class of error
	// this filter exists to stop retrying.
	private const int UnixErrorAgainLinux = 11;
	private const int UnixErrorAgainBsd = 35;

	private static bool IsLockContention(IOException exception) =>
		exception.HResult is ErrorSharingViolationHResult
			or ErrorLockViolationHResult
			or UnixErrorAgainLinux
			or UnixErrorAgainBsd;

	private void EnsureLockDirectory(string lockFilePath) {
		string directory = _fileSystem.Path.GetDirectoryName(lockFilePath);
		if (!string.IsNullOrEmpty(directory) && !_fileSystem.Directory.Exists(directory)) {
			_fileSystem.Directory.CreateDirectory(directory);
		}
	}

	private string NormalizeKey(string lockFilePath) {
		try {
			return _fileSystem.Path.GetFullPath(lockFilePath);
		} catch (ArgumentException) {
			// A path the platform cannot normalise still deserves a stable monitor key; the subsequent
			// File.Open will surface the real problem.
			return lockFilePath;
		} catch (NotSupportedException) {
			return lockFilePath;
		} catch (PathTooLongException) {
			return lockFilePath;
		}
	}
}
