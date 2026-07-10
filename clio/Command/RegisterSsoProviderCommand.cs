using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: RegisterSsoProviderOptions

/// <summary>
///     Options for the <c>register-sso-provider</c> command.
/// </summary>
[Verb("register-sso-provider", Aliases = ["sso-register-provider"],
	HelpText = "Register a new OIDC SSO provider in Creatio")]
public class RegisterSsoProviderOptions : IdentityCommandOptions
{

	[Option("code", Required = true,
		HelpText = "Unique provider code used as the lookup key (must match ^[A-Za-z0-9._-]{1,64}$)")]
	public string Code { get; set; }

	[Option("name", Required = true, HelpText = "Display name for the SSO provider")]
	public string Name { get; set; }

	[Option("issuer-url", Required = true, HelpText = "OIDC issuer / authority URL (must be https://)")]
	public string Url { get; set; }

	[Option("oidc-client-id", Required = true, HelpText = "OAuth client ID registered with the provider")]
	public string OidcClientId { get; set; }

	[Option("oidc-client-secret", Required = false,
		HelpText = "OAuth client secret. Prefer --oidc-client-secret-file or the CLIO_OIDC_CLIENT_SECRET env var to avoid shell-history exposure")]
	public string OidcClientSecret { get; set; }

	[Option("oidc-client-secret-file", Required = false,
		HelpText = "Path to a file containing the OIDC client secret. Mutually exclusive with --oidc-client-secret")]
	public string OidcClientSecretFile { get; set; }

	[Option("discovery-url", Required = false,
		HelpText = "OIDC discovery endpoint (defaults to <url>/.well-known/openid-configuration; must be https://)")]
	public string DiscoveryUrl { get; set; }

	[Option("logout-url", Required = false, HelpText = "End-session endpoint URL for single logout (must be https://)")]
	public string LogoutUrl { get; set; }

}

#endregion

#region Class: RegisterSsoProviderCommand

/// <summary>
///     Registers a new OIDC SSO provider in Creatio by posting to the <c>api/SsoProvider/Register</c>
///     controller. The operation is create-only: a provider whose <c>--code</c> already exists is
///     rejected by the server (surfaced as a non-zero exit with the server error message).
/// </summary>
public class RegisterSsoProviderCommand : RemoteCommand<RegisterSsoProviderOptions>
{

	private const string OidcClientSecretEnvVar = "CLIO_OIDC_CLIENT_SECRET";

	/// <summary>
	///     Initializes a new instance of the <see cref="RegisterSsoProviderCommand" /> class.
	/// </summary>
	public RegisterSsoProviderCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
		: base(applicationClient, settings) { }

	/// <summary>
	///     Relative service path. <see cref="RemoteCommand{T}.ServiceUri" /> prepends
	///     <see cref="RemoteCommand{T}.RootPath" />, which follows the standard Creatio convention:
	///     the <c>0/</c> workspace prefix is added for .NET Framework hosts and omitted for .NET Core
	///     hosts. Set the environment's runtime flag (<c>--IsNetCore</c>) to match the target instance.
	/// </summary>
	protected override string ServicePath => "/api/SsoProvider/Register";

	/// <inheritdoc />
	protected override string GetRequestData(RegisterSsoProviderOptions options) {
		if (!string.IsNullOrEmpty(options.OidcClientSecret) && !string.IsNullOrEmpty(options.OidcClientSecretFile)) {
			throw new InvalidOperationException(
				"--oidc-client-secret and --oidc-client-secret-file are mutually exclusive.");
		}
		string secret = ResolveClientSecret(options);
		// Server contract is camelCase; only non-empty optional fields are sent so the server
		// applies its own defaults (e.g. the standard discovery path) for omitted values.
		Dictionary<string, string> payload = new() {
			["code"] = options.Code,
			["name"] = options.Name,
			["url"] = options.Url,
			["clientId"] = options.OidcClientId
		};
		if (!string.IsNullOrEmpty(secret)) {
			payload["clientSecret"] = secret;
		}
		if (!string.IsNullOrEmpty(options.DiscoveryUrl)) {
			payload["discoveryUrl"] = options.DiscoveryUrl;
		}
		if (!string.IsNullOrEmpty(options.LogoutUrl)) {
			payload["logoutUrl"] = options.LogoutUrl;
		}
		return JsonSerializer.Serialize(payload);
	}

