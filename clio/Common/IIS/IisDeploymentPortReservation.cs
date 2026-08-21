using System;
using System.IO;

namespace Clio.Common.IIS;

/// <summary>
/// Reserves an IIS deployment port across clio processes and verifies that IIS and TCP state still report it free.
/// </summary>
public interface IIisDeploymentPortReservation {

	/// <summary>
	/// Acquires the machine-wide reservation for <paramref name="port"/> and fails closed when the port is occupied
	/// or another clio deployment already owns the reservation.
	/// </summary>
	/// <param name="port">IIS port that the deployment intends to bind.</param>
	/// <returns>A lease that must remain alive until IIS has created the binding.</returns>
	IDisposable Acquire(int port);

}

/// <summary>
/// Uses a machine-wide exclusive lock file to serialize validation and mutation for one IIS deployment port.
/// </summary>
public sealed class IisDeploymentPortReservation(IAvailableIisPortService availableIisPortService)
	: IIisDeploymentPortReservation {

	private readonly IAvailableIisPortService _availableIisPortService = availableIisPortService
		?? throw new ArgumentNullException(nameof(availableIisPortService));

	/// <inheritdoc />
	public IDisposable Acquire(int port) {
		if (port is <= 0 or > 65535) {
			throw new ArgumentOutOfRangeException(nameof(port), port, "IIS port must be between 1 and 65535.");
		}

		string lockRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"Creatio", "clio", "deployment-locks");
		FileStream lockStream;
		try {
			Directory.CreateDirectory(lockRoot);
			lockStream = new FileStream(Path.Combine(lockRoot, $"iis-port-{port}.lock"),
				FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
		}
		catch (IOException) {
			throw new InvalidOperationException(
				$"IIS port {port} is already reserved by another clio deployment. Choose a different port.");
		}
		catch (UnauthorizedAccessException exception) {
			throw new InvalidOperationException(
				"Clio cannot access the machine-wide deployment lock directory. Run with an account that can read and create files under the shared Creatio clio data directory.",
				exception);
		}
		try {
			FindAvailableIisPortResult availability = _availableIisPortService.FindAsync(port, port)
				.GetAwaiter().GetResult();
			if (!string.Equals(availability.Status, "available", StringComparison.Ordinal)
				|| availability.FirstAvailablePort != port) {
				throw new InvalidOperationException(
					$"IIS port {port} is not available or could not be verified. {availability.Summary}");
			}

			return lockStream;
		}
		catch {
			lockStream.Dispose();
			throw;
		}
	}

}
