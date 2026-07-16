using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml.Linq;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class IisApplicationPoolResolver {
	public static string Resolve(string environmentUri, string? expectedApplicationPoolName = null) {
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException("IIS application-pool resolution requires Windows.");
		}
		if (!Uri.TryCreate(environmentUri, UriKind.Absolute, out Uri? uri)) {
			throw new InvalidOperationException("The registered sandbox URI is not a valid absolute URI.");
		}

		string appCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			"System32", "inetsrv", "appcmd.exe");
		if (!File.Exists(appCmd)) {
			throw new InvalidOperationException($"IIS AppCmd was not found at '{appCmd}'.");
		}

		return Resolve(uri, RunAppCmd(appCmd, "list", "site", "/xml"),
			RunAppCmd(appCmd, "list", "app", "/xml"), expectedApplicationPoolName,
			HostIdentifiesCurrentMachine);
	}

	internal static string Resolve(Uri environmentUri, string sitesXml, string applicationsXml,
		Func<string, bool> hostIdentifiesCurrentMachine) => Resolve(
			environmentUri, sitesXml, applicationsXml, null, hostIdentifiesCurrentMachine);

	internal static string Resolve(Uri environmentUri, string sitesXml, string applicationsXml,
		string? expectedApplicationPoolName, Func<string, bool> hostIdentifiesCurrentMachine) {
		string safeTarget = FormatSafeTarget(environmentUri);
		if (!string.IsNullOrWhiteSpace(environmentUri.UserInfo)) {
			throw new InvalidOperationException(
				$"Registered sandbox URI '{safeTarget}' must not contain user information.");
		}
		if (!hostIdentifiesCurrentMachine(environmentUri.Host)) {
			throw new InvalidOperationException(
				$"Registered sandbox URI '{safeTarget}' does not identify the current machine; " +
				"destructive E2E execution is refused.");
		}

		XElement sitesRoot = XElement.Parse(sitesXml);
		XElement applicationsRoot = XElement.Parse(applicationsXml);
		if (!string.IsNullOrWhiteSpace(expectedApplicationPoolName)) {
			return ResolveExpectedApplicationPool(
				environmentUri, sitesRoot, applicationsRoot, expectedApplicationPoolName, safeTarget);
		}
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
				$"Registered sandbox URI '{safeTarget}' matched {matchingApplications.Length} IIS applications; " +
				"exactly one match is required before destructive E2E execution.");
		}

		string? applicationPoolName = matchingApplications[0].Attribute("APPPOOL.NAME")?.Value;
		if (string.IsNullOrWhiteSpace(applicationPoolName)) {
			throw new InvalidOperationException(
				$"The IIS application matched by '{safeTarget}' has no application-pool name.");
		}
		return applicationPoolName;
	}

	private static string ResolveExpectedApplicationPool(Uri environmentUri, XElement sitesRoot,
		XElement applicationsRoot, string expectedApplicationPoolName, string safeTarget) {
		string targetName = Uri.UnescapeDataString(environmentUri.AbsolutePath)
			.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.LastOrDefault() ?? string.Empty;
		XElement[] assignments = applicationsRoot.Elements("APP")
			.Where(application => string.Equals(application.Attribute("APPPOOL.NAME")?.Value,
				expectedApplicationPoolName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (assignments.Length != 1) {
			throw new InvalidOperationException(
				$"The configured sandbox application pool '{expectedApplicationPoolName}' is assigned to " +
				$"{assignments.Length} IIS applications; exactly one assignment is required before destructive E2E execution.");
		}

		bool routedTargetMatches = string.Equals(
			targetName, expectedApplicationPoolName, StringComparison.OrdinalIgnoreCase);
		bool directIisTargetMatches = SiteMatchesAssignment(
			sitesRoot, assignments[0], environmentUri);
		if (!routedTargetMatches && !directIisTargetMatches) {
			throw new InvalidOperationException(
				$"The configured sandbox application pool '{expectedApplicationPoolName}' does not match " +
				$"the registered URI target '{safeTarget}'.");
		}
		return expectedApplicationPoolName;
	}

	private static bool SiteMatchesAssignment(XElement sitesRoot, XElement application, Uri environmentUri) {
		string siteName = application.Attribute("SITE.NAME")?.Value ?? string.Empty;
		string applicationPath = NormalizeApplicationPath(application.Attribute("path")?.Value ?? string.Empty);
		string targetPath = NormalizeApplicationPath(Uri.UnescapeDataString(environmentUri.AbsolutePath));
		return string.Equals(applicationPath, targetPath, StringComparison.OrdinalIgnoreCase)
			&& sitesRoot.Elements("SITE").Any(site =>
				string.Equals(site.Attribute("SITE.NAME")?.Value, siteName, StringComparison.OrdinalIgnoreCase)
				&& SiteMatches(site, environmentUri));
	}

	internal static bool HostIdentifiesCurrentMachine(string host) {
		if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(host, Dns.GetHostName(), StringComparison.OrdinalIgnoreCase)) {
			return true;
		}

		IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
		if (!string.IsNullOrWhiteSpace(properties.DomainName)
			&& string.Equals(host, $"{properties.HostName}.{properties.DomainName}",
				StringComparison.OrdinalIgnoreCase)) {
			return true;
		}

		HashSet<IPAddress> localAddresses = NetworkInterface.GetAllNetworkInterfaces()
			.SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
			.Select(address => address.Address)
			.ToHashSet();
		IPAddress[] targetAddresses;
		try {
			if (IPAddress.TryParse(host, out IPAddress? literal)) {
				targetAddresses = [literal];
			}
			else {
				targetAddresses = Dns.GetHostAddresses(host);
			}
		}
		catch (SocketException) {
			return false;
		}
		return targetAddresses.Any(address => IPAddress.IsLoopback(address) || localAddresses.Contains(address));
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

	private static string FormatSafeTarget(Uri environmentUri) {
		UriBuilder safeTarget = new(environmentUri.Scheme, environmentUri.Host, environmentUri.Port,
			environmentUri.AbsolutePath) {
			UserName = string.Empty,
			Password = string.Empty,
			Query = string.Empty,
			Fragment = string.Empty
		};
		return safeTarget.Uri.AbsoluteUri.TrimEnd('/');
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
