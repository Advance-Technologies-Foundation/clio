namespace Clio.Command.McpServer;

/// <summary>
/// Null-object <see cref="ITargetUrlValidator"/> registered as the shared default in
/// <c>BindingsModule.RegisterInto</c> so the credential-passthrough resolver's constructor
/// dependency is always satisfiable — including in the stdio host and the per-environment
/// ephemeral containers, where the real <see cref="TargetUrlValidator"/> (built at HTTP Run
/// time from the bound host + <c>--allowed-base-urls</c> policy) is deliberately absent. The
/// HTTP host registers the real validator after the shared registration, so last-registration-wins
/// gives the real validator in HTTP and this null object everywhere else. It is only ever reached
/// on a passthrough request, and a passthrough request only occurs in the HTTP host — so the
/// no-op <see cref="EnsureAllowed"/> here is never exercised as an egress guard.
/// </summary>
internal sealed class NullTargetUrlValidator : ITargetUrlValidator
{
	/// <inheritdoc />
	public void EnsureAllowed(string url) {
		// No-op: the real egress guard only runs in the HTTP host, which registers TargetUrlValidator.
	}
}
