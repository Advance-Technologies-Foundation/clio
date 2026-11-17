using System;
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
	/// <exception cref="InvalidOperationException">Another call holds the lease, or the lock directory cannot be
	/// used; the message says which.</exception>
	IDisposable Acquire(string schemaName);
}

/// <inheritdoc />
public sealed class AdministratedObjectWriteLease : IAdministratedObjectWriteLease {

	private readonly string _lockRoot;
	private readonly string _environment;

	/// <summary>Creates the lease for the environment the command runs against, next to the deployment locks.</summary>
	/// <param name="settings">The resolved environment; its URL is the lease's environment key.</param>
	public AdministratedObjectWriteLease(EnvironmentSettings settings)
		: this(settings, Path.Combine(System.Environment.GetFolderPath(OperatingSystem.IsWindows()
			? System.Environment.SpecialFolder.CommonApplicationData
			: System.Environment.SpecialFolder.LocalApplicationData), "Creatio", "clio", "administrated-object-locks")) {
	}

	/// <summary>Creates the lease over <paramref name="lockRoot"/>; tests use a folder of their own.</summary>
	/// <param name="settings">The resolved environment.</param>
	/// <param name="lockRoot">The directory that holds the lock files.</param>
	internal AdministratedObjectWriteLease(EnvironmentSettings settings, string lockRoot) {
		_environment = EnvironmentKey(settings);
		_lockRoot = lockRoot;
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
		try {
			Directory.CreateDirectory(_lockRoot);
			return new FileStream(Path.Combine(_lockRoot, $"object-{lockKey}.lock"), FileMode.OpenOrCreate,
				FileAccess.Read, FileShare.None);
		}
		catch (IOException exception) when (!IsLockContention(exception)) {
			throw new InvalidOperationException($"clio cannot take the permissions lock of '{schemaName}' in "
				+ $"'{_lockRoot}': {exception.Message} Nothing was changed.", exception);
		}
		catch (IOException) {
			throw new InvalidOperationException($"another call is changing the permissions of '{schemaName}' on this "
				+ "environment (set-object-rights, set-default-record-rights and set-column-rights take one lease per "
				+ "object): calls for one object must not run in parallel. Nothing was changed: re-run it after that call "
				+ "completes.");
		}
		catch (UnauthorizedAccessException exception) {
			throw new InvalidOperationException("clio cannot use its permissions lock directory "
				+ $"'{_lockRoot}'; nothing was changed.", exception);
		}
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
