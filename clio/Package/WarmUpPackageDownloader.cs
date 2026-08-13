namespace Clio.Package;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Clio.Common;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

/// <inheritdoc cref="IWarmUpPackageDownloader"/>
public class WarmUpPackageDownloader : IWarmUpPackageDownloader
{

	#region Constants: Private

	private const string WarmUpFileName = "package.zip";

	#endregion

	#region Fields: Private

	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IAbstractionsFileSystem _fileSystem;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public WarmUpPackageDownloader(IWorkingDirectoriesProvider workingDirectoriesProvider,
			IAbstractionsFileSystem fileSystem, ILogger logger){
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		logger.CheckArgumentNull(nameof(logger));
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_logger = logger;
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Deterministic, no-throw core of the warm-up download — the entire background-thread body.
	/// </summary>
	/// <remarks>
	/// Kept internal (not private) so regression tests can drive the temporary-file lifecycle synchronously,
	/// without spawning a thread. It never propagates an exception: a download failure is logged, and the
	/// temporary directory is removed in <c>finally</c> with its own guarded cleanup.
	/// </remarks>
	/// <param name="downloadIntoFile">Action that downloads into the temporary file path it receives.</param>
	internal void RunWarmUpDownload(Action<string> downloadIntoFile){
		string tempDirectory = null;
		try {
			// Acquire the temp location INSIDE the boundary: if directory creation itself fails, the failure
			// is logged rather than escaping the raw background thread and terminating the process.
			tempDirectory = _workingDirectoriesProvider.CreateTempDirectory();
			RestrictToOwner(tempDirectory);
			string tempFilePath = _fileSystem.Path.Combine(tempDirectory, WarmUpFileName);
			downloadIntoFile(tempFilePath);
		}
		catch (Exception e) {
			// Warm-up is best-effort: the real download runs separately, so a failure here is not fatal.
			_logger.WriteWarning($"Package zip warm-up download failed: {e.Message}");
		}
		finally {
			CleanUp(tempDirectory);
		}
	}

	#endregion

	#region Methods: Private

	private void CleanUp(string tempDirectory){
		if (tempDirectory is null) {
			return;
		}
		try {
			_workingDirectoriesProvider.DeleteDirectoryIfExists(tempDirectory);
		}
		catch (Exception e) {
			// Surface, do not swallow: a leaked warm-up archive under the temp root must be visible in the log.
			_logger.WriteWarning(
				$"Failed to clean up warm-up temporary directory '{tempDirectory}': {e.Message}");
		}
	}

	private void RestrictToOwner(string directory){
		// On Windows the per-user temp root already restricts access through inherited ACLs; there is no
		// portable POSIX-mode equivalent to apply here.
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return;
		}
		try {
			// Best-effort owner-only (0700) directory so the discarded payload the external downloader writes
			// with FileMode.Create cannot be traversed by another local user while it exists. Uses the concrete
			// filesystem intentionally: this is a real OS security operation the abstraction does not model, and
			// it is a no-op for a non-existent path (e.g. a substituted provider's path in unit tests).
			if (Directory.Exists(directory)) {
				File.SetUnixFileMode(directory,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			}
		}
		catch (Exception e) {
			_logger.WriteWarning($"Could not restrict warm-up temporary directory permissions: {e.Message}");
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void StartWarmUpDownload(Action<string> downloadIntoFile){
		downloadIntoFile.CheckArgumentNull(nameof(downloadIntoFile));
		Thread thread = new(() => RunWarmUpDownload(downloadIntoFile)) {
			IsBackground = true
		};
		thread.Start();
	}

	#endregion

}
