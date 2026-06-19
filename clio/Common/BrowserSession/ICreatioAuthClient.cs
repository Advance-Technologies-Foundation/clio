using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Authenticates against Creatio via forms-auth and returns the harvested session cookies as a
/// <see cref="StorageStateResult"/>. Forms-auth (login + password) is the sole cookie-issuance path;
/// OAuth-only or incomplete-credential environments fail closed (there is no OAuth token→cookie
/// exchange for clio's token — confirmed by the ENG-91234 Story-11 spike).
/// </summary>
public interface ICreatioAuthClient {
	/// <summary>
	/// Logs in to <paramref name="env"/> and harvests the session cookies.
	/// </summary>
	/// <param name="env">The target environment (must carry <c>Login</c> + <c>Password</c>).</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The harvested storageState.</returns>
	/// <exception cref="CreatioAuthenticationException">
	/// On invalid/missing credentials, a connectivity failure, or an empty cookie response. The
	/// exception message is sanitized (no secret material), so it is safe to print under <c>--debug</c>.
	/// </exception>
	Task<StorageStateResult> LoginAsync(EnvironmentSettings env, CancellationToken ct = default);
}
