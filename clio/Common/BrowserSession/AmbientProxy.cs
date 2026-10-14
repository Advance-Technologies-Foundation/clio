using System;
using System.Linq;
using System.Net;

namespace Clio.Common.BrowserSession;

/// <summary>
/// The proxy the standard variables name - <c>HTTPS_PROXY</c> / <c>HTTP_PROXY</c> (either spelling), with
/// <c>NO_PROXY</c> - for the headless-browser download only.
/// </summary>
/// <remarks>
/// In MCP mode <c>Program</c> replaces <c>HttpClient.DefaultProxy</c> with an empty proxy unless
/// <c>CLIO_MCP_RESPECT_AMBIENT_PROXY</c> is set, so calls to Creatio go direct. The browser download is the opposite
/// case: it goes to the internet, and on a network whose only way out is the corporate proxy it can succeed only
/// through it. So its client reads the variables itself; the worker inherits all of them.
/// </remarks>
public sealed class AmbientProxy : IWebProxy {

	private readonly Func<string, string> _readEnvironment;

	/// <summary>Initializes the proxy over the process environment.</summary>
	public AmbientProxy() : this(Environment.GetEnvironmentVariable) {
	}

	/// <summary>Initializes the proxy over an explicit environment, for tests.</summary>
	internal AmbientProxy(Func<string, string> readEnvironment) => _readEnvironment = readEnvironment;

	/// <inheritdoc />
	public ICredentials Credentials { get; set; }

	/// <inheritdoc />
	public Uri GetProxy(Uri destination) {
		string configured = destination.Scheme == Uri.UriSchemeHttps
			? Read("HTTPS_PROXY") ?? Read("HTTP_PROXY")
			: Read("HTTP_PROXY");
		if (configured is null) {
			return null;
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
		if (GetProxy(host) is null) {
			return true;
		}
		string noProxy = Read("NO_PROXY");
		if (noProxy is null) {
			return false;
		}
		return noProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Any(entry => entry == "*" || Matches(host.Host, entry));
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
