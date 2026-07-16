using System;

namespace Clio.Common;

/// <summary>
/// <see cref="IReauthExecutor"/> that runs the wrapped call exactly once and never
/// re-authenticates. Used by the credential-passthrough auth path (bearer token).
/// </summary>
/// <remarks>
/// The authoritative contract documentation lives on <see cref="IReauthExecutor"/>.
/// Passthrough clients are built from opaque bearer material (FR-18): there are no
/// login/password credentials to re-login with, so an unauthorized response cannot be
/// recovered here. The <c>isUnauthorized</c> predicate is intentionally ignored — the
/// call is executed once and its result returned verbatim.
/// </remarks>
internal sealed class NoReauthExecutor : IReauthExecutor {

	/// <inheritdoc />
	public T Execute<T>(Func<T> call, Func<T, bool> isUnauthorized) => call();
}
