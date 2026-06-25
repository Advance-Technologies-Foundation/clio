using System;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// How an IdentityService base URL was determined for an environment.
/// </summary>
public enum IdentityServerUrlSource
{
	/// <summary>No IdentityService URL could be determined.</summary>
	None,

	/// <summary>Read from the <c>OAuth20IdentityServerUrl</c> Creatio system setting.</summary>
	Setting,

	/// <summary>Derived from the Creatio host by inserting the <c>-is</c> suffix.</summary>
	Derived
}

/// <summary>
/// Resolves the IdentityService base URL for a Creatio host and computes the well-known OAuth endpoints.
/// Pure, side-effect-free logic so it can be unit tested without any environment or network.
/// </summary>
public interface IIdentityServerUrlResolver
{
	/// <summary>
	/// Derives an IdentityService base URL from a Creatio base URL by inserting the <c>-is</c> suffix into
	/// the first host label (e.g. <c>186843-crm-bundle.creatio.com</c> → <c>186843-crm-bundle-is.creatio.com</c>),
	/// preserving scheme and port. Returns an empty string when the input is not an absolute URL.
	/// </summary>
	/// <param name="creatioBaseUrl">Creatio base URL.</param>
	/// <returns>The derived IdentityService base URL, or an empty string.</returns>
	string DeriveIdentityServerUrl(string creatioBaseUrl);

	/// <summary>
	/// Computes the token endpoint (<c>{base}/connect/token</c>) for an IdentityService base URL.
	/// </summary>
	/// <param name="identityServerBaseUrl">IdentityService base URL.</param>
	/// <returns>The token endpoint, or an empty string when the base URL is empty.</returns>
	string GetTokenEndpoint(string identityServerBaseUrl);

	/// <summary>
	/// Computes the discovery endpoint (<c>{base}/.well-known/openid-configuration</c>) for an
	/// IdentityService base URL.
	/// </summary>
	/// <param name="identityServerBaseUrl">IdentityService base URL.</param>
	/// <returns>The discovery endpoint, or an empty string when the base URL is empty.</returns>
	string GetDiscoveryEndpoint(string identityServerBaseUrl);
}

/// <inheritdoc />
public sealed class IdentityServerUrlResolver : IIdentityServerUrlResolver
{
	private const string IdentitySuffix = "-is";

	/// <inheritdoc />
	public string DeriveIdentityServerUrl(string creatioBaseUrl) {
		if (string.IsNullOrWhiteSpace(creatioBaseUrl)
			|| !Uri.TryCreate(creatioBaseUrl, UriKind.Absolute, out Uri uri)) {
			return string.Empty;
		}
		string host = uri.Host;
		int firstDot = host.IndexOf('.');
		string derivedHost = firstDot < 0
			? $"{host}{IdentitySuffix}"
			: $"{host[..firstDot]}{IdentitySuffix}{host[firstDot..]}";
		UriBuilder builder = new(uri.Scheme, derivedHost) {
			Path = string.Empty
		};
		if (!uri.IsDefaultPort) {
			builder.Port = uri.Port;
		}
		return builder.Uri.ToString().TrimEnd('/');
	}

	/// <inheritdoc />
	public string GetTokenEndpoint(string identityServerBaseUrl) =>
		string.IsNullOrWhiteSpace(identityServerBaseUrl)
			? string.Empty
			: $"{identityServerBaseUrl.TrimEnd('/')}/connect/token";

	/// <inheritdoc />
	public string GetDiscoveryEndpoint(string identityServerBaseUrl) =>
		string.IsNullOrWhiteSpace(identityServerBaseUrl)
			? string.Empty
			: $"{identityServerBaseUrl.TrimEnd('/')}/.well-known/openid-configuration";
}
