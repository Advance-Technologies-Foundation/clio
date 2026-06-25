using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
///     Provides canonical AI-facing guidance for using Creatio server-to-server OAuth credentials.
/// </summary>
[McpServerResourceType]
public sealed class ServerToServerOAuthGuidanceResource {

	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/server-to-server-oauth";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	///     Canonical guidance article for exchanging server-to-server OAuth credentials for bearer tokens.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "server-to-server-oauth-guidance")]
	[Description("Returns canonical MCP guidance for using Creatio server-to-server OAuth client IDs " +
		"and secrets: token minting, expiry handling without refresh tokens, and bearer-authenticated requests.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
			# clio MCP server-to-server OAuth guide

			Use this guide after a Creatio OAuth app has been created and you have received a client ID
			and client secret for the `client_credentials` grant. The outside caller uses these
			credentials to mint short-lived bearer tokens from IdentityService, then sends those tokens
			to Creatio APIs.

			## Agent workflow for setting up the credentials

			1. `get-identity-service-config` - discover the Creatio base URL and IdentityService token
			   endpoint.
			2. `resolve-oauth-system-user` - find the existing Creatio user that the OAuth app should
			   execute as, unless the caller explicitly asks to create a new technical user.
			3. `create-server-to-server-oauth-app` - create the OAuth app and capture the one-time
			   `clientId` and `clientSecret` from the structured result.
			4. `verify-oauth-app` - mint a token and run the bearer-authenticated smoke request.

			Never paste the client secret or access token into chat rooms, issue comments, logs, docs, or
			planned payloads. Store the client secret in the user's secret manager or environment-specific
			configuration. If the secret is lost, rotate or recreate the OAuth app.

			## Mint an access token

			Send a form-url-encoded POST request to the IdentityService token endpoint:

			```http
			POST {identityServiceUrl}/connect/token
			Content-Type: application/x-www-form-urlencoded

			grant_type=client_credentials&client_id={clientId}&client_secret={clientSecret}
			```

			curl example:

			```bash
			curl -sS -X POST "{identityServiceUrl}/connect/token" \
			  -H "Content-Type: application/x-www-form-urlencoded" \
			  --data-urlencode "grant_type=client_credentials" \
			  --data-urlencode "client_id=${CLIENT_ID}" \
			  --data-urlencode "client_secret=${CLIENT_SECRET}"
			```

			PowerShell example:

			```powershell
			$tokenResponse = Invoke-RestMethod `
			  -Method Post `
			  -Uri "$identityServiceUrl/connect/token" `
			  -ContentType "application/x-www-form-urlencoded" `
			  -Body @{
			    grant_type = "client_credentials"
			    client_id = $env:CLIENT_ID
			    client_secret = $env:CLIENT_SECRET
			  }
			$accessToken = $tokenResponse.access_token
			```

			A successful response includes `access_token`, `token_type` (`Bearer`), and `expires_in`
			(seconds). Treat the token as a secret.

			## Expiry and refresh behavior

			The server-to-server `client_credentials` flow does not use refresh tokens in this Creatio
			setup. Do not ask for or store `refresh_token`. When the token expires, or when a Creatio
			request returns 401 Unauthorized because the token is no longer valid, mint a new token by
			repeating the same `/connect/token` request with the client ID and client secret.

			## Call a Creatio API with the bearer token

			Send the token in the `Authorization: Bearer` header. The exact Creatio route prefix depends
			on the environment; classic .NET Framework environments commonly require the `/0/` prefix.
			Use the URL reported by clio tools when available. This example reads one Contact row through
			DataService:

			```http
			POST {creatioUrl}/0/DataService/json/SyncReply/SelectQuery
			Authorization: Bearer {accessToken}
			Content-Type: application/json

			{
			  "rootSchemaName": "Contact",
			  "rowCount": 1,
			  "columns": {
			    "items": {
			      "Id": { "expression": { "expressionType": 0, "columnPath": "Id" } },
			      "Name": { "expression": { "expressionType": 0, "columnPath": "Name" } }
			    }
			  }
			}
			```

			curl example:

			```bash
			curl -sS -X POST "{creatioUrl}/0/DataService/json/SyncReply/SelectQuery" \
			  -H "Authorization: Bearer ${ACCESS_TOKEN}" \
			  -H "Content-Type: application/json" \
			  -d '{"rootSchemaName":"Contact","rowCount":1,"columns":{"items":{"Id":{"expression":{"expressionType":0,"columnPath":"Id"}},"Name":{"expression":{"expressionType":0,"columnPath":"Name"}}}}}'
			```

			If the request succeeds, the OAuth app is usable by external server-to-server integrations.
			If token minting works but Creatio returns 403 Forbidden, the OAuth app's system user is
			authenticated but lacks permissions for the requested object or operation.
			"""
	};

}
