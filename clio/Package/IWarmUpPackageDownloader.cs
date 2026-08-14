namespace Clio.Package;

using System;

/// <summary>
/// Runs the fire-and-forget "warm-up" package-zip download that triggers server-side archive
/// generation and whose downloaded payload is discarded.
/// </summary>
/// <remarks>
/// The implementation owns the whole temporary-file lifecycle: it downloads into an owner-private
/// location established fail-closed, always removes it afterwards (surfacing cleanup failures rather
/// than swallowing them), and keeps a best-effort exception boundary so a failure on the worker thread
/// cannot terminate the process. This exists as an injectable seam so the lifecycle can be
/// regression-tested without touching the network.
/// </remarks>
public interface IWarmUpPackageDownloader
{

	/// <summary>
	/// Starts the warm-up download on a foreground thread, so the process stays alive until the temporary
	/// directory has been cleaned up.
	/// </summary>
	/// <param name="downloadIntoFile">
	/// Action that performs the actual download into the full temporary-file path it receives. The path
	/// points inside an owner-private temporary directory; the payload written there is discarded and the
	/// directory is always removed once the action returns or fails.
	/// </param>
	void StartWarmUpDownload(Action<string> downloadIntoFile);

}
