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

	/// <summary>
	/// Scans the inclusive range in ascending order and atomically reserves the first IIS port that remains free.
	/// </summary>
	/// <param name="rangeStart">Inclusive first candidate port.</param>
	/// <param name="rangeEnd">Inclusive last candidate port.</param>
	/// <returns>A lease exposing the selected port; keep it alive until IIS creates the binding.</returns>
	IisDeploymentPortLease AcquireFirstAvailable(int rangeStart, int rangeEnd);

}

/// <summary>Represents an owned IIS deployment port reservation.</summary>
public sealed class IisDeploymentPortLease : IDisposable {
	private readonly IDisposable _reservation;

	internal IisDeploymentPortLease(int port, IDisposable reservation) {
		Port = port;
		_reservation = reservation;
	}

	/// <summary>Gets the reserved port.</summary>
	public int Port { get; }

	/// <inheritdoc />
	public void Dispose() => _reservation.Dispose();
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
		ValidatePort(port, nameof(port));
		FileStream lockStream = TryOpenLock(port);
		if (lockStream is null) {
			throw new InvalidOperationException(
				$"IIS port {port} is already reserved by another clio deployment. Choose a different port.");
		}
		try {
			FindAvailableIisPortResult availability = Find(port, port);
			if (!IsAvailable(availability, port)) {
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

	/// <inheritdoc />
	public IisDeploymentPortLease AcquireFirstAvailable(int rangeStart, int rangeEnd) {
		ValidatePort(rangeStart, nameof(rangeStart));
		ValidatePort(rangeEnd, nameof(rangeEnd));
		if (rangeStart > rangeEnd) {
			throw new ArgumentException("The IIS port range start must be less than or equal to its end.");
		}

		int candidateStart = rangeStart;
		string lastSummary = "No port in the range was reported available.";
		while (candidateStart <= rangeEnd) {
			FindAvailableIisPortResult scan = Find(candidateStart, rangeEnd);
			lastSummary = scan.Summary;
			if (!string.Equals(scan.Status, "available", StringComparison.Ordinal)
				|| scan.FirstAvailablePort is not int candidate) {
				break;
			}

			FileStream lockStream = TryOpenLock(candidate);
			if (lockStream is null) {
				candidateStart = candidate + 1;
				continue;
			}
			try {
				FindAvailableIisPortResult revalidation = Find(candidate, candidate);
				lastSummary = revalidation.Summary;
				if (IsAvailable(revalidation, candidate)) {
					return new IisDeploymentPortLease(candidate, lockStream);
				}
			}
			catch {
				lockStream.Dispose();
				throw;
			}
			lockStream.Dispose();
			candidateStart = candidate + 1;
		}

		throw new InvalidOperationException(
			$"No available IIS port could be reserved in the configured range [{rangeStart}, {rangeEnd}]. {lastSummary}");
	}

	private FindAvailableIisPortResult Find(int rangeStart, int rangeEnd) =>
		_availableIisPortService.FindAsync(rangeStart, rangeEnd).GetAwaiter().GetResult();

	private static bool IsAvailable(FindAvailableIisPortResult result, int port) =>
		string.Equals(result.Status, "available", StringComparison.Ordinal)
		&& result.FirstAvailablePort == port;

	private static FileStream TryOpenLock(int port) {
		string lockRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"Creatio", "clio", "deployment-locks");
		try {
			Directory.CreateDirectory(lockRoot);
			return new FileStream(Path.Combine(lockRoot, $"iis-port-{port}.lock"),
				FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
		}
		catch (IOException) {
			return null;
		}
		catch (UnauthorizedAccessException exception) {
			throw new InvalidOperationException(
				"Clio cannot access the machine-wide deployment lock directory. Run with an account that can read and create files under the shared Creatio clio data directory.",
				exception);
		}
	}

	private static void ValidatePort(int port, string parameterName) {
		if (port is <= 0 or > 65535) {
			throw new ArgumentOutOfRangeException(parameterName, port, "IIS port must be between 1 and 65535.");
		}
	}

}
