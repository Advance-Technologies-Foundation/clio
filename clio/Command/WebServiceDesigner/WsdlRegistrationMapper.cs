using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Command.WebServiceDesigner;

/// <summary>An existing service the WSDL's operations are read for: its methods keep their codes.</summary>
/// <param name="Code">The service code.</param>
/// <param name="Namespaces">Its namespaces, the default first.</param>
/// <param name="Methods">Its methods with message element and SOAP action.</param>
public sealed record WsdlTargetService(string Code, IReadOnlyList<WebServiceNamespaceView> Namespaces,
	IReadOnlyList<WebServiceMethodView> Methods);

/// <summary>A port turned into register-web-service arguments.</summary>
/// <param name="Port">The port name.</param>
/// <param name="StudioPick">Whether Studio's quick setup would create this port's service.</param>
/// <param name="Arguments">The register-web-service arguments, without package-name.</param>
/// <param name="ExistingMethods">With a target service: the codes of its methods these operations replace.</param>
public sealed record WsdlServiceArguments(string Port, bool StudioPick, WsdlRegistrationArgs Arguments,
	IReadOnlyList<string> ExistingMethods);

/// <summary>What read-wsdl proposes for one port: register-web-service input without package-name.</summary>
public sealed record WsdlRegistrationArgs(
	[property: System.Text.Json.Serialization.JsonPropertyName("environment-name")] string EnvironmentName,
	[property: System.Text.Json.Serialization.JsonPropertyName("code")] string Code,
	[property: System.Text.Json.Serialization.JsonPropertyName("caption")] string Caption,
	[property: System.Text.Json.Serialization.JsonPropertyName("base-uri")] string BaseUri,
	[property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
	[property: System.Text.Json.Serialization.JsonPropertyName("namespaces")] IReadOnlyList<WebServiceNamespaceArg> Namespaces,
	[property: System.Text.Json.Serialization.JsonPropertyName("methods")] IReadOnlyList<WebServiceMethodArg> Methods);

/// <summary>
/// Turns a port read with Studio's wizard rules into register-web-service arguments with clio's rules (ADR D11): stable
/// codes (the environment's schema-name prefix and the wizard's shortened name, a number only on a collision, never the
/// wizard's random suffix), the message element with its namespace prefix, and nothing register-web-service would refuse:
/// a parameter or method it would refuse is left out with a warning.
/// </summary>
public static partial class WsdlRegistrationMapper {

	private const int MaxCodeLength = 200;

	[GeneratedRegex("[^A-Za-z0-9_]")]
	private static partial Regex NonIdentifierCharacter();

	[GeneratedRegex(@"^(?:([A-Za-z_][A-Za-z0-9_\-]*):)?([A-Za-z_][A-Za-z0-9_\-]*)$")]
	private static partial Regex PlatformElementName();

	/// <summary>Maps one port.</summary>
	/// <param name="port">The port as the parser read it.</param>
	/// <param name="studioPick">Whether Studio's quick setup would pick it.</param>
	/// <param name="prefix">The environment's schema-name prefix; may be empty.</param>
	/// <param name="environmentName">The environment the arguments are for.</param>
	/// <param name="target">An existing service the operations go into, or null for a new service.</param>
	/// <param name="warnings">Collected warnings.</param>
	/// <returns>The arguments.</returns>
	public static WsdlServiceArguments Map(WsdlPort port, bool studioPick, string prefix, string environmentName,
		WsdlTargetService target, ICollection<string> warnings) {
		ArgumentNullException.ThrowIfNull(port);
		prefix ??= string.Empty;
		var context = new PortContext(port, prefix, target, warnings);
		List<WebServiceNamespaceArg> namespaces = context.BuildNamespaces();
		var methods = new List<WebServiceMethodArg>();
		var existing = new List<string>();
		var methodCodes = new HashSet<string>(
			(target?.Methods ?? []).Select(m => m.Code), StringComparer.OrdinalIgnoreCase);
		IReadOnlyDictionary<WsdlOperation, string> reused = ReusedCodes(port, context, target);
		foreach (WsdlOperation operation in port.Operations) {
			WebServiceMethodArg method = context.BuildMethod(operation, reused.GetValueOrDefault(operation), methodCodes);
			if (method is null) {
				continue;
			}
			methods.Add(method);
			if (reused.ContainsKey(operation)) {
				existing.Add(method.Code);
			}
		}
		string code = target?.Code ?? ServiceCode(prefix, port.ServiceName);
		string type = port.ServiceType == ServiceSchemaCodes.ServiceTypeSoap12 ? "soap12" : "soap11";
		return new WsdlServiceArguments(port.Name, studioPick,
			new WsdlRegistrationArgs(environmentName, code, port.ServiceName, port.BaseUri, type, namespaces, methods),
			target is null ? null : existing);
	}

	// A WSDL operation is the same as an existing method when the root element (local name) and the SOAP action match:
	// that method keeps its code, so a re-read gives setMethod input for it.
	private static Dictionary<WsdlOperation, string> ReusedCodes(WsdlPort port, PortContext context,
		WsdlTargetService target) {
		var result = new Dictionary<WsdlOperation, string>();
		if (target is null) {
			return result;
		}
		var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (WsdlOperation operation in port.Operations) {
			string localName = LocalName(context.MessageElement(operation));
			WebServiceMethodView match = target.Methods.FirstOrDefault(m => !claimed.Contains(m.Code)
				&& string.Equals(LocalName(m.MessageElement), localName, StringComparison.Ordinal)
				&& string.Equals(m.SoapAction ?? string.Empty, operation.SoapAction ?? string.Empty, StringComparison.Ordinal));
			if (match is not null) {
				claimed.Add(match.Code);
				result[operation] = match.Code;
			}
		}
		return result;
	}

	private static string LocalName(string element) {
		string value = element ?? string.Empty;
		int colon = value.IndexOf(':');
		return colon < 0 ? value : value[(colon + 1)..];
	}

	/// <summary>The service code: the prefix and the service name as a Latin identifier (the wizard does not shorten it).</summary>
	internal static string ServiceCode(string prefix, string serviceName) =>
		Capped(prefix, Identifier(serviceName ?? string.Empty, "Service", prefix));

	private static string Identifier(string name, string fallback, string prefix) {
		string tail = NonIdentifierCharacter().Replace(name, string.Empty);
		if (tail.Length == 0) {
			tail = fallback;
		}
		return prefix.Length == 0 && char.IsDigit(tail[0]) ? "_" + tail : tail;
	}

	/// <summary>
	/// A code: the prefix, then the wizard's shortened name made a Latin identifier, capped at 200 characters by
	/// keeping the end of the name as the wizard does but never the prefix; a number from 2 on a collision.
	/// </summary>
	internal static string UniqueCode(string prefix, string name, string fallback, ISet<string> taken) {
		string tail = Identifier(WsdlParser.ShortenCaption((name ?? string.Empty).Replace(" ", string.Empty)), fallback,
			prefix);
		string code = Capped(prefix, tail);
		for (int index = 2; taken.Contains(code); index++) {
			code = Capped(prefix, tail + index);
		}
		taken.Add(code);
		return code;
	}

	private static string Capped(string prefix, string tail) {
		int room = Math.Max(MaxCodeLength - prefix.Length, 1);
		return prefix + (tail.Length <= room ? tail : tail[^room..]);
	}

	/// <summary>The per-port mapping: the namespace prefixes and the warnings.</summary>
	private sealed class PortContext {
		private readonly WsdlPort _port;
		private readonly string _prefix;
		private readonly ICollection<string> _warnings;
		private readonly Dictionary<string, string> _prefixRename = new(StringComparer.Ordinal);
		private readonly HashSet<string> _unqualified = new(StringComparer.Ordinal);
		private readonly Dictionary<string, string> _prefixByUri = new(StringComparer.Ordinal);
		private readonly WsdlTargetService _target;
		private string _operation;

		public PortContext(WsdlPort port, string prefix, WsdlTargetService target, ICollection<string> warnings) {
			_port = port;
			_prefix = prefix;
			_target = target;
			_warnings = warnings;
		}

		private void Warn(string message) {
			if (!_warnings.Contains(message)) {
				_warnings.Add(message);
			}
		}

		/// <summary>
		/// The namespaces to declare. A namespace with no URI (an element of no namespace) cannot be declared: its prefix is
		/// dropped from the paths, so the platform puts those elements in the default namespace. With a target service its
		/// namespaces come first and keep their prefixes; a WSDL namespace it lacks gets a free ns-prefix.
		/// </summary>
		public List<WebServiceNamespaceArg> BuildNamespaces() {
			var result = new List<WebServiceNamespaceArg>();
			var prefixes = new HashSet<string>(StringComparer.Ordinal);
			foreach (WebServiceNamespaceView existing in _target?.Namespaces ?? []) {
				result.Add(new WebServiceNamespaceArg(existing.Prefix, existing.Uri));
				prefixes.Add(existing.Prefix);
				_prefixByUri.TryAdd(existing.Uri ?? string.Empty, existing.Prefix);
			}
			foreach (WsdlNamespace ns in _port.Namespaces) {
				if (string.IsNullOrEmpty(ns.Uri)) {
					_unqualified.Add(ns.Prefix);
					Warn($"Port '{_port.Name}': some elements have no namespace ({ns.Prefix} in Studio's paths); the platform "
						+ "puts an element without a prefix in the default namespace, so those elements were given none. Test a "
						+ "call: an API that expects unqualified elements may refuse them.");
					continue;
				}
				if (_prefixByUri.TryGetValue(ns.Uri, out string kept)) {
					_prefixRename[ns.Prefix] = kept;
					continue;
				}
				string prefix = ns.Prefix;
				for (int index = 0; prefixes.Contains(prefix); index++) {
					prefix = "ns" + index;
				}
				prefixes.Add(prefix);
				_prefixRename[ns.Prefix] = prefix;
				_prefixByUri[ns.Uri] = prefix;
				result.Add(new WebServiceNamespaceArg(prefix, ns.Uri));
			}
			return result;
		}

		/// <summary>The message element with the prefix of its namespace (Studio stores the bare local name).</summary>
		public string MessageElement(WsdlOperation operation) {
			string ns = operation.MessageElementNamespace;
			return !string.IsNullOrEmpty(ns) && _prefixByUri.TryGetValue(ns, out string prefix)
				? $"{prefix}:{operation.StudioMessageElement}"
				: operation.StudioMessageElement;
		}

		public WebServiceMethodArg BuildMethod(WsdlOperation operation, string reusedCode, ISet<string> methodCodes) {
			_operation = operation.Caption;
			string messageElement = MessageElement(operation);
			if (!PlatformElementName().IsMatch(messageElement)) {
				Warn($"Operation '{operation.Caption}' was left out: its request element '{messageElement}' has a character "
					+ "the platform cuts element names at (only letters, digits, '_' and '-' survive), so no call could reach "
					+ "it. Describe it by hand only if the API accepts the cut name.");
				return null;
			}
			string code = reusedCode ?? UniqueCode(_prefix, operation.Caption, "Method", methodCodes);
			var requestCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var responseCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<WebServiceParameterArg> request = Parameters(operation.Request, true, string.Empty, requestCodes);
			List<WebServiceParameterArg> response = Parameters(operation.Response, false, string.Empty, responseCodes);
			return new WebServiceMethodArg(code, null, null,
				Caption: operation.Caption,
				RequestParameters: request.Count > 0 ? request : null,
				ResponseParameters: response.Count > 0 ? response : null,
				MessageElement: messageElement,
				SoapAction: string.IsNullOrEmpty(operation.SoapAction) ? null : operation.SoapAction);
		}

		private List<WebServiceParameterArg> Parameters(IReadOnlyList<WsdlParameter> parameters, bool isRequest,
			string parentCaption, ISet<string> codes) {
			var result = new List<WebServiceParameterArg>();
			foreach (WsdlParameter parameter in parameters) {
				WebServiceParameterArg arg = Parameter(parameter, isRequest, parentCaption, codes);
				if (arg is not null) {
					result.Add(arg);
				}
			}
			return result;
		}

		private WebServiceParameterArg Parameter(WsdlParameter parameter, bool isRequest, string parentCaption,
			ISet<string> codes) {
			string side = isRequest ? "request" : "response";
			string path = ElementPath(parameter.Path);
			if (parameter.IsCopy) {
				Warn($"Operation '{_operation}': {side} element '{path}' was left out: Studio's wizard lists an element of a "
					+ "nested anonymous type a second time one level too high, and no such element exists there.");
				return null;
			}
			if (path.Length == 0) {
				Warn($"Operation '{_operation}': a repeated {side} element without a wrapper element was left out; Studio's "
					+ "wizard gives it an empty path, which the platform cannot address.");
				return null;
			}
			if (isRequest && !path.Split('/').All(segment => PlatformElementName().IsMatch(segment))) {
				Warn($"Operation '{_operation}': request element '{path}' was left out: the platform cuts element names at "
					+ "any character other than letters, digits, '_' and '-', so it would send another element.");
				return null;
			}
			string code = UniqueCode(_prefix, parentCaption + parameter.Caption, "Param", codes);
			List<WebServiceParameterArg> items = parameter.Type == "Object"
				? Parameters(parameter.Items, isRequest, parameter.Caption, codes)
				: [];
			if (parameter.Type == "Object" && (!parameter.IsArray || items.Count == 0)) {
				Warn($"Operation '{_operation}': {side} element '{path}' was left out: it is an object with no readable "
					+ "fields, which a web service parameter cannot describe.");
				return null;
			}
			string sequenceElement = parameter.IsArray ? ElementPath(parameter.SequenceElement) : null;
			return new WebServiceParameterArg(code,
				Type: parameter.Type,
				Path: path,
				Caption: parameter.Caption,
				Array: parameter.IsArray ? true : null,
				Items: items.Count > 0 ? items : null,
				SoapHeader: parameter.IsSoapHeader && isRequest ? true : null,
				SequenceElement: string.IsNullOrEmpty(sequenceElement) ? null : sequenceElement);
		}

		// The path with clio's prefixes: renamed where a target service already declares the namespace, removed where the
		// element has no namespace.
		private string ElementPath(string path) {
			if (string.IsNullOrEmpty(path)) {
				return string.Empty;
			}
			var builder = new StringBuilder();
			foreach (string segment in path.Split('/')) {
				if (builder.Length > 0) {
					builder.Append('/');
				}
				int colon = segment.IndexOf(':');
				string prefix = colon < 0 ? null : segment[..colon];
				string name = colon < 0 ? segment : segment[(colon + 1)..];
				if (prefix is null || _unqualified.Contains(prefix)) {
					builder.Append(name);
				} else {
					builder.Append(_prefixRename.GetValueOrDefault(prefix, prefix)).Append(':').Append(name);
				}
			}
			return builder.ToString();
		}
	}
}
