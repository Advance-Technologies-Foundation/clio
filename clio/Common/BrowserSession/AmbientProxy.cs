using System;
using System.Linq;
using System.Net;

namespace Clio.Common.BrowserSession;

/// <summary>
/// The proxy for the headless-browser download only: the one the standard variables name - <c>HTTPS_PROXY</c> /
/// <c>HTTP_PROXY</c> (either spelling) - and otherwise the system proxy (Windows / macOS settings, PAC included), with
/// <c>NO_PROXY</c> honoured either way.
/// </summary>
/// <remarks>
/// In MCP mode <c>Program</c> replaces <c>HttpClient.DefaultProxy</c> with an empty proxy unless
/// <c>CLIO_MCP_RESPECT_AMBIENT_PROXY</c> is set, so calls to Creatio go direct. The browser download is the opposite
/// case: it goes to the internet, and on a network whose only way out is the corporate proxy it can succeed only
/// through it. So its client reads the variables itself; the worker inherits all of them.
/// </remarks>
public sealed class AmbientProxy : IWebProxy {

	private readonly Func<string, string> _readEnvironment;
	private readonly IWebProxy _systemProxy;

	/// <summary>Initializes the proxy over the process environment and the system proxy settings.</summary>
	public AmbientProxy() : this(Environment.GetEnvironmentVariable, SystemProxy()) {
	}

	/// <summary>Initializes the proxy over an explicit environment and system proxy, for tests.</summary>
	internal AmbientProxy(Func<string, string> readEnvironment, IWebProxy systemProxy) {
		_readEnvironment = readEnvironment;
		_systemProxy = systemProxy;
	}

	// The proxy the OS is configured with (WinHTTP / PAC on Windows, System Configuration on macOS). Read directly
	// because HttpClient.DefaultProxy - which would return it - is the thing MCP mode clears.
#pragma warning disable SYSLIB0014 // GetSystemWebProxy is the only public API that returns the OS proxy settings.
	private static IWebProxy SystemProxy() => WebRequest.GetSystemWebProxy();
#pragma warning restore SYSLIB0014

	/// <summary>
	/// Credentials for a proxy, looked up by the proxy's own address each time: only the user:password written into
	/// HTTPS_PROXY / HTTP_PROXY, and only for the proxy that URL names. A system or PAC/WPAD-chosen proxy gets none -
	/// the user's Windows (NTLM/Kerberos) credentials are never offered to a proxy an auto-detect could have picked. The
	/// setter refuses a value: credentials come from the variables, never from instance state another proxy could inherit.
	/// </summary>
	public ICredentials Credentials {
		get => new ConfiguredProxyCredentials(this);
		set => throw new NotSupportedException(
			"Proxy credentials come only from the user:password in HTTPS_PROXY / HTTP_PROXY, for that proxy alone.");
	}

	// Resolves a credential for exactly one proxy URI (scheme, host and port) from the variables as they are now.
	private sealed class ConfiguredProxyCredentials(AmbientProxy owner) : ICredentials {
		public NetworkCredential GetCredential(Uri uri, string authType) => owner.CredentialFor(uri);
	}

	internal NetworkCredential CredentialFor(Uri proxyAddress) {
		foreach (string name in new[] { "HTTPS_PROXY", "HTTP_PROXY" }) {
			Uri configured = Parse(Read(name));
			if (configured is not null && !string.IsNullOrEmpty(configured.UserInfo)
				&& string.Equals(configured.Scheme, proxyAddress.Scheme, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(configured.Host, proxyAddress.Host, StringComparison.OrdinalIgnoreCase)
				&& configured.Port == proxyAddress.Port) {
				string[] parts = configured.UserInfo.Split(':', 2);
				return new NetworkCredential(Uri.UnescapeDataString(parts[0]),
					parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
			}
		}
		return null;
	}

	private static Uri Parse(string configured) {
		if (configured is null) {
			return null;
		}
		if (!configured.Contains("://", StringComparison.Ordinal)) {
			configured = "http://" + configured;
		}
		return Uri.TryCreate(configured, UriKind.Absolute, out Uri proxy) ? proxy : null;
	}

	/// <inheritdoc />
	public Uri GetProxy(Uri destination) {
		string configured = Configured(destination);
		return configured is null ? SystemProxyFor(destination) : Parse(configured);
	}

	/// <inheritdoc />
	public bool IsBypassed(Uri host) {
		string noProxy = Read("NO_PROXY");
		if (noProxy is not null && noProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Any(entry => entry == "*" || Matches(host.Host, entry))) {
			return true;
		}
		return GetProxy(host) is null;
	}

	private string Configured(Uri destination) =>
		destination.Scheme == Uri.UriSchemeHttps ? Read("HTTPS_PROXY") ?? Read("HTTP_PROXY") : Read("HTTP_PROXY");

	// The system proxy answers the destination itself when it would go direct.
	private Uri SystemProxyFor(Uri destination) {
		if (_systemProxy is null || _systemProxy.IsBypassed(destination)) {
			return null;
		}
		Uri proxy = _systemProxy.GetProxy(destination);
		if (proxy is null || proxy == destination) {
			return null;
		}
		return proxy;
	}

	// NO_PROXY entries match the host itself or any subdomain: "corp.example" and ".corp.example" both cover
	// "mirror.corp.example".
	private static bool Matches(string host, string entry) {
		string domain = entry.TrimStart('.');
		return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
			|| host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
	}

	private string Read(string name) {
		string value = _readEnvironment(name);
		if (string.IsNullOrWhiteSpace(value)) {
			value = _readEnvironment(name.ToLowerInvariant());
		}
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
