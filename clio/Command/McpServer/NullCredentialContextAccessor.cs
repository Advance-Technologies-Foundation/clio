namespace Clio.Command.McpServer;

/// <summary>
/// Null-object <see cref="ICredentialContextAccessor"/> registered as the shared default in
/// <c>BindingsModule.RegisterInto</c> so the credential-passthrough resolver's constructor
/// dependency is always satisfiable — including in the stdio host and the per-environment
/// ephemeral containers, where the real <see cref="CredentialContextAccessor"/> (which needs
/// <c>IHttpContextAccessor</c>) is deliberately absent. The HTTP host registers the real
/// accessor after the shared registration, so last-registration-wins gives the real accessor
/// in HTTP and this null object everywhere else. Its <see cref="Current"/> getter always returns
/// <see langword="null"/>, which the resolver reads as "no passthrough request" (existing path).
/// </summary>
internal sealed class NullCredentialContextAccessor : ICredentialContextAccessor
{
	/// <inheritdoc />
	public CredentialContext Current {
		get => null;
		set {
			// No-op: there is no per-request store outside an HTTP request.
		}
	}
}
