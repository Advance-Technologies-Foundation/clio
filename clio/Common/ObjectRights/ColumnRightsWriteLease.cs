using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Clio.Common.ObjectRights;

/// <summary>
/// Serializes <c>set-column-rights</c> writes to one object of one environment across every clio process on the machine.
/// A save names every column with rows, built from the rows read just before it, so two calls on one object that
/// overlap would each send the other's columns back as they were and revert the other's change while both report
/// success. The lease is an exclusive OS handle on a hashed lock file, so the operating system releases it when the
/// holding process dies — the MCP worker boundary kills a call at its budget.
/// </summary>
public interface IColumnRightsWriteLease {
	/// <summary>
	/// Takes the lease for <paramref name="environment"/> and <paramref name="schemaName"/>, or refuses at once when
	/// another call holds it: a waiting call would plan on rows the running one is about to change.
	/// </summary>
	/// <param name="environment">The environment's name, or its URL when no name was given.</param>
	/// <param name="schemaName">The object's code.</param>
	/// <returns>The lease; dispose it after the read-back.</returns>
	/// <exception cref="InvalidOperationException">Another call holds the lease, or the lock directory cannot be
	/// used; the message says which.</exception>
	IDisposable Acquire(string environment, string schemaName);
}

/// <inheritdoc />
public sealed class ColumnRightsWriteLease : IColumnRightsWriteLease {

	private readonly string _lockRoot;

	/// <summary>Creates the lease over the clio data directory, next to the deployment locks.</summary>
	public ColumnRightsWriteLease()
		: this(Path.Combine(System.Environment.GetFolderPath(OperatingSystem.IsWindows()
			? System.Environment.SpecialFolder.CommonApplicationData
			: System.Environment.SpecialFolder.LocalApplicationData), "Creatio", "clio", "column-rights-locks")) {
	}

	/// <summary>Creates the lease over <paramref name="lockRoot"/>; tests use a folder of their own.</summary>
	/// <param name="lockRoot">The directory that holds the lock files.</param>
	internal ColumnRightsWriteLease(string lockRoot) {
		_lockRoot = lockRoot;
	}

	/// <inheritdoc />
	public IDisposable Acquire(string environment, string schemaName) {
		string identity = $"{(environment ?? string.Empty).Trim().ToUpperInvariant()}|{schemaName.ToUpperInvariant()}";
		string lockKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
		try {
			Directory.CreateDirectory(_lockRoot);
			return new FileStream(Path.Combine(_lockRoot, $"column-rights-{lockKey}.lock"), FileMode.OpenOrCreate,
				FileAccess.Read, FileShare.None);
		}
		catch (IOException) {
			throw new InvalidOperationException($"another set-column-rights call is changing '{schemaName}' on this "
				+ "environment; calls for one object must not run in parallel. Nothing was changed: re-run it after that "
				+ "call completes.");
		}
		catch (UnauthorizedAccessException exception) {
			throw new InvalidOperationException("clio cannot use its column-rights lock directory "
				+ $"'{_lockRoot}'; nothing was changed.", exception);
		}
	}
}
