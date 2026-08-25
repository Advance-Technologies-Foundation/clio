using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Clio.Common.IIS;

/// <summary>Serializes Creatio deployment mutations across clio processes that target the same canonical directory.</summary>
public interface IDeploymentTargetReservation {
	/// <summary>Acquires a cross-process exclusive lease for a logical environment name.</summary>
	/// <param name="environmentName">Environment name used for registration and name-scoped integrations.</param>
	/// <returns>A lease that must remain alive for the complete deploy or named uninstall operation.</returns>
	IDisposable AcquireEnvironment(string environmentName);

	/// <summary>Acquires a cross-process exclusive lease for a canonical deployment directory.</summary>
	/// <param name="canonicalTargetPath">Fully qualified deployment directory.</param>
	/// <returns>A lease that must remain alive for the complete deploy or uninstall operation.</returns>
	IDisposable Acquire(string canonicalTargetPath);

}

/// <summary>Uses a hashed lock-file name to serialize mutations without exposing the target path.</summary>
public sealed class DeploymentTargetReservation : IDeploymentTargetReservation {
	/// <inheritdoc />
	public IDisposable AcquireEnvironment(string environmentName) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name must not be empty.", nameof(environmentName));
		}
		return AcquireCore("environment", environmentName.Trim().ToUpperInvariant(),
			$"Creatio environment '{environmentName}' is already being changed by another clio process. Try again after it completes.");
	}

	/// <inheritdoc />
	public IDisposable Acquire(string canonicalTargetPath) {
		if (string.IsNullOrWhiteSpace(canonicalTargetPath)
			|| !Path.IsPathFullyQualified(canonicalTargetPath)) {
			throw new ArgumentException("Deployment target path must be fully qualified.", nameof(canonicalTargetPath));
		}
		string normalizedPath = DirectoryPathIdentity.Normalize(canonicalTargetPath);
		return AcquireCore("target", normalizedPath.ToUpperInvariant(),
			$"Creatio deployment target '{normalizedPath}' is already being changed by another clio process. Try again after it completes.");
	}

	private static IDisposable AcquireCore(string kind, string identity, string collisionMessage) {
		string lockKey = Convert.ToHexString(SHA256.HashData(
			Encoding.UTF8.GetBytes(identity)));
		string applicationData = Environment.GetFolderPath(OperatingSystem.IsWindows()
			? Environment.SpecialFolder.CommonApplicationData
			: Environment.SpecialFolder.LocalApplicationData);
		string lockRoot = Path.Combine(applicationData,
			"Creatio", "clio", "deployment-locks");
		try {
			Directory.CreateDirectory(lockRoot);
			return new FileStream(Path.Combine(lockRoot, $"{kind}-{lockKey}.lock"),
				FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
		}
		catch (IOException) {
			throw new InvalidOperationException(collisionMessage);
		}
		catch (UnauthorizedAccessException exception) {
			throw new InvalidOperationException(
				"Clio cannot access the deployment lock directory. Run with an account that can read and create files under the Creatio clio data directory.",
				exception);
		}
	}

}
