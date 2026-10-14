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

	/// <inheritdoc />
	public ICredentials Credentials { get; set; }

	/// <inheritdoc />
	public Uri GetProxy(Uri destination) {
		string configured = Configured(destination);
		if (configured is null) {
			return SystemProxyFor(destination);
		}
		if (!configured.Contains("://", StringComparison.Ordinal)) {
			configured = "http://" + configured;
		}
		if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri proxy)) {
			return null;
		}
		if (!string.IsNullOrEmpty(proxy.UserInfo)) {
			string[] parts = proxy.UserInfo.Split(':', 2);
			Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]),
				parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
		}
		return proxy;
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
		Credentials ??= CredentialCache.DefaultCredentials;
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
