using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
///     Provides canonical AI-facing guidance for the Creatio identity-assertion / Identity Service V3
///     token-exchange flow used by the embedded AI chat.
/// </summary>
[McpServerResourceType]
public sealed class IdentityAssertionGuidanceResource {

	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/identity-assertion";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	///     Canonical guidance article for onboarding and verifying the identity-assertion token-exchange flow.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "identity-assertion-guidance")]
	[Description("Returns canonical MCP guidance for the Creatio identity-assertion / Identity Service V3 " +
		"token-exchange flow: onboarding sequence, prerequisites, the four clio tools, and troubleshooting.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
			# Identity assertion & token exchange via AI chat (Identity Service V3)

			Use this guidance to set up and verify the Creatio identity-assertion flow that lets an
			embedded AI chat identify a Creatio user to the Agentic Platform through an Identity
			Service V3 token exchange — without a separate login.

			## How identity flows (conceptual)

			1. Keys. Each Creatio instance owns an asymmetric signing key pair. The PRIVATE key never
			   leaves the instance; the PUBLIC key is registered once with Identity Service V3 at onboarding.
			2. Assertion. When a user opens the embedded chat, Creatio mints a short-lived signed JWT
			   for the currently authenticated user.
			3. Exchange. The chat backend presents the assertion to Identity Service V3, which verifies
			   the signature with the registered public key.
			4. Credentials. On success, V3 issues user-scoped credentials to the chat backend.

			Only Creatio can mint valid assertions; V3 verifies them independently without calling back.

			## Prerequisites on the environment

			- The Creatio feature `EnableIdentityAssertionIssuer` MUST be enabled for the three
			  identityAssertion endpoints. While it is off they return 403 Forbidden.
			- `get-identity-public-jwk` and `regenerate-identity-signing-key` additionally require the
			  current user to hold the `CanManageIdentityAssertionIssuer` operation permission.
			- `check-auth-code-flow` does NOT depend on the feature.

			## clio tools (each takes environmentName + optional format: text|json)

			- get-identity-assertion        -> POST identityAssertion/currentUser. Issues the short-lived
			  signed JWT for the current user (text = the token; json = full payload with expiry/issuer/audience).
			- get-identity-public-jwk       -> GET  identityAssertion/publicJwk. Exports the instance public
			  key as a JWK to register with Identity Service V3 (text = compact JWK; json = indented).
			- regenerate-identity-signing-key -> POST identityAssertion/regenerateSigningKey. DESTRUCTIVE:
			  invalidates assertions signed with the previous key; the new public key must be re-registered with V3.
			- check-auth-code-flow          -> GET  identityServiceInfo/canUseAuthorizationCodeFlow. Diagnostic
			  boolean for whether the OAuth authorization code flow is available.

			## Onboarding sequence

			1. (Optional) regenerate-identity-signing-key — establish a fresh key pair (destructive).
			2. get-identity-public-jwk (format=json) — export the public JWK; register it with Identity Service V3.
			3. check-auth-code-flow — confirm the environment can use the authorization code flow.

			## Verifying the flow

			4. get-identity-assertion — issue an assertion for the current user (what the frontend obtains).
			5. The chat backend exchanges the assertion at Identity Service V3 for user-scoped credentials
			   (performed outside clio).

			A healthy check: check-auth-code-flow prints true; get-identity-public-jwk returns a stable JWK;
			get-identity-assertion returns a JWT whose signature validates against that JWK, with a short
			expiresIn and the expected issuer/audience.

			## Troubleshooting

			- 403 "endpoint is disabled" -> EnableIdentityAssertionIssuer is off; enable the feature.
			- 403 "AccessDenied" on jwk/regenerate -> grant CanManageIdentityAssertionIssuer to the user.
			- 401 on get-identity-assertion -> user not authenticated / session expired; re-check credentials.
			- 400 "UserEmailMissing" -> the current user has no email; set it in Creatio.
			- Assertion fails to validate in V3 -> public key was not re-registered after a key regeneration;
			  re-run get-identity-public-jwk and register the current JWK.
			"""
	};

}
