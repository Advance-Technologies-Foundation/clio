using System;
using System.Globalization;
using System.IO;

namespace Clio.Common.McpWorker;

using IFileSystem = System.IO.Abstractions.IFileSystem;

/// <inheritdoc cref="IWorkerTempResidueSweeper"/>
public sealed class WorkerTempResidueSweeper : IWorkerTempResidueSweeper {

	/// <summary>
	/// How old a residual directory must be before it is removed.
	/// </summary>
	/// <remarks>
	/// The age gate is the whole safety argument, so it is generous on purpose. A directory younger than
	/// this may belong to a clio process that is still running — another MCP host, a plain CLI invocation,
	/// or one of this host's own live workers — and no cheap check distinguishes "abandoned an hour ago"
	/// from "in use right now": the working directory carries no owner and no lock. A day is far longer
	/// than any single clio operation and still bounds the accumulation to what one day produces.
	/// </remarks>
	internal static readonly TimeSpan DefaultResidueAge = TimeSpan.FromDays(1);

	private readonly IWorkingDirectoriesProvider _workingDirectories;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly TimeSpan _residueAge;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerTempResidueSweeper"/> class.
	/// </summary>
	/// <param name="workingDirectories">The provider that owns the temporary root being swept.</param>
	/// <param name="fileSystem">The file system.</param>
	/// <param name="logger">The host logger.</param>
	public WorkerTempResidueSweeper(IWorkingDirectoriesProvider workingDirectories, IFileSystem fileSystem,
		ILogger logger)
		: this(workingDirectories, fileSystem, logger, DefaultResidueAge) {
	}

	// Tests drive the age gate rather than waiting a day for it, so the interval is injectable — but only
	// from inside the assembly: an age chosen by a caller is an age chosen by somebody who does not know
	// which other clio processes are running.
	internal WorkerTempResidueSweeper(IWorkingDirectoriesProvider workingDirectories, IFileSystem fileSystem,
		ILogger logger, TimeSpan residueAge) {
		_workingDirectories = workingDirectories ?? throw new ArgumentNullException(nameof(workingDirectories));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_residueAge = residueAge;
	}

	/// <inheritdoc />
	public WorkerTempResidueSweepReport Sweep() {
		string root = _workingDirectories.BaseTempDirectory;
		if (!_fileSystem.Directory.Exists(root)) {
			return new WorkerTempResidueSweepReport(0, 0);
		}
		int removed = 0;
		int retained = 0;
		DateTime cutoffUtc = DateTime.UtcNow - _residueAge;
		foreach (string directory in EnumerateCandidates(root)) {
			if (!IsGeneratedWorkingDirectoryName(_fileSystem.Path.GetFileName(directory))) {
				// Not ours. The same root holds marketplace downloads and whatever a future feature puts
				// there, and a sweep that deletes what it does not recognise is a data-loss bug waiting
				// for its first report.
				continue;
			}
			if (!IsOlderThan(directory, cutoffUtc)) {
				retained++;
				continue;
			}
			if (TryDelete(directory)) {
				removed++;
			} else {
				retained++;
			}
		}
		if (removed > 0) {
			_logger.WriteInfo(string.Create(CultureInfo.InvariantCulture,
				$"Removed {removed} abandoned working director(ies) under {root}."));
		}
		return new WorkerTempResidueSweepReport(removed, retained);
	}

	// A 32-character lowercase hex name — exactly what Guid.ToString("N") produces, which is what
	// WorkingDirectoriesProvider.GenerateTempDirectoryPath returns. Matched by SHAPE rather than by
	// parsing, because Guid.TryParse also accepts the braced and hyphenated forms this method never emits.
	private static bool IsGeneratedWorkingDirectoryName(string name) {
		if (name is not { Length: 32 }) {
			return false;
		}
		foreach (char character in name) {
			bool isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
			if (!isHex) {
				return false;
			}
		}
		return true;
	}

	private string[] EnumerateCandidates(string root) {
		try {
			return _fileSystem.Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
		}
		catch (Exception exception) when (IsFileSystemFailure(exception)) {
			// An unreadable temporary root is not worth a warning on every start: the sweep is a courtesy,
			// and the next run will try again.
			_logger.WriteDebug($"Working-directory sweep skipped: {exception.Message}");
			return [];
		}
	}

	private bool IsOlderThan(string directory, DateTime cutoffUtc) {
		try {
			// The LATER of the two stamps. A directory created a week ago and written to a minute ago is in
			// use, and reading only the creation time would delete it out from under its owner — which is
			// the single most damaging mistake this sweep could make.
			DateTime lastWriteUtc = _fileSystem.Directory.GetLastWriteTimeUtc(directory);
			DateTime creationUtc = _fileSystem.Directory.GetCreationTimeUtc(directory);
			DateTime newest = lastWriteUtc > creationUtc ? lastWriteUtc : creationUtc;
			return newest < cutoffUtc;
		}
		catch (Exception exception) when (IsFileSystemFailure(exception)) {
			// Unreadable stamps mean unknown age, and unknown age is treated as young.
			_logger.WriteDebug($"Working-directory age could not be read for {directory}: {exception.Message}");
			return false;
		}
	}

	private bool TryDelete(string directory) {
		try {
			_fileSystem.Directory.Delete(directory, recursive: true);
			return true;
		}
		catch (Exception exception) when (IsFileSystemFailure(exception)) {
			// Locked by a process this one cannot see, or not ours to delete. Left for the next sweep.
			_logger.WriteDebug($"Working directory {directory} was not removed: {exception.Message}");
			return false;
		}
	}

	private static bool IsFileSystemFailure(Exception exception) =>
		exception is IOException
			or UnauthorizedAccessException
			or ArgumentException
			or NotSupportedException;
}
