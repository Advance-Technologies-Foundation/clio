using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class IisApplicationPoolResolver {
	public static string Resolve(string environmentUri) {
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException("IIS application-pool resolution requires Windows.");
		}
		if (!Uri.TryCreate(environmentUri, UriKind.Absolute, out Uri? uri)) {
			throw new InvalidOperationException(
				$"Registered sandbox URI '{environmentUri}' is not a valid absolute URI.");
		}

		string appCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			"System32", "inetsrv", "appcmd.exe");
		if (!File.Exists(appCmd)) {
			throw new InvalidOperationException($"IIS AppCmd was not found at '{appCmd}'.");
		}

		return Resolve(uri, RunAppCmd(appCmd, "list", "site", "/xml"),
			RunAppCmd(appCmd, "list", "app", "/xml"));
	}

	internal static string Resolve(Uri environmentUri, string sitesXml, string applicationsXml) {
		XElement sitesRoot = XElement.Parse(sitesXml);
		XElement applicationsRoot = XElement.Parse(applicationsXml);
		HashSet<string> matchingSites = sitesRoot.Elements("SITE")
			.Where(site => SiteMatches(site, environmentUri))
			.Select(site => site.Attribute("SITE.NAME")?.Value)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Cast<string>()
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string applicationPath = NormalizeApplicationPath(Uri.UnescapeDataString(environmentUri.AbsolutePath));
		XElement[] matchingApplications = applicationsRoot.Elements("APP")
			.Where(application => matchingSites.Contains(application.Attribute("SITE.NAME")?.Value ?? string.Empty))
			.Where(application => string.Equals(
				NormalizeApplicationPath(application.Attribute("path")?.Value ?? string.Empty),
				applicationPath, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		if (matchingApplications.Length != 1) {
			throw new InvalidOperationException(
				$"Registered sandbox URI '{environmentUri}' matched {matchingApplications.Length} IIS applications; " +
				"exactly one match is required before destructive E2E execution.");
		}

		string? applicationPoolName = matchingApplications[0].Attribute("APPPOOL.NAME")?.Value;
		if (string.IsNullOrWhiteSpace(applicationPoolName)) {
			throw new InvalidOperationException(
				$"The IIS application matched by '{environmentUri}' has no application-pool name.");
		}
		return applicationPoolName;
	}

	private static bool SiteMatches(XElement site, Uri environmentUri) {
		string? siteName = site.Attribute("SITE.NAME")?.Value;
		string? bindings = site.Attribute("bindings")?.Value;
		return !string.IsNullOrWhiteSpace(siteName)
			&& !string.IsNullOrWhiteSpace(bindings)
			&& bindings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Any(binding => BindingMatches(binding, environmentUri));
	}

	private static bool BindingMatches(string binding, Uri environmentUri) {
		int protocolSeparator = binding.IndexOf('/');
		if (protocolSeparator <= 0 || !string.Equals(binding[..protocolSeparator], environmentUri.Scheme,
				StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		string bindingInformation = binding[(protocolSeparator + 1)..];
		int hostSeparator = bindingInformation.LastIndexOf(':');
		int portSeparator = hostSeparator > 0
			? bindingInformation.LastIndexOf(':', hostSeparator - 1)
			: -1;
		if (portSeparator < 0 || hostSeparator < 0
			|| !int.TryParse(bindingInformation[(portSeparator + 1)..hostSeparator],
				NumberStyles.None, CultureInfo.InvariantCulture, out int port)
			|| port != environmentUri.Port) {
			return false;
		}

		string boundAddress = bindingInformation[..portSeparator].Trim('[', ']');
		string hostHeader = bindingInformation[(hostSeparator + 1)..];
		bool addressMatches = boundAddress is "*" or "0.0.0.0" or "::"
			|| IPAddress.TryParse(boundAddress, out IPAddress? boundIp)
			&& IPAddress.TryParse(environmentUri.Host, out IPAddress? environmentIp)
			&& boundIp.Equals(environmentIp);
		if (!string.IsNullOrWhiteSpace(hostHeader)) {
			return addressMatches
				&& string.Equals(hostHeader, environmentUri.Host, StringComparison.OrdinalIgnoreCase);
		}
		return addressMatches;
	}

	private static string NormalizeApplicationPath(string path) {
		string normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
		if (!normalized.StartsWith('/')) {
			normalized = "/" + normalized;
		}
		return normalized.Length == 1 ? normalized : normalized.TrimEnd('/');
	}

	private static string RunAppCmd(string appCmd, params string[] arguments) {
		ProcessStartInfo startInfo = new(appCmd) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		foreach (string argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start IIS appcmd.exe.");
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0) {
			throw new InvalidOperationException(
				$"appcmd.exe failed with exit code {process.ExitCode}: {error}");
		}
		return output;
	}
}