	/// <summary>
	///     Resolves the OIDC client secret in priority order: <c>--oidc-client-secret-file</c> &gt;
	///     <c>--oidc-client-secret</c> &gt; <c>CLIO_OIDC_CLIENT_SECRET</c> environment variable.
	/// </summary>
	private string ResolveClientSecret(RegisterSsoProviderOptions options) {
		if (!string.IsNullOrEmpty(options.OidcClientSecretFile)) {
			if (!File.Exists(options.OidcClientSecretFile)) {
				throw new FileNotFoundException(
					$"--oidc-client-secret-file not found: {options.OidcClientSecretFile}");
			}
			string fileSecret = File.ReadAllText(options.OidcClientSecretFile).Trim();
			if (string.IsNullOrEmpty(fileSecret)) {
				throw new InvalidOperationException(
					$"--oidc-client-secret-file is empty: {options.OidcClientSecretFile}");
			}
			return fileSecret;
		}
		if (!string.IsNullOrEmpty(options.OidcClientSecret)) {
			return options.OidcClientSecret;
		}
		return Environment.GetEnvironmentVariable(OidcClientSecretEnvVar);
	}

	/// <inheritdoc />
	protected override void ProceedResponse(string response, RegisterSsoProviderOptions options) {
		if (string.IsNullOrWhiteSpace(response)) {
			CommandSuccess = false;
			Logger.WriteError("Empty response received from server.");
			Logger.WriteError($"Endpoint: {ServiceUri}");
			return;
		}
		if (IdentityOutput.TryParseError(response, out string error, out string description)) {
			CommandSuccess = false;
			if (options.Format == IdentityOutputFormat.Json) {
				Logger.WriteLine(IdentityOutput.Pretty(response));
			}
			else {
				Logger.WriteError($"{error}: {description}");
			}
			return;
		}
		// A successful registration returns a JSON object describing the provider.
		if (IsJsonObject(response)) {
			if (options.Format == IdentityOutputFormat.Json) {
				Logger.WriteLine(IdentityOutput.Pretty(response));
			}
			else {
				Logger.WriteInfo($"Registered SSO provider: {options.Code}");
			}
			return;
		}
		// Not a JSON object. A bare JSON string (e.g. "SsoProvider with Code 'x' already exists.") is a
		// server-side error/validation message — surface it verbatim. Anything else (an HTML login /
		// redirect page, 404 text) means the request never reached the SsoProvider controller, most
		// often a wrong runtime route (--IsNetCore mismatch) or an unauthenticated session.
		CommandSuccess = false;
		if (TryGetJsonStringMessage(response, out string message)) {
			Logger.WriteError(message);
			return;
		}
		Logger.WriteError("Unexpected non-JSON response; the provider was NOT registered.");
		Logger.WriteError($"Endpoint: {ServiceUri}");
		Logger.WriteError(
			"Verify the environment is reachable, the credentials are valid, and the runtime flag "
			+ "(--IsNetCore) matches the target instance.");
		Logger.WriteError($"Response (first 500 chars): {Truncate(response, 500)}");
	}

	private static bool IsJsonObject(string raw) {
		try {
			using JsonDocument document = JsonDocument.Parse(raw);
			return document.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException) {
			return false;
		}
	}

	private static bool TryGetJsonStringMessage(string raw, out string message) {
		message = null;
		try {
			using JsonDocument document = JsonDocument.Parse(raw);
			if (document.RootElement.ValueKind == JsonValueKind.String) {
				message = document.RootElement.GetString();
				return !string.IsNullOrWhiteSpace(message);
			}
		}
		catch (JsonException) {
			// Not JSON at all; fall through to the generic non-JSON handling.
		}
		return false;
	}

	private static string Truncate(string value, int max) =>
		value.Length <= max ? value : value.Substring(0, max) + "…";

}

#endregion
