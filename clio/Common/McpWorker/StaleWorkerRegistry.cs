using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;

namespace Clio.Common.McpWorker;

// Declared INSIDE the namespace on purpose: a compilation-unit alias would lose to
// Clio.Common.IFileSystem, which name lookup finds while walking up the enclosing namespaces.
using IFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>
/// The identity of a running process, as observed from outside it.
/// </summary>
/// <param name="ProcessId">Operating-system process identifier.</param>
/// <param name="StartTimeUtcTicks">UTC start time in ticks.</param>
/// <param name="ExecutablePath">Absolute path of the running executable.</param>
public sealed record ProcessIdentitySnapshot(int ProcessId, long StartTimeUtcTicks, string ExecutablePath);

/// <summary>
/// One worker recorded on disk so that a future clio process can clean it up if this one dies without
/// running any cleanup.
/// </summary>
/// <param name="ProcessId">The worker's process identifier.</param>
/// <param name="StartTimeUtcTicks">The worker's UTC start time in ticks.</param>
/// <param name="ExecutablePath">The worker's executable path, as observed at spawn.</param>
/// <param name="OwnerProcessId">The parent (supervising) process identifier.</param>
/// <param name="OwnerStartTimeUtcTicks">The parent's UTC start time in ticks.</param>
/// <param name="OwnerExecutablePath">The parent's executable path.</param>
/// <param name="RecordedAtUtc">When the entry was written.</param>
/// <remarks>
/// The owner identity is recorded, and checked with the same triple, because more than one clio MCP
/// server can run on a machine at once. Without it a starting parent would reap the LIVE workers of
/// its healthy neighbour — a self-inflicted version of the very failure this feature removes.
/// </remarks>
public sealed record WorkerRegistrationEntry(
	int ProcessId,
	long StartTimeUtcTicks,
	string ExecutablePath,
	int OwnerProcessId,
	long OwnerStartTimeUtcTicks,
	string OwnerExecutablePath,
	DateTimeOffset RecordedAtUtc);

