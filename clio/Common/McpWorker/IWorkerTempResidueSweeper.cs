using System;

namespace Clio.Common.McpWorker;

/// <summary>
/// What one sweep of the clio temporary root removed.
/// </summary>
/// <param name="Removed">How many residual working directories were deleted.</param>
/// <param name="Retained">
/// How many were left in place — either because they are younger than the safety age or because the
/// operating system refused the delete.
/// </param>
/// <remarks>A data-only carrier, so it is a <see langword="record"/> per the DI policy.</remarks>
public sealed record WorkerTempResidueSweepReport(int Removed, int Retained);

/// <summary>
/// Removes the per-operation working directories that a killed clio process could not remove itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>IWorkingDirectoriesProvider.CreateTempDirectory</c> deletes its directory in a <c>finally</c>, which
/// is exactly the construct a killed process does not run. Before ENG-95262 that mattered rarely — a clio
/// run ended by Ctrl+C. Under the worker execution boundary the parent kills a child on every budget
/// expiry, every cancellation and every stale reap, so what used to be an occasional leftover becomes a
/// steady accumulation of unpacked packages under the user's temporary directory, growing until somebody
/// notices the disk.
/// </para>
/// <para>
/// The sweep is deliberately narrow. It removes only DIRECTORIES whose name is the 32-hex form
/// <c>WorkingDirectoriesProvider.GenerateTempDirectoryPath</c> produces, and only those older than
/// <see cref="WorkerTempResidueSweeper.DefaultResidueAge"/> — a marketplace download written straight into
/// the same root is a file, not a directory, and a directory that a concurrent clio process is using right
/// now is younger than the age gate. Anything it cannot delete is counted and left; cleanup that fights
/// another process for a lock is worse than cleanup that runs again tomorrow.
/// </para>
/// </remarks>
public interface IWorkerTempResidueSweeper {

	/// <summary>
	/// Deletes residual working directories older than the safety age.
	/// </summary>
	/// <returns>What the sweep removed and what it left behind.</returns>
	WorkerTempResidueSweepReport Sweep();
}
