namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Centralized fail-fast guard for MCP tools (or individual tool branches) that are NOT supported
/// under credential passthrough (FR-04, ENG-93347). Guard-only tools call it FIRST — before any
/// Creatio-reaching work — so an authorized <c>X-Integration-Credentials</c> request gets one
/// uniform, tool-owned rejection instead of a confused-deputy call against a registered
/// environment's stored credentials.
/// </summary>
public interface ICredentialPassthroughToolGuard {

	/// <summary>
	/// Gets a value indicating whether a credential-passthrough context is active for the current
	/// request (an authorized <c>X-Integration-Credentials</c> header was accepted by the
	/// <c>mcp-http</c> middleware). Always <see langword="false"/> on stdio and on HTTP requests
	/// that carried no credential header.
	/// </summary>
	bool IsPassthroughActive { get; }

	/// <summary>
	/// Builds the single, uniform "not supported under credential passthrough" message shared by
	/// every tool-level fail-fast path. The message names the rejected tool and the supported
	/// alternative and never echoes header/credential material.
	/// </summary>
	/// <param name="toolName">The MCP tool name being rejected (e.g. <c>link-from-repository-by-environment</c>).</param>
	/// <param name="alternativeGuidance">The supported alternative to point the caller at (register the environment / use the stdio path / a passthrough-safe branch).</param>
	/// <returns>The uniform rejection message.</returns>
	string BuildUnsupportedMessage(string toolName, string alternativeGuidance);
}

/// <summary>
/// Default <see cref="ICredentialPassthroughToolGuard"/> backed by the ENG-93208 passthrough seam:
/// passthrough is active exactly when <see cref="ICredentialContextAccessor.Current"/> is non-null.
/// In the shared/stdio container the accessor is the null object (never active); the
/// <c>mcp-http</c> host swaps in the real per-request accessor, so the guard fires only on
/// authorized passthrough requests.
/// </summary>
public sealed class CredentialPassthroughToolGuard(ICredentialContextAccessor credentialContextAccessor)
	: ICredentialPassthroughToolGuard {

	/// <inheritdoc />
	public bool IsPassthroughActive => credentialContextAccessor.Current is not null;

	/// <inheritdoc />
	public string BuildUnsupportedMessage(string toolName, string alternativeGuidance) =>
		$"Tool '{toolName}' is not supported under credential passthrough. {alternativeGuidance}";
}
