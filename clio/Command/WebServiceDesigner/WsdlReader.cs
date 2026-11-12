using System;
using System.Collections.Generic;
using System.ComponentModel;
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
	[property: Description("register-web-service arguments for this port; add package-name")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? Arguments = null,

	[property: JsonPropertyName("existing-methods")]
	[property: Description("With service-code: codes of its methods these operations replace (modify-web-service setMethod)")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> ExistingMethods = null,

	[property: JsonPropertyName("operations")]
	[property: Description("Operation names only, when the WSDL was too large to return whole")]
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
	/// <returns>The ports with warnings, or a summary when the result would be too large.</returns>
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
	/// <summary>Above this the result lists operation names only; an agent reads it again with operations.</summary>
	internal const int MaxResultCharacters = 120_000;

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
		if (!rights.GetCanExecuteOperation("CanManageSolution", new CreatioRequestOptions())) {
			throw new UnauthorizedAccessException(
				"The environment user lacks CanManageSolution, which Studio > Web services requires.");
		}
		string prefix = prefixReader.Read();
		IReadOnlyList<WsdlSourceFile> files = fromUrl ? Download(request.WsdlUrl) : ReadFiles(request.WsdlFiles);
		WsdlDocument document = parser.Parse(files, prefix);
		var warnings = document.Warnings.ToList();
		IReadOnlyList<WsdlPort> ports = SelectPorts(document, request.Port);
		WsdlTargetService target = ReadTarget(request.ServiceCode);
		var services = new List<WsdlServiceArguments>();
		foreach (WsdlPort port in ports) {
			WsdlPort selected = SelectOperations(port, request.Operations);
			services.Add(WsdlRegistrationMapper.Map(selected, port.Name == document.StudioPort, prefix,
				request.EnvironmentName, target, warnings));
		}
		if (fromUrl && services.Any(HasReplacementCharacter)) {
			warnings.Add("Some names or captions contain '?': the platform download turns every non-ASCII character into "
				+ "'?'. Read the WSDL again with wsdl-files (a local copy) to keep them.");
		}
		var notices = new List<string>();
		if (target is null) {
			notices.AddRange(services.Select(s => s.Arguments.Code).Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(code => !client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, code))
				.Select(code => $"The code '{code}' is already used by a web service: register under another code, or read "
					+ $"the WSDL again with service-code '{code}' to change that service with modify-web-service."));
		}
		return Result(services, warnings, notices, request.Operations is { Count: > 0 });
	}

	// Names and captions only: a base URI or a SOAP action may hold a real '?'.
	private static bool HasReplacementCharacter(WsdlServiceArguments service) =>
		(service.Arguments.Caption ?? string.Empty).Contains('?')
		|| service.Arguments.Methods.Any(m => (m.Caption + m.MessageElement).Contains('?')
			|| Flatten(m.RequestParameters).Concat(Flatten(m.ResponseParameters))
				.Any(p => (p.Caption + p.Path + p.SequenceElement).Contains('?')));

	private static IEnumerable<WebServiceParameterArg> Flatten(IReadOnlyList<WebServiceParameterArg> parameters) =>
		(parameters ?? []).SelectMany(p => Flatten(p.Items).Prepend(p));

	private static WsdlReadResult Result(IReadOnlyList<WsdlServiceArguments> services, List<string> warnings,
		List<string> notices, bool filtered) {
		List<WsdlServiceView> views = services.Select(s => new WsdlServiceView(s.Port, s.StudioPick,
			JsonSerializer.SerializeToElement(s.Arguments, ArgumentOptions), s.ExistingMethods)).ToList();
		var result = new WsdlReadResult(true, Services: views, Warnings: warnings.Count > 0 ? warnings : null,
			Notices: notices.Count > 0 ? notices : null);
		if (filtered || JsonSerializer.Serialize(result).Length <= MaxResultCharacters) {
			return result;
		}
		notices.Add($"The WSDL is too large to return whole (over {MaxResultCharacters} characters); only the operation "
			+ "names are listed. Ask the user which operations they need, then read it again with operations (and port).");
		return result with {
			Services = services.Select(s => new WsdlServiceView(s.Port, s.StudioPick,
				Operations: s.Arguments.Methods.Select(m => m.Caption).ToList())).ToList(),
			Notices = notices
		};
	}

	private IReadOnlyList<WsdlSourceFile> Download(string url) {
		string trimmed = url.Trim();
		if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri parsed)
				|| (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) {
			throw new ArgumentException("wsdl-url must be an absolute http or https URL, e.g. "
				+ "http://www.dneonline.com/calculator.asmx?WSDL.");
		}
		if (!string.IsNullOrEmpty(parsed.UserInfo)) {
			throw new ArgumentException("wsdl-url must not carry credentials (user:password@); download the WSDL yourself "
				+ "and pass it as wsdl-files.");
		}
		return client.DownloadWsdl(trimmed);
	}

	private IReadOnlyList<WsdlSourceFile> ReadFiles(IReadOnlyList<string> paths) {
		if (paths.Count > MaxFiles) {
			throw new ArgumentException($"wsdl-files: at most {MaxFiles} files.");
		}
		var files = new List<WsdlSourceFile>();
		foreach (string path in paths) {
			if (string.IsNullOrWhiteSpace(path) || !fileSystem.Path.IsPathFullyQualified(path)) {
				throw new ArgumentException($"wsdl-files: '{path}' must be an absolute path; the MCP server does not run in "
					+ "the user's folder.");
			}
			if (!fileSystem.File.Exists(path)) {
				throw new ArgumentException($"wsdl-files: '{path}' does not exist.");
			}
			if (fileSystem.FileInfo.New(path).Length > MaxFileBytes) {
				throw new ArgumentException($"wsdl-files: '{path}' is larger than {MaxFileBytes / 1024 / 1024} MB.");
			}
			files.Add(new WsdlSourceFile(fileSystem.Path.GetFullPath(path), fileSystem.File.ReadAllBytes(path)));
		}
		return files;
	}

	private static IReadOnlyList<WsdlPort> SelectPorts(WsdlDocument document, string port) {
		if (document.Ports.Count == 0) {
			throw new ArgumentException("The WSDL has no SOAP 1.1 or SOAP 1.2 port, so there is nothing to register. "
				+ string.Join(" ", document.Warnings));
		}
		if (string.IsNullOrWhiteSpace(port)) {
			return document.Ports;
		}
		WsdlPort selected = document.Ports.FirstOrDefault(p => string.Equals(p.Name, port.Trim(), StringComparison.Ordinal))
			?? document.Ports.FirstOrDefault(p => string.Equals(p.Name, port.Trim(), StringComparison.OrdinalIgnoreCase));
		return selected is null
			? throw new ArgumentException($"port '{port}' is not a SOAP port of the WSDL; its SOAP ports: "
				+ string.Join(", ", document.Ports.Select(p => p.Name)) + ".")
			: [selected];
	}

	private static WsdlPort SelectOperations(WsdlPort port, IReadOnlyList<string> operations) {
		if (operations is not { Count: > 0 }) {
			return port;
		}
		var wanted = new HashSet<string>(operations.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()),
			StringComparer.OrdinalIgnoreCase);
		string[] unknown = wanted.Where(name => port.Operations.All(o => !string.Equals(o.Caption, name,
			StringComparison.OrdinalIgnoreCase))).ToArray();
		if (unknown.Length > 0) {
			throw new ArgumentException($"operations: {string.Join(", ", unknown)} not found in port '{port.Name}'; its "
				+ "operations: " + string.Join(", ", port.Operations.Select(o => o.Caption).Take(200)) + ".");
		}
		return port with { Operations = port.Operations.Where(o => wanted.Contains(o.Caption)).ToList() };
	}

	private WsdlTargetService ReadTarget(string serviceCode) {
		if (string.IsNullOrWhiteSpace(serviceCode)) {
			return null;
		}
		WebServiceToolResult described = designer.Describe(serviceCode.Trim(), false);
		WebServiceView service = described.Service;
		if (service is null || !service.Type.StartsWith("SOAP", StringComparison.Ordinal)) {
			throw new ArgumentException($"service-code '{serviceCode}' is a {service?.Type ?? "unknown"} service; a WSDL's "
				+ "operations go into a SOAP service.");
		}
		return new WsdlTargetService(service.Code, service.Namespaces ?? [], service.Methods);
	}
}
