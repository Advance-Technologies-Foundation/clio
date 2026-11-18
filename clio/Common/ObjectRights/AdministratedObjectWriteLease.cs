using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Clio.Common.ObjectRights;

/// <summary>
/// Serializes the writes of one administrated object of one environment across the clio processes that share the lock
/// directory — those of one OS user (the user's local application data); on Windows, those of the machine (its common
/// application data):
/// <c>set-object-rights</c>, <c>set-default-record-rights</c> and <c>set-column-rights</c> all save through
/// <c>SaveAdministratedObject</c>, which carries the object's three switches as the writer read them, and
/// <c>set-column-rights</c> sends every restricted column as it read them. Two writes of one object that overlap would
/// revert each other while both report success. The lease is an exclusive OS handle on a hashed lock file, so the
/// operating system releases it when the holding process dies — the MCP worker boundary kills a call at its budget.
/// </summary>
public interface IAdministratedObjectWriteLease {
	/// <summary>
	/// Takes the lease for <paramref name="schemaName"/> on the environment this command runs against (its resolved
	/// URL), or refuses at once when another call holds it: a waiting call would plan on a state the running one is
	/// about to change.
	/// </summary>
	/// <param name="schemaName">The object's code.</param>
	/// <returns>The lease; dispose it after the read-back.</returns>
	/// <returns>An <see cref="UnheldLease"/> when no lock folder can be used; the write then goes on without the lease,
	/// and the caller warns.</returns>
	/// <exception cref="InvalidOperationException">Another call holds the lease.</exception>
	IDisposable Acquire(string schemaName);
}

/// <inheritdoc />
/// <summary>
/// What <see cref="IAdministratedObjectWriteLease.Acquire"/> returns when no lock folder could be used (a read-only
/// container, a service account without a writable profile): the write goes on without the lease, and the caller says
/// so. The fresh read right before the save still refuses a change made since the plan.
/// </summary>
/// <param name="Reason">Why no lock folder could be used.</param>
public sealed record UnheldLease(string Reason) : IDisposable {
	/// <inheritdoc />
	public void Dispose() { }
}

/// <inheritdoc />
public sealed class AdministratedObjectWriteLease : IAdministratedObjectWriteLease {

	private readonly IReadOnlyList<string> _lockRoots;
	private readonly string _environment;

	/// <summary>
	/// Creates the lease for the environment the command runs against: next to the deployment locks, and in the temp
	/// folder when that one cannot be used.
	/// </summary>
	/// <param name="settings">The resolved environment; its URL is the lease's environment key.</param>
	public AdministratedObjectWriteLease(EnvironmentSettings settings)
		: this(settings, Path.Combine(System.Environment.GetFolderPath(OperatingSystem.IsWindows()
				? System.Environment.SpecialFolder.CommonApplicationData
				: System.Environment.SpecialFolder.LocalApplicationData), "Creatio", "clio", "administrated-object-locks"),
			Path.Combine(Path.GetTempPath(), "clio-administrated-object-locks")) {
	}

	/// <summary>Creates the lease over <paramref name="lockRoots"/>, tried in order; tests use folders of their own.</summary>
	/// <param name="settings">The resolved environment.</param>
	/// <param name="lockRoots">The directories that may hold the lock files, the preferred one first.</param>
	internal AdministratedObjectWriteLease(EnvironmentSettings settings, params string[] lockRoots) {
		_environment = EnvironmentKey(settings);
		_lockRoots = lockRoots;
	}

	// The environment by its resolved URL, normalised: two names of one site, or one URL written with a trailing slash
	// or in another case, are one environment.
	internal static string EnvironmentKey(EnvironmentSettings settings) {
		string uri = settings?.Uri?.Trim() ?? string.Empty;
		return Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed)
			? parsed.GetLeftPart(UriPartial.Path).TrimEnd('/').ToUpperInvariant()
			: uri.TrimEnd('/').ToUpperInvariant();
	}

	/// <inheritdoc />
	public IDisposable Acquire(string schemaName) {
		string identity = $"{_environment}|{schemaName.Trim().ToUpperInvariant()}";
		string lockKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
		List<string> failures = new();
		foreach (string root in _lockRoots) {
			try {
				Directory.CreateDirectory(root);
				return new FileStream(Path.Combine(root, $"object-{lockKey}.lock"), FileMode.OpenOrCreate,
					FileAccess.Read, FileShare.None);
			}
			catch (IOException exception) when (IsLockContention(exception)) {
				throw new InvalidOperationException($"another call is changing the permissions of '{schemaName}' on this "
					+ "environment (set-object-rights, set-default-record-rights and set-column-rights take one lease per "
					+ "object): calls for one object must not run in parallel. Nothing was changed: re-run it after that "
					+ "call completes.");
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
				// This folder cannot hold the lock (read-only, no profile): the next one is tried.
				failures.Add($"'{root}': {exception.Message}");
			}
		}
		return new UnheldLease(string.Join("; ", failures));
	}

	// Whether the open failed because another handle holds the file: a sharing or lock violation on Windows, the
	// EWOULDBLOCK/EAGAIN the advisory lock .NET takes on Unix answers with (11 on Linux, 35 on macOS). Any other
	// failure — a path that cannot be created, a full disk — is reported as it is, never as another call.
	internal static bool IsLockContention(IOException exception) {
		int code = exception.HResult & 0xFFFF;
		return OperatingSystem.IsWindows()
			? code is 32 or 33
			: exception.HResult is 11 or 35;
	}
}
