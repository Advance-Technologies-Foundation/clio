using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.WebServiceDesigner;

/// <summary>What read-wsdl was asked for.</summary>
/// <param name="EnvironmentName">The environment whose prefix the codes get and, for a URL, which downloads it.</param>
/// <param name="WsdlUrl">The WSDL URL; the stand downloads it. Exclusive with <paramref name="WsdlFiles"/>.</param>
/// <param name="WsdlFiles">Local absolute paths of the WSDL and the schemas it imports.</param>
/// <param name="Port">One port to return; all SOAP ports when null.</param>
/// <param name="ServiceCode">An existing SOAP service the operations are read for; null for a new service.</param>
/// <param name="Operations">The operations to return; all when null.</param>
public sealed record WsdlReadRequest(string EnvironmentName, string WsdlUrl, IReadOnlyList<string> WsdlFiles,
	string Port = null, string ServiceCode = null, IReadOnlyList<string> Operations = null);

/// <summary>One SOAP port of the WSDL as read-wsdl returns it.</summary>
public sealed record WsdlServiceView(
	[property: JsonPropertyName("port")] string Port,

	[property: JsonPropertyName("studio-pick")]
	[property: Description("True for the port Studio's quick setup would create: the SOAP 1.2 one when the WSDL has both")]
	bool StudioPick,

	[property: JsonPropertyName("arguments")]
	[property: Description("register-web-service arguments for this port (add package-name); with service-code, the modify-web-service material")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? Arguments = null,

	[property: JsonPropertyName("existing-methods")]
	[property: Description("With service-code: codes of its methods these operations replace (modify-web-service setMethod)")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> ExistingMethods = null,

	[property: JsonPropertyName("operations")]
	[property: Description("Operation names only, when the result was too large to return whole")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> Operations = null);

/// <summary>The result of read-wsdl.</summary>
public sealed record WsdlReadResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string Error = null,

	[property: JsonPropertyName("services")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<WsdlServiceView> Services = null,

	[property: JsonPropertyName("warnings")]
	[property: Description("What was not read or will need a check: dropped ports, operations and elements, unsupported constructs")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> Warnings = null,

	[property: JsonPropertyName("notices")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> Notices = null);

/// <summary>
/// Turns a WSDL into register-web-service arguments the way Studio's SOAP quick setup builds a service, and saves
/// nothing (ADR D9). A URL is downloaded by the platform's own WSDL endpoint, never by clio.
/// </summary>
public interface IWsdlReader {
	/// <summary>Reads the WSDL and returns one argument object per SOAP port.</summary>
	/// <param name="request">The request.</param>
	/// <returns>The ports with warnings, or operation names only when the result would be too large.</returns>
	WsdlReadResult Read(WsdlReadRequest request);
}

/// <inheritdoc cref="IWsdlReader"/>
public sealed class WsdlReader(
	IWebServiceDesignerClient client,
	ICreatioRightsClient rights,
	IWebServicePrefixReader prefixReader,
	IWebServiceDesigner designer,
	IWsdlParser parser,
	IFileSystem fileSystem) : IWsdlReader {

	/// <summary>The platform endpoint's own limits: 100 files, 10 MB each.</summary>
	internal const int MaxFiles = 100;
	internal const long MaxFileBytes = 10 * 1024 * 1024;
	/// <summary>The most all local files may hold together, the same as a download.</summary>
	internal const long MaxTotalBytes = WebServiceDesignerClient.MaxDownloadedBytes;
	/// <summary>Above this the result lists operation names only; an agent reads it again with operations.</summary>
	internal const int MaxResultCharacters = 120_000;
	/// <summary>The most operation names a too-large result lists per port.</summary>
	internal const int MaxListedOperations = 500;
	/// <summary>The most warnings a result carries; the rest are counted.</summary>
	internal const int MaxWarnings = 100;

	private const string ManageSolutionOperation = "CanManageSolution";

	private static readonly JsonSerializerOptions ArgumentOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <inheritdoc />
	public WsdlReadResult Read(WsdlReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		bool fromUrl = !string.IsNullOrWhiteSpace(request.WsdlUrl);
		bool fromFiles = request.WsdlFiles is { Count: > 0 };
		if (fromUrl == fromFiles) {
			throw new ArgumentException("Give wsdl-url (the stand downloads it) or wsdl-files (local absolute paths), not "
				+ "both and not neither.");
		}
		// The arguments are checked before anything is sent to the environment.
		Uri url = fromUrl ? CheckUrl(request.WsdlUrl) : null;
		if (fromFiles) {
			CheckPaths(request.WsdlFiles);
		}
		if (!rights.GetCanExecuteOperation(ManageSolutionOperation, new CreatioRequestOptions())) {
			throw new UnauthorizedAccessException(
				"The environment user lacks CanManageSolution, which Studio > Web services requires.");
		}
		string prefix = prefixReader.Read();
		IReadOnlyList<WsdlSourceFile> files = fromUrl ? Download(url) : ReadFiles(request.WsdlFiles);
		WsdlDocument document = parser.Parse(files, prefix);
		var warnings = new List<string>();
		if (fromUrl && HasReplacementCharacter(document)) {
			warnings.Add("Some names contain '?': the platform download turns every non-ASCII character into '?', so they "
				+ "may be left out or wrong. Read the WSDL again with wsdl-files (a local copy) to keep them.");
		}
		warnings.AddRange(document.Warnings);
		IReadOnlyList<WsdlPort> ports = SelectOperations(SelectPorts(document, request.Port), request.Operations);
		WsdlTargetService target = ReadTarget(request.ServiceCode);
		var services = new List<WsdlServiceArguments>();
		foreach (WsdlPort port in ports) {
			services.Add(WsdlRegistrationMapper.Map(port, port.Name == document.StudioPort, prefix,
				request.EnvironmentName, target, warnings));
		}
		return Result(services, warnings, Notices(services, target, url));
	}

	private List<string> Notices(IReadOnlyList<WsdlServiceArguments> services, WsdlTargetService target, Uri url) {
		var notices = new List<string>();
		if (target is null) {
			notices.AddRange(services.Select(s => s.Arguments.Code).Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(code => !client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, code))
				.Select(code => $"The code '{code}' is already used by a web service: register under another code, or read "
					+ $"the WSDL again with service-code '{code}' to change that service with modify-web-service."));
		}
		// The WSDL names the endpoint the calls (and any credentials set up later) go to; tell the user when it is not the
		// host the WSDL came from.
		foreach (string host in services
				.Select(s => Uri.TryCreate(s.Arguments.BaseUri, UriKind.Absolute, out Uri endpoint) ? endpoint.Host : null)
				.Where(h => h is not null && url is not null && !string.Equals(h, url.Host, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase)) {
			notices.Add($"The WSDL sends the calls to {WsdlParser.Shown(host)}, not to {url.Host} where it was downloaded "
				+ "from; show the user the base-uri before registering.");
		}
		return notices;
	}

	private static bool HasReplacementCharacter(WsdlDocument document) =>
		document.Ports.Any(p => (p.ServiceName ?? string.Empty).Contains('?')
			|| p.Operations.Any(o => (o.Caption + o.StudioMessageElement).Contains('?')
				|| Flatten(o.Request).Concat(Flatten(o.Response)).Any(x => (x.Caption + x.Path + x.SequenceElement).Contains('?'))));

	private static IEnumerable<WsdlParameter> Flatten(IReadOnlyList<WsdlParameter> parameters) =>
		(parameters ?? []).SelectMany(p => Flatten(p.Items).Prepend(p));

	private static WsdlReadResult Result(IReadOnlyList<WsdlServiceArguments> services, List<string> warnings,
		List<string> notices) {
		List<string> shownWarnings = warnings.Count <= MaxWarnings
			? warnings
			: [.. warnings.Take(MaxWarnings), $"...and {warnings.Count - MaxWarnings} more warnings."];
		List<WsdlServiceView> views = services.Select(s => new WsdlServiceView(s.Port, s.StudioPick,
			JsonSerializer.SerializeToElement(s.Arguments, ArgumentOptions), s.ExistingMethods)).ToList();
		var result = new WsdlReadResult(true, Services: views, Warnings: shownWarnings.Count > 0 ? shownWarnings : null,
			Notices: notices.Count > 0 ? notices : null);
		if (JsonSerializer.Serialize(result).Length <= MaxResultCharacters) {
			return result;
		}
		var summaryNotices = new List<string>(notices) {
			$"The result is too large to return whole (over {MaxResultCharacters} characters); only the operation names are "
			+ "listed. Ask the user which operations they need, then read the WSDL again with operations (and port)."
		};
		return result with {
			Services = services.Select(s => new WsdlServiceView(s.Port, s.StudioPick,
				Operations: s.Arguments.Methods.Select(m => m.Caption).Take(MaxListedOperations)
					.Concat(s.Arguments.Methods.Count > MaxListedOperations
						? [$"...and {s.Arguments.Methods.Count - MaxListedOperations} more"]
						: []).ToList())).ToList(),
			Notices = summaryNotices
		};
	}

	private static Uri CheckUrl(string url) {
		string trimmed = url.Trim();
		if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri parsed)
				|| (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) {
			throw new ArgumentException("wsdl-url must be an absolute http or https URL to the WSDL, such as the address "
				+ "of an .asmx or .svc service followed by ?WSDL.");
		}
		if (!string.IsNullOrEmpty(parsed.UserInfo)) {
			throw new ArgumentException("wsdl-url must not carry credentials (user:password@); download the WSDL yourself "
				+ "and pass it as wsdl-files.");
		}
		// The URL travels to the stand and back in its error messages, so a key or token in it is refused like one in a
		// base URI.
		bool secretInQuery = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => (Pair: pair, At: pair.IndexOf('=')))
			.Any(p => p.At > 0 && p.At < p.Pair.Length - 1
				&& WebServiceSchemaView.IsSecretName(Uri.UnescapeDataString(p.Pair[..p.At])));
		if (secretInQuery) {
			throw new ArgumentException("wsdl-url carries a key or token in its query; download the WSDL yourself and pass "
				+ "it as wsdl-files.");
		}
		return parsed;
	}

	private IReadOnlyList<WsdlSourceFile> Download(Uri url) {
		try {
			return client.DownloadWsdl(url.OriginalString);
		} catch (InvalidOperationException exception) {
			throw new InvalidOperationException(exception.Message + " If the stand cannot reach the URL, or the WSDL imports "
				+ "schemas by a relative schemaLocation (which the platform cannot download), download the files yourself and "
				+ "pass them as wsdl-files.");
		}
	}

	private void CheckPaths(IReadOnlyList<string> paths) {
		if (paths.Count > MaxFiles) {
			throw new ArgumentException($"wsdl-files: at most {MaxFiles} files.");
		}
		foreach (string path in paths) {
			// A UNC path (\\host\share, //host/share) would make the machine running clio open a network connection with
			// the user's credentials.
			if (string.IsNullOrWhiteSpace(path) || !fileSystem.Path.IsPathFullyQualified(path)
					|| path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) {
				throw new ArgumentException($"wsdl-files: '{WsdlParser.Shown(path)}' must be an absolute local path; the MCP "
					+ "server does not run in the user's folder, and network paths are not read.");
			}
		}
	}

	private IReadOnlyList<WsdlSourceFile> ReadFiles(IReadOnlyList<string> paths) {
		var files = new List<WsdlSourceFile>();
		long total = 0;
		foreach (string path in paths) {
			string fullPath = fileSystem.Path.GetFullPath(path);
			byte[] content = ReadFile(fullPath, path);
			total += content.Length;
			if (total > MaxTotalBytes) {
				throw new ArgumentException($"wsdl-files: the files hold more than {MaxTotalBytes / 1024 / 1024} MB together.");
			}
			files.Add(new WsdlSourceFile(fullPath, content));
		}
		return files;
	}

	// One regular file, read through a single handle so the size checked is the size read; a directory, a device, a link
	// or a FIFO is refused before it is opened.
	private byte[] ReadFile(string fullPath, string given) {
		string shown = WsdlParser.Shown(given);
		try {
			if (!fileSystem.File.Exists(fullPath)) {
				throw new ArgumentException($"wsdl-files: '{shown}' does not exist.");
			}
			FileAttributes attributes = fileSystem.File.GetAttributes(fullPath);
			IFileInfo info = fileSystem.FileInfo.New(fullPath);
			if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0
					|| info.LinkTarget is not null) {
				throw new ArgumentException($"wsdl-files: '{shown}' is not a regular file.");
			}
			if (info.Length > MaxFileBytes) {
				throw new ArgumentException($"wsdl-files: '{shown}' is larger than {MaxFileBytes / 1024 / 1024} MB.");
			}
			using FileSystemStream stream = fileSystem.File.Open(fullPath, FileMode.Open, FileAccess.Read,
				FileShare.Read | FileShare.Delete);
			if (stream.Length > MaxFileBytes) {
				throw new ArgumentException($"wsdl-files: '{shown}' is larger than {MaxFileBytes / 1024 / 1024} MB.");
			}
			byte[] content = new byte[stream.Length];
			stream.ReadExactly(content);
			return content;
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException) {
			throw new ArgumentException($"wsdl-files: '{shown}' could not be read.");
		}
	}

	private static IReadOnlyList<WsdlPort> SelectPorts(WsdlDocument document, string port) {
		if (document.Ports.Count == 0) {
			throw new ArgumentException("The WSDL has no SOAP 1.1 or SOAP 1.2 port, so there is nothing to register. "
				+ string.Join(" ", document.Warnings.Take(10)));
		}
		if (string.IsNullOrWhiteSpace(port)) {
			return document.Ports;
		}
		WsdlPort selected = document.Ports.FirstOrDefault(p => string.Equals(p.Name, port.Trim(), StringComparison.Ordinal))
			?? document.Ports.FirstOrDefault(p => string.Equals(p.Name, port.Trim(), StringComparison.OrdinalIgnoreCase));
		return selected is null
			? throw new ArgumentException($"port '{WsdlParser.Shown(port)}' is not a SOAP port of the WSDL; its SOAP ports: "
				+ string.Join(", ", document.Ports.Select(p => p.Name).Take(50)) + ".")
			: [selected];
	}

	// Each port keeps the named operations it has; a port with none of them is left out, and a name no port has is refused.
	private static IReadOnlyList<WsdlPort> SelectOperations(IReadOnlyList<WsdlPort> ports, IReadOnlyList<string> operations) {
		if (operations is not { Count: > 0 }) {
			return ports;
		}
		var wanted = new HashSet<string>(operations.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()),
			StringComparer.OrdinalIgnoreCase);
		string[] unknown = wanted.Where(name => ports.All(p => p.Operations.All(o => !string.Equals(o.Caption, name,
			StringComparison.OrdinalIgnoreCase)))).ToArray();
		if (unknown.Length > 0) {
			throw new ArgumentException($"operations: {string.Join(", ", unknown.Select(WsdlParser.Shown).Take(20))} not found; "
				+ "the operations: " + string.Join(", ", ports.SelectMany(p => p.Operations).Select(o => o.Caption).Distinct()
					.Take(200)) + ".");
		}
		return ports.Select(p => p with { Operations = p.Operations.Where(o => wanted.Contains(o.Caption)).ToList() })
			.Where(p => p.Operations.Count > 0).ToList();
	}

	private WsdlTargetService ReadTarget(string serviceCode) {
		if (string.IsNullOrWhiteSpace(serviceCode)) {
			return null;
		}
		WebServiceView service = designer.Describe(serviceCode.Trim(), false).Service
			?? throw new ArgumentException($"service-code '{serviceCode}' could not be read.");
		bool soap = service.Type == WebServiceSchemaView.TypeName(ServiceSchemaCodes.ServiceTypeSoap11)
			|| service.Type == WebServiceSchemaView.TypeName(ServiceSchemaCodes.ServiceTypeSoap12);
		if (!soap) {
			throw new ArgumentException($"service-code '{serviceCode}' is a {service.Type} service; a WSDL's operations go "
				+ "into a SOAP service.");
		}
		return new WsdlTargetService(service.Code, service.Namespaces ?? [], service.Methods);
	}
}
