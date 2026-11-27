using System;
using System.IO;

namespace Clio.Common.Skills;

// Declared INSIDE the namespace on purpose: a compilation-unit alias would lose to
// Clio.Common.IFileSystem, which name lookup finds while walking up the enclosing namespaces.
using IFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>
/// Serializes toolkit install/update/delete across clio processes on the machine.
/// </summary>
/// <remarks>
/// <para>
/// A toolkit operation drives a multi-step agent CLI sequence (Codex: marketplace remove, add, plugin add).
/// Two interleaved sequences, for example the startup auto-update's background child and a manual
/// <c>update-toolkit</c>, can leave an agent with the marketplace removed and the plugin missing.
/// </para>
/// <para>
/// The lock is fail-fast rather than waiting: an operation takes seconds and performs network calls, so a
/// waiter would block for an unbounded time, and the loser has nothing useful left to do once the other
/// process finishes the same work. This is also why <c>InterprocessFileGate</c>, whose contract is a short
/// disk-only critical section with a 30-second wait, is not used here.
/// </para>
/// </remarks>
public interface IToolkitOperationLock {
	/// <summary>
	/// Tries to take the machine-wide toolkit operation lock without waiting.
	/// </summary>
	/// <returns>
	/// A handle to dispose when the operation ends, or <c>null</c> when another clio process holds the lock.
	/// When the lock file cannot be created at all, a no-op handle is returned and the operation runs
	/// unguarded rather than being refused.
	/// </returns>
	IDisposable TryAcquire();
}

/// <inheritdoc />
public sealed class ToolkitOperationLock(IFileSystem fileSystem, IUserHomeProvider homeProvider) : IToolkitOperationLock {
	internal const string LockFileName = "toolkit-operation.lock";

	/// <inheritdoc />
	public IDisposable TryAcquire() {
		string directory = homeProvider.GetClioDir();
		string path = fileSystem.Path.Combine(directory, LockFileName);
		try {
			fileSystem.Directory.CreateDirectory(directory);
			// Exclusion is the open handle (FileShare.None), never the file's existence: the OS releases a
			// handle when its process dies, so a killed operation cannot strand the lock. The file is never
			// deleted.
			return fileSystem.File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		}
		catch (IOException) when (fileSystem.File.Exists(path)) {
			// The sentinel exists as a file but cannot be opened exclusively: another clio process holds it.
			return null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// The sentinel cannot be created at all (read-only home, full disk, a directory in its place).
			// Refusing every toolkit operation for as long as that lasts would be worse than the race the
			// lock guards against, so proceed unguarded.
			return NoLock.Instance;
		}
	}

	private sealed class NoLock : IDisposable {
		public static readonly NoLock Instance = new();

		public void Dispose() {
		}
	}
}
