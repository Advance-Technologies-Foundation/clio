namespace Clio.Package;

using System;
using System.IO;
using System.Threading;
using Clio.Common;

/// <inheritdoc cref="IWarmUpPackageDownloader"/>
public class WarmUpPackageDownloader : IWarmUpPackageDownloader
{

	#region Constants: Private

	private const string WarmUpFileName = "package.zip";
	private const string TempDirectoryPrefix = "clio-warmup-";

	#endregion

	#region Fields: Private

	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public WarmUpPackageDownloader(ILogger logger){
		logger.CheckArgumentNull(nameof(logger));
		_logger = logger;
	}

	#endregion

	#region Methods: Protected

	/// <summary>
	/// Creates an owner-private temporary directory atomically and returns its full path.
	/// </summary>
	/// <remarks>
	/// Fail-closed contract: the download must only run when this returns successfully.
	/// <see cref="Directory.CreateTempSubdirectory(string)"/> creates a uniquely named directory that is
	/// owner-only (mode <c>0700</c>) on Unix in a single atomic step - closing the create-then-chmod window -
	/// and, on Windows, sits under the per-user <c>%TEMP%</c> whose ACLs already scope it to the current user.
	/// It deliberately does NOT honour <c>CLIO_WORKING_DIRECTORY</c>, so a shared working directory cannot
	/// downgrade the privacy guarantee. Virtual so tests can force the failure path.
	/// </remarks>
	/// <returns>The full path of the newly created owner-private directory.</returns>
	protected virtual string CreateOwnerPrivateDirectory() => Directory.CreateTempSubdirectory(TempDirectoryPrefix).FullName;

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Deterministic, no-throw core of the warm-up download - the entire foreground-thread body.
	/// </summary>
	/// <remarks>
	/// Internal (not private) so regression tests can drive the real temporary-file lifecycle synchronously.
	/// It never propagates an exception: if the owner-private directory cannot be established the download is
	/// SKIPPED (fail-closed), a download failure is logged, and the directory is removed in <c>finally</c> with
	/// its own guarded cleanup.
	/// </remarks>
	/// <param name="downloadIntoFile">Action that downloads into the temporary file path it receives.</param>
	internal void RunWarmUpDownload(Action<string> downloadIntoFile){
		string tempDirectory;
		try {
			tempDirectory = CreateOwnerPrivateDirectory();
		}
		catch (Exception e) {
			// Fail-closed: without a verified owner-private location we do NOT download into an unprotected one.
			_logger.WriteWarning(
				$"Package zip warm-up skipped: could not create a private temporary directory: {e.Message}");
			return;
		}
		try {
			string tempFilePath = Path.Combine(tempDirectory, WarmUpFileName);
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
		try {
			if (Directory.Exists(tempDirectory)) {
				Directory.Delete(tempDirectory, recursive: true);
			}
		}
		catch (Exception e) {
			// Surface, do not swallow: a leaked warm-up archive under the temp root must be visible in the log.
			_logger.WriteWarning(
				$"Failed to clean up warm-up temporary directory '{tempDirectory}': {e.Message}");
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void StartWarmUpDownload(Action<string> downloadIntoFile){
		downloadIntoFile.CheckArgumentNull(nameof(downloadIntoFile));
		// Foreground thread on purpose: it keeps the process alive until RunWarmUpDownload's finally has removed
		// the temporary directory, so a warm-up in flight at shutdown cannot leak its partial archive. The body
		// is no-throw, so a foreground lifetime adds no crash risk.
		Thread thread = new(() => RunWarmUpDownload(downloadIntoFile)) {
			IsBackground = false
		};
		thread.Start();
	}

	#endregion

}