/// <summary>
/// What one stale-worker reap did.
/// </summary>
/// <param name="Inspected">Entries read from the registry.</param>
/// <param name="Terminated">Workers whose identity matched and which were killed.</param>
/// <param name="StrangersSkipped">
/// Entries whose process id is now absent or belongs to a different process. Never killed; the entry is
/// dropped so a reused identifier does not stay in the file forever.
/// </param>
/// <param name="LiveOwnersSkipped">
/// Entries belonging to another clio parent that is still running. Left in the file — they are that
/// parent's business, not this one's.
/// </param>
/// <param name="Warnings">Human-readable notes about entries that could not be processed.</param>
public sealed record StaleWorkerReapReport(
	int Inspected,
	int Terminated,
	int StrangersSkipped,
	int LiveOwnersSkipped,
	IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a process's identity and terminates a verified stale worker.
/// </summary>
/// <remarks>
/// Implemented by <see cref="WorkerProcessSupervisor"/>, the one class in this feature allowed to touch
/// <see cref="System.Diagnostics.Process"/>. It is a seam so that the identity gate itself can be unit
/// tested: a real process-id collision cannot be manufactured, but a substituted inspector reporting a
/// stranger can, and that is the case AC-02 is about.
/// </remarks>
public interface IWorkerProcessInspector {

	/// <summary>Captures the identity of a running process.</summary>
	/// <param name="processId">The process identifier to inspect.</param>
	/// <returns>The identity, or <see langword="null"/> when no such process exists or it cannot be read.</returns>
	ProcessIdentitySnapshot TryCaptureIdentity(int processId);

	/// <summary>
	/// Terminates a worker whose identity the caller has already revalidated, together with the
	/// descendants its containment covers.
	/// </summary>
	/// <param name="entry">The verified registry entry.</param>
	/// <returns>What was signalled.</returns>
	WorkerTerminationOutcome TerminateStaleWorker(WorkerRegistrationEntry entry);
}

/// <summary>
/// The on-disk record of live workers, and the identity-checked cleanup that uses it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a file at all.</b> A parent that is <c>SIGKILL</c>ed runs no cleanup, so the only way a later
/// parent can know what to look for is a record written before the fact. Windows containment makes such
/// orphans impossible by construction (kill-on-close), and Unix parent-death signalling makes them
/// unlikely; this is the backstop for the cases neither covers — a worker whose arming had not yet
/// completed, or a platform where it is unavailable.
/// </para>
/// <para>
/// <b>Why identity is checked, and checked LAST.</b> Process identifiers are reused. Between writing an
/// entry and reading it, the recorded number can belong to the user's editor. So a kill is issued only
/// after the full triple — identifier, start time, executable path — is re-read from the live process
/// immediately beforehand. The file lock below is mutual exclusion only; it says nothing about who owns
/// a process identifier and cannot replace the triple.
/// </para>
/// </remarks>
public interface IStaleWorkerRegistry {

	/// <summary>Records a worker as live.</summary>
	/// <param name="entry">The entry to add.</param>
	void Record(WorkerRegistrationEntry entry);

	/// <summary>Removes a worker from the record, by identity.</summary>
	/// <param name="processId">The worker's process identifier.</param>
	/// <param name="startTimeUtcTicks">The worker's start time in ticks.</param>
	void Remove(int processId, long startTimeUtcTicks);

	/// <summary>Reads every recorded entry.</summary>
	/// <returns>The entries, or an empty list when the registry is missing or unreadable.</returns>
	IReadOnlyList<WorkerRegistrationEntry> Read();

	/// <summary>
	/// Kills workers recorded by parents that are no longer running, revalidating each candidate's
	/// identity immediately before the kill.
	/// </summary>
	/// <param name="inspector">Reads identities and performs the kill.</param>
	/// <returns>What was found, killed and discarded.</returns>
	StaleWorkerReapReport Reap(IWorkerProcessInspector inspector);
}

/// <inheritdoc />
public sealed class StaleWorkerRegistry : IStaleWorkerRegistry {

	private const string RegistryDirectoryName = "mcp-workers";
	private const string RegistryFileName = "workers.json";
	private const string LockDirectoryName = ".locks";
	private const string LockFileName = "workers.lock";

	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	private readonly IFileSystem _fileSystem;
	private readonly IInterprocessFileGate _fileGate;
	private readonly string _registryDirectory;

	/// <summary>
	/// Initializes a new instance of the <see cref="StaleWorkerRegistry"/> class rooted under clio's
	/// per-user home directory.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction.</param>
	/// <param name="fileGate">
	/// Interprocess gate that serialises the read-modify-write of the registry file, so two parents
	/// starting at once cannot lose one another's entries or reap concurrently.
	/// </param>
	public StaleWorkerRegistry(IFileSystem fileSystem, IInterprocessFileGate fileGate)
		: this(fileSystem, fileGate, null) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="StaleWorkerRegistry"/> class rooted at an explicit
	/// directory. Used by tests, which must not write into the developer's real clio home.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction.</param>
	/// <param name="fileGate">Interprocess gate that serialises registry mutation.</param>
	/// <param name="registryDirectory">Directory holding the registry file; clio home when null.</param>
	internal StaleWorkerRegistry(IFileSystem fileSystem, IInterprocessFileGate fileGate,
		string registryDirectory) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_fileGate = fileGate ?? throw new ArgumentNullException(nameof(fileGate));
		_registryDirectory = registryDirectory
			?? _fileSystem.Path.Combine(ClioRuntimePaths.Home, RegistryDirectoryName);
	}

	private string RegistryPath => _fileSystem.Path.Combine(_registryDirectory, RegistryFileName);

	// The sentinel lives in a sibling directory of the registry file so nothing that rewrites the
	// registry can unlink the lock from under its holder.
	private string LockPath => _fileSystem.Path.Combine(_registryDirectory, LockDirectoryName, LockFileName);

	/// <inheritdoc />
	public void Record(WorkerRegistrationEntry entry) {
		ArgumentNullException.ThrowIfNull(entry);
		_fileGate.Enter(LockPath, () => {
			List<WorkerRegistrationEntry> entries = ReadUnguarded()
				.Where(existing => !IsSameWorker(existing, entry.ProcessId, entry.StartTimeUtcTicks))
				.ToList();
			entries.Add(entry);
			WriteUnguarded(entries);
		});
	}

	/// <inheritdoc />
	public void Remove(int processId, long startTimeUtcTicks) {
		_fileGate.Enter(LockPath, () => {
			List<WorkerRegistrationEntry> entries = ReadUnguarded();
			int removed = entries.RemoveAll(entry => IsSameWorker(entry, processId, startTimeUtcTicks));
			if (removed > 0) {
				WriteUnguarded(entries);
			}
		});
	}

	/// <inheritdoc />
	public IReadOnlyList<WorkerRegistrationEntry> Read() {
		return _fileGate.Enter<IReadOnlyList<WorkerRegistrationEntry>>(LockPath, ReadUnguarded);
	}

	/// <inheritdoc />
	public StaleWorkerReapReport Reap(IWorkerProcessInspector inspector) {
		ArgumentNullException.ThrowIfNull(inspector);
		return _fileGate.Enter(LockPath, () => ReapUnguarded(inspector));
	}

	private StaleWorkerReapReport ReapUnguarded(IWorkerProcessInspector inspector) {
		List<WorkerRegistrationEntry> entries = ReadUnguarded();
		List<WorkerRegistrationEntry> survivors = [];
		List<string> warnings = [];
		int terminated = 0;
		int strangers = 0;
		int liveOwners = 0;
		int currentProcessId = System.Environment.ProcessId;

		foreach (WorkerRegistrationEntry entry in entries) {
			if (entry.OwnerProcessId == currentProcessId) {
				// Written by this very process; it is a live lease, not a leftover.
				survivors.Add(entry);
				continue;
			}
			if (IsOwnerStillRunning(inspector, entry)) {
				liveOwners++;
				survivors.Add(entry);
				continue;
			}
			ProcessIdentitySnapshot actual = inspector.TryCaptureIdentity(entry.ProcessId);
			if (actual is null || !MatchesRecordedWorker(actual, entry)) {
				// Absent, or the identifier now belongs to somebody else. Never kill it; drop the entry
				// so a reused identifier does not stay in the file forever.
				strangers++;
				continue;
			}
			WorkerTerminationOutcome outcome = inspector.TerminateStaleWorker(entry);
			if (outcome == WorkerTerminationOutcome.Failed) {
				warnings.Add(
					$"Stale worker {entry.ProcessId} matched its recorded identity but could not be terminated; it is left in the registry for the next attempt.");
				survivors.Add(entry);
				continue;
			}
			terminated++;
		}

		if (survivors.Count != entries.Count) {
			WriteUnguarded(survivors);
		}
		return new StaleWorkerReapReport(entries.Count, terminated, strangers, liveOwners, warnings);
	}

	private static bool IsOwnerStillRunning(IWorkerProcessInspector inspector, WorkerRegistrationEntry entry) {
		ProcessIdentitySnapshot owner = inspector.TryCaptureIdentity(entry.OwnerProcessId);
		return owner is not null
			&& owner.StartTimeUtcTicks == entry.OwnerStartTimeUtcTicks
			&& PathsEqual(owner.ExecutablePath, entry.OwnerExecutablePath);
	}

	private static bool MatchesRecordedWorker(ProcessIdentitySnapshot actual, WorkerRegistrationEntry entry) {
		return actual.ProcessId == entry.ProcessId
			&& actual.StartTimeUtcTicks == entry.StartTimeUtcTicks
			&& PathsEqual(actual.ExecutablePath, entry.ExecutablePath);
	}

	private static bool PathsEqual(string left, string right) {
		if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) {
			// An unreadable path is not a match. Half an identity is not an identity, and the cost of
			// refusing is one surviving orphan; the cost of guessing is a stranger's process.
			return false;
		}
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		return string.Equals(left, right, comparison);
	}

	private static bool IsSameWorker(WorkerRegistrationEntry entry, int processId, long startTimeUtcTicks) {
		return entry.ProcessId == processId && entry.StartTimeUtcTicks == startTimeUtcTicks;
	}

	private List<WorkerRegistrationEntry> ReadUnguarded() {
		if (!_fileSystem.File.Exists(RegistryPath)) {
			return [];
		}
		try {
			string content = _fileSystem.File.ReadAllText(RegistryPath);
			if (string.IsNullOrWhiteSpace(content)) {
				return [];
			}
			return JsonSerializer.Deserialize<List<WorkerRegistrationEntry>>(content) ?? [];
		} catch (JsonException) {
			// A corrupt registry must not stop clio from starting. The worst case of treating it as empty
			// is that an orphan survives, which the containment layers are there to prevent anyway.
			return [];
		} catch (System.IO.IOException) {
			return [];
		} catch (UnauthorizedAccessException) {
			return [];
		}
	}

	private void WriteUnguarded(List<WorkerRegistrationEntry> entries) {
		EnsureRegistryDirectory();
		string temporaryPath = RegistryPath + ".tmp";
		_fileSystem.File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, SerializerOptions));
		// Atomic replace, so a concurrently starting parent that reads WITHOUT the gate (a future caller,
		// or a different clio version) can never observe a truncated prefix.
		_fileSystem.File.Move(temporaryPath, RegistryPath, overwrite: true);
	}

	private void EnsureRegistryDirectory() {
		if (!_fileSystem.Directory.Exists(_registryDirectory)) {
			_fileSystem.Directory.CreateDirectory(_registryDirectory);
		}
	}
}
