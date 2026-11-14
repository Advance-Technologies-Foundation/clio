using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Clio.Command.WebServiceDesigner;

/// <summary>An existing service the WSDL's operations are read for: its methods keep their codes.</summary>
/// <param name="Code">The service code.</param>
/// <param name="Namespaces">Its namespaces, the default first.</param>
/// <param name="Methods">Its methods with message element, SOAP action and parameter trees.</param>
public sealed record WsdlTargetService(string Code, IReadOnlyList<WebServiceNamespaceView> Namespaces,
	IReadOnlyList<WebServiceMethodView> Methods);

/// <summary>A port turned into register-web-service arguments.</summary>
/// <param name="Port">The port name.</param>
/// <param name="StudioPick">Whether Studio's quick setup would create this port's service.</param>
/// <param name="Arguments">The register-web-service arguments, without package-name.</param>
/// <param name="ExistingMethods">With a target service: the codes of its methods these operations replace.</param>
public sealed record WsdlServiceArguments(string Port, bool StudioPick, WsdlRegistrationArgs Arguments,
	IReadOnlyList<string> ExistingMethods);

/// <summary>
/// The warnings of one read-wsdl call, across every port: each message once, the first <see cref="MaxKept"/> kept and the
/// rest counted. Lookup and insertion are constant time, so a WSDL that yields a warning per element (up to the parser's
/// element budget) costs linear time, and the messages remembered for deduplication are bounded too.
/// </summary>
public sealed class WarningLog {
	/// <summary>The most warnings a result lists.</summary>
	public const int MaxKept = 100;

	/// <summary>The most distinct messages remembered to count the rest without repeats.</summary>
	internal const int MaxTracked = 10_000;

	private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
	private readonly List<string> _kept = [];

	/// <summary>The warnings to list, in the order they were added.</summary>
	public IReadOnlyList<string> Kept => _kept;

	/// <summary>How many more distinct warnings there were (an upper bound once too many were seen to track).</summary>
	public int Dropped { get; private set; }

	/// <summary>Counts warnings another cap already left out (the parser's), so the count line stays true.</summary>
	/// <param name="count">How many were left out.</param>
	public void AddDropped(int count) {
		if (count > 0) {
			Dropped += count;
		}
	}

	/// <summary>Adds a warning unless the same message was added before.</summary>
	/// <param name="message">The warning.</param>
	public void Add(string message) {
		if (_seen.Count < MaxTracked) {
			if (!_seen.Add(message)) {
				return;
			}
		} else if (_seen.Contains(message)) {
			return;
		}
		if (_kept.Count < MaxKept) {
			_kept.Add(message);
		} else {
			Dropped++;
		}
	}

	/// <summary>Whether a message was added (among the tracked ones).</summary>
	public bool Contains(string message) => _seen.Contains(message);

	/// <summary>The warnings as a result lists them: the kept ones, then a count of the rest.</summary>
	public IReadOnlyList<string> ToList() =>
		Dropped == 0 ? _kept : [.. _kept, $"...and {Dropped} more warnings."];
}

/// <summary>What read-wsdl proposes for one port: register-web-service input without package-name.</summary>
public sealed record WsdlRegistrationArgs(
	[property: JsonPropertyName("environment-name")] string EnvironmentName,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("base-uri")] string BaseUri,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("namespaces")] IReadOnlyList<WebServiceNamespaceArg> Namespaces,
	[property: JsonPropertyName("methods")] IReadOnlyList<WebServiceMethodArg> Methods);

/// <summary>
/// Turns a port read with Studio's wizard rules into register-web-service arguments with clio's rules (ADR D11): stable
/// codes (the environment's schema-name prefix and the full WSDL name in PascalCase, a number only on a collision, never
/// the wizard's shortening or random suffix), the message element with its namespace prefix, and nothing register-web-service would refuse
/// in a parameter or a method: such a parameter or method is left out with a warning. A service-level value it would
/// refuse (the base URI, a namespace URI) is kept with a warning, because the agent can only replace it, not drop it.
/// </summary>
public static partial class WsdlRegistrationMapper {

	private const int MaxCodeLength = WsdlParser.MaxCodeLength;
	private const string ObjectType = WsdlParser.ObjectType;

	/// <summary>The longest soapAction kept; a longer one is not an action, and it would only carry text to the agent.</summary>
	internal const int MaxSoapActionLength = 2048;

	/// <summary>The longest namespace URI the arguments carry; a longer one is left out with a warning.</summary>
	internal const int MaxNamespaceUriLength = 2048;

	/// <summary>The longest base URI the arguments carry; a longer one is left out with a warning.</summary>
	internal const int MaxBaseUriLength = 2048;

	[GeneratedRegex("[^A-Za-z0-9_]")]
	private static partial Regex NonIdentifierCharacter();

	[GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
	private static partial Regex WordSeparator();

	[GeneratedRegex("[^A-Za-z0-9]")]
	private static partial Regex NonLatinCharacter();

	/// <summary>Maps one port.</summary>
	/// <param name="port">The port as the parser read it.</param>
	/// <param name="studioPick">Whether Studio's quick setup would pick it.</param>
	/// <param name="prefix">The environment's schema-name prefix; may be empty.</param>
	/// <param name="environmentName">The environment the arguments are for.</param>
	/// <param name="target">An existing service the operations go into, or null for a new service.</param>
	/// <param name="warnings">The warnings of the call, shared by every port so a message common to them is listed once.</param>
	/// <param name="newCode">The code the user named for a new service; null builds one from the WSDL service name.</param>
	/// <returns>The arguments.</returns>
	public static WsdlServiceArguments Map(WsdlPort port, bool studioPick, string prefix, string environmentName,
		WsdlTargetService target, WarningLog warnings,
		string newCode = null) {
		ArgumentNullException.ThrowIfNull(port);
		prefix ??= string.Empty;
		var context = new PortContext(port, prefix, target, warnings);
		List<WebServiceNamespaceArg> namespaces = context.BuildNamespaces();
		string baseUri = context.CheckBaseUri();
		var methods = new List<WebServiceMethodArg>();
		var existing = new List<string>();
		var methodCodes = new CodePool((target?.Methods ?? []).Select(m => m.Code));
		IReadOnlyDictionary<WsdlOperation, WebServiceMethodView> reused = ReusedMethods(port, target);
		foreach (WsdlOperation operation in port.Operations) {
			WebServiceMethodView match = reused.GetValueOrDefault(operation);
			WebServiceMethodArg method = context.BuildMethod(operation, match, methodCodes);
			if (method is null) {
				continue;
			}
			methods.Add(method);
			if (match is not null) {
				existing.Add(method.Code);
			}
		}
		// An existing service keeps its code; a new one takes the code the user named, else one built from the WSDL.
		string code = target?.Code ?? newCode ?? ServiceCode(prefix, port.ServiceName);
		string type = port.ServiceType == ServiceSchemaCodes.ServiceTypeSoap12 ? "soap12" : "soap11";
		return new WsdlServiceArguments(port.Name, studioPick,
			new WsdlRegistrationArgs(environmentName, code, port.ServiceName, baseUri, type, namespaces, methods),
			target is null ? null : existing);
	}

	// The soapAction the arguments carry, without the spaces around it register trims too: one that is not printable ASCII
	// or carries credentials, or an overlong one, is left out (with a warning where the method is built).
	private static string KeptSoapAction(string action) {
		string trimmed = action?.Trim();
		return string.IsNullOrEmpty(trimmed) || !IsPlainValue(trimmed, MaxSoapActionLength)
			|| WebServiceDefinitionBuilder.HasUserInfo(trimmed)
			? null
			: trimmed;
	}

	// A value the arguments may carry as written: printable ASCII only (no spaces, control, format or invisible
	// characters of any plane), and not overlong.
	private static bool IsPlainValue(string value, int maxLength) =>
		value.Length <= maxLength && value.All(c => c is > ' ' and < (char)0x7F);

	// A WSDL operation is the same as an existing method when the root element (local name) and the SOAP action match:
	// that method keeps its code, so a re-read gives setMethod input for it. The action compared is the one the arguments
	// carry, so a method registered from a read that left the action out matches again.
	private static Dictionary<WsdlOperation, WebServiceMethodView> ReusedMethods(WsdlPort port, WsdlTargetService target) {
		var result = new Dictionary<WsdlOperation, WebServiceMethodView>();
		if (target is null) {
			return result;
		}
		var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (WsdlOperation operation in port.Operations) {
			WebServiceMethodView match = target.Methods.FirstOrDefault(m => !claimed.Contains(m.Code)
				&& string.Equals(LocalName(m.MessageElement), operation.StudioMessageElement, StringComparison.Ordinal)
				// describe masks a key-like query value of the stored action, so the WSDL's action is compared masked too.
				&& string.Equals(m.SoapAction?.Trim() ?? string.Empty,
					WebServiceSchemaView.MaskUriQuery(KeptSoapAction(operation.SoapAction)) ?? string.Empty,
					StringComparison.Ordinal));
			if (match is not null) {
				claimed.Add(match.Code);
				result[operation] = match;
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
		Capped(prefix, Identifier(serviceName ?? string.Empty, "Service"));

	private static string Identifier(string name, string fallback) {
		string tail = NonIdentifierCharacter().Replace(name, string.Empty);
		return tail.Length == 0 ? fallback : tail;
	}

	/// <summary>
	/// The longest parameter code. The Call web service element names its process parameters <c>&lt;code&gt;_In</c> and
	/// <c>&lt;code&gt;_Out</c>, and a compiled process declares a C# field <c>_&lt;name&gt;</c> for each, where an identifier
	/// may have at most 512 characters (CS0645): a longer code breaks the compile of every process that calls the method.
	/// 500 leaves room below that. A method code is only a string value there, so it is not cut.
	/// </summary>
	internal const int MaxParameterCodeLength = 500;

	/// <summary>
	/// A method or parameter code (ADR D11): the prefix, then the full name in PascalCase as a Latin identifier
	/// (GetCustomerOrders stays GetCustomerOrders; a flattened path joins its names, customer/name -> CustomerName); a number
	/// from 2 only on a collision. Studio's code validators check only that a code is a Latin letter followed by Latin
	/// letters, digits or underscores, so without a prefix a code that would start with a digit starts with the fallback
	/// word. A code is cut only at <paramref name="maxLength"/>, keeping the prefix and the end of the name, as the wizard
	/// keeps it; the wizard's own 200 characters are its generator's choice, not a limit of Studio or the platform.
	/// </summary>
	internal static string UniqueCode(string prefix, string name, string fallback, CodePool taken,
			int maxLength = int.MaxValue) {
		string tail = Identifier(PascalCase(name), fallback);
		if (prefix.Length == 0 && char.IsDigit(tail[0])) {
			tail = fallback + tail;
		}
		return taken.Claim(prefix, tail, maxLength);
	}

	/// <summary>
	/// The local names of an element path, joined by spaces (<c>tem:order/tem:id</c> -> <c>order id</c>): what a parameter
	/// code is built from. Studio's caption is not used, because it drops a first word every top-level parameter shares (a
	/// wrapper element), so <c>customer/name</c> would lose <c>customer</c> and a code would change with its siblings.
	/// </summary>
	internal static string PathNames(string path) =>
		string.Join(' ', (path ?? string.Empty).Split('/').Select(segment => segment[(segment.IndexOf(':') + 1)..]));

	/// <summary>
	/// The words of a name, each with a capital first letter, joined (customer name, short_description, intA). Words are
	/// split at what is neither a letter nor a digit; a letter outside Latin is then dropped from its word, so Größe is
	/// Gre, not two words. A name with no Latin letter or digit at all gives an empty string, and the caller's fallback.
	/// </summary>
	internal static string PascalCase(string name) =>
		string.Concat(WordSeparator().Split(name ?? string.Empty)
			.Select(word => NonLatinCharacter().Replace(word, string.Empty)).Where(word => word.Length > 0)
			.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

	/// <summary>
	/// The codes in use, with the next number to try per name: numbering many equal names stays linear instead of probing
	/// 2, 3, 4... again for each of them. A code given back (an object with no describable field) lowers the next number of
	/// the name it was claimed for to its own, so the next claim of that name takes it again; the other names keep theirs,
	/// so giving codes back does not start every name's probing over.
	/// </summary>
	internal sealed class CodePool {
		private readonly HashSet<string> _taken;
		private readonly Dictionary<string, int> _next = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, (string Tail, int Index)> _claimedAs = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Creates a pool with codes already taken.</summary>
		/// <param name="taken">Codes no new one may take.</param>
		public CodePool(IEnumerable<string> taken = null) {
			_taken = new HashSet<string>((taken ?? []).Where(c => c is not null), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Takes the code for the name, or the name with the lowest free number from 2.</summary>
		/// <param name="prefix">The schema-name prefix.</param>
		/// <param name="tail">The name after the prefix, already an identifier.</param>
		/// <param name="maxLength">The longest code; a longer one keeps the prefix and the end of the name.</param>
		/// <returns>The code taken.</returns>
		public string Claim(string prefix, string tail, int maxLength = int.MaxValue) {
			string code = Fit(prefix, tail, maxLength);
			// Numbers are remembered per code, not per name: long names that end alike fit to one code, and each starting
			// again at 2 probed every number already taken.
			string key = code;
			int index = 1;
			if (_taken.Contains(code)) {
				index = _next.GetValueOrDefault(key, 2);
				code = Fit(prefix, tail + index, maxLength);
				while (_taken.Contains(code)) {
					index++;
					code = Fit(prefix, tail + index, maxLength);
				}
				_next[key] = index + 1;
			}
			_taken.Add(code);
			_claimedAs[code] = (key, index);
			return code;
		}

		/// <summary>Gives a claimed code back.</summary>
		/// <param name="code">The code.</param>
		public void Release(string code) {
			if (_taken.Remove(code) && _claimedAs.Remove(code, out (string Tail, int Index) claimed) && claimed.Index > 1
					&& _next.TryGetValue(claimed.Tail, out int next) && claimed.Index < next) {
				_next[claimed.Tail] = claimed.Index;
			}
		}
	}

	// A method or parameter code within maxLength, keeping the end of a longer name; without a prefix a cut that leaves
	// something other than a Latin letter first gets one (Studio's validator wants it), from the name it cut.
	private static string Fit(string prefix, string tail, int maxLength) {
		if (prefix.Length + tail.Length <= maxLength) {
			return prefix + tail;
		}
		string kept = tail[^Math.Max(maxLength - prefix.Length, 1)..];
		return prefix.Length == 0 && !char.IsAsciiLetter(kept[0]) ? tail[0] + kept[1..] : prefix + kept;
	}

	// The service code (a schema name) keeps the end of a long name within 200 characters, as the wizard does; a code
	// without a prefix must still start with a letter or an underscore, so a cut that leaves a digit first gets one.
	private static string Capped(string prefix, string tail) {
		int room = Math.Max(MaxCodeLength - prefix.Length, 1);
		string kept = tail.Length <= room ? tail : tail[^room..];
		if (prefix.Length == 0 && char.IsDigit(kept[0])) {
			kept = "_" + (kept.Length < MaxCodeLength ? kept : kept[1..]);
		}
		return prefix + kept;
	}

	/// <summary>The per-port mapping: the namespace prefixes and the warnings.</summary>
	private sealed class PortContext(WsdlPort port, string prefix, WsdlTargetService target, WarningLog warnings) {
		private readonly Dictionary<string, string> _prefixRename = new(StringComparer.Ordinal);
		private readonly HashSet<string> _unqualified = new(StringComparer.Ordinal);
		// The parameters of the matched method the WSDL has, by reference, so the rest can be named.
		private readonly HashSet<WebServiceParameterView> _matched = new(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<string, string> _prefixByUri = new(StringComparer.Ordinal);
		private string _operation;

		private void Warn(string message) => warnings.Add(message);

		private static string Shown(string value) => WsdlParser.Shown(value);

		/// <summary>
		/// The namespaces to declare. A namespace with no URI (an element of no namespace, or a prefix the WSDL never
		/// declares) cannot be declared: its prefix is dropped from the paths, so the platform puts those elements in the
		/// default namespace. With a target service its namespaces come first and keep their prefixes; a WSDL namespace it
		/// lacks gets a free ns-prefix.
		/// </summary>
		public List<WebServiceNamespaceArg> BuildNamespaces() {
			var result = new List<WebServiceNamespaceArg>();
			var prefixes = new HashSet<string>(StringComparer.Ordinal);
			foreach (WebServiceNamespaceView existing in target?.Namespaces ?? []) {
				result.Add(new WebServiceNamespaceArg(existing.Prefix, existing.Uri));
				prefixes.Add(existing.Prefix);
				_prefixByUri.TryAdd(existing.Uri ?? string.Empty, existing.Prefix);
			}
			foreach (WsdlNamespace ns in port.Namespaces) {
				if (string.IsNullOrEmpty(ns.Uri)) {
					_unqualified.Add(ns.Prefix);
					Warn(ns.Uri is null
						? $"Port '{port.Name}': the WSDL uses a namespace prefix it never declares; those elements were given no "
							+ "prefix, so the platform puts them in the default namespace. Check the WSDL with the API owner."
						: $"Port '{port.Name}': some elements have no namespace; the platform puts an element without a prefix "
							+ "in the default namespace, so those elements were given none. Test a call: an API that expects "
							+ "unqualified elements may refuse them.");
					continue;
				}
				if (_prefixByUri.TryGetValue(ns.Uri, out string kept)) {
					_prefixRename[ns.Prefix] = kept;
					continue;
				}
				string nsPrefix = ns.Prefix;
				int index = 0;
				while (prefixes.Contains(nsPrefix)) {
					nsPrefix = "ns" + index;
					index++;
				}
				prefixes.Add(nsPrefix);
				_prefixRename[ns.Prefix] = nsPrefix;
				_prefixByUri[ns.Uri] = nsPrefix;
				// A URI that is not printable ASCII, an overlong one or one with credentials is not a namespace the platform
				// can declare, and it would carry WSDL text into the arguments the agent forwards. It is left out: the elements
				// that use its prefix are then refused by register-web-service until the user gives the right URI.
				if (!IsPlainValue(ns.Uri, MaxNamespaceUriLength) || WebServiceDefinitionBuilder.HasUserInfo(ns.Uri)) {
					Warn($"Port '{port.Name}': the namespace URI '{Shown(ns.Uri)}' has spaces, control or non-ASCII "
						+ $"characters or credentials, or is longer than {MaxNamespaceUriLength} characters; it was left out, so "
						+ $"the elements with prefix '{nsPrefix}' cannot be registered. Ask the API owner for the right URI.");
					continue;
				}
				result.Add(new WebServiceNamespaceArg(nsPrefix, ns.Uri));
			}
			return result;
		}

		/// <summary>
		/// The base URI, without credentials or a key. One register-web-service would refuse, or with spaces, control
		/// characters or a fragment, is not returned at all: it could carry WSDL text or a token into the arguments the agent
		/// forwards. A warning, without the fragment, asks for the real endpoint, because only the user can give it.
		/// </summary>
		public string CheckBaseUri() {
			string baseUri = WithoutSecrets(port.BaseUri?.Trim());
			if (string.IsNullOrEmpty(baseUri)) {
				return baseUri;
			}
			// The fragment and the characters are checked first: register's own error repeats the address.
			string reason = null;
			if (baseUri.Contains('#')) {
				reason = "it has a fragment";
			} else if (!IsPlainValue(baseUri, MaxBaseUriLength)) {
				reason = $"it has spaces, control or non-ASCII characters, or is longer than {MaxBaseUriLength} characters";
			}
			if (reason is null) {
				var errors = new WebServiceDefinitionBuilder.Errors();
				WebServiceDefinitionBuilder.NormalizeBaseUri(baseUri, errors, soap: true);
				if (errors.Count == 0) {
					return baseUri;
				}
				reason = Shown(errors[0]);
			}
			string shown = WebServiceSchemaView.StripUserInfo(baseUri.Split('#')[0]);
			Warn($"Port '{port.Name}': its address '{Shown(shown)}' cannot be the base-uri ({reason}), so base-uri was left "
				+ "out; ask the user for the real endpoint and set base-uri before registering.");
			return null;
		}

		// The address without the user information of its authority (user:password@host), removed, not masked.
		private static string WithoutUserInfo(string uri) {
			int scheme = uri.IndexOf("://", StringComparison.Ordinal);
			int start = scheme < 0 ? 0 : scheme + 3;
			int end = uri.IndexOfAny(['/', '?', '#'], start);
			if (end < 0) {
				end = uri.Length;
			}
			if (end <= start) {
				return uri;
			}
			int at = uri.LastIndexOf('@', end - 1, end - start);
			return at < 0 ? uri : uri[..start] + uri[(at + 1)..];
		}

		// Credentials or a key in the WSDL's address would reach the agent and its transcript through the arguments, and
		// register would refuse them anyway; they are left out with a warning to set them up through a system setting.
		private string WithoutSecrets(string address) {
			if (string.IsNullOrEmpty(address)) {
				return address;
			}
			string result = address;
			string withoutUser = WithoutUserInfo(result);
			if (!string.Equals(withoutUser, result, StringComparison.Ordinal)) {
				result = withoutUser;
				Warn($"Port '{port.Name}': its address carries credentials (user:password@); they were left out of base-uri. "
					+ "Set the authentication up with a system setting the user fills.");
			}
			int question = result.IndexOf('?');
			if (question >= 0) {
				string[] pairs = result[(question + 1)..].Split('&');
				string[] kept = pairs.Where(pair => {
					int equals = pair.IndexOf('=');
					string name = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
					return !WebServiceSchemaView.IsSecretQueryName(name);
				}).ToArray();
				if (kept.Length < pairs.Length) {
					result = result[..question] + (kept.Length > 0 ? "?" + string.Join('&', kept) : string.Empty);
					Warn($"Port '{port.Name}': its address carries a key or token in the query; it was left out of base-uri. "
						+ "Send it as a parameter whose default is a system setting the user fills.");
				}
			}
			return result;
		}

		/// <summary>The message element with the prefix of its namespace (Studio stores the bare local name).</summary>
		private string MessageElement(WsdlOperation operation) {
			string ns = operation.MessageElementNamespace;
			return !string.IsNullOrEmpty(ns) && _prefixByUri.TryGetValue(ns, out string nsPrefix)
				? $"{nsPrefix}:{operation.StudioMessageElement}"
				: operation.StudioMessageElement;
		}

		public WebServiceMethodArg BuildMethod(WsdlOperation operation, WebServiceMethodView existing,
			CodePool methodCodes) {
			_operation = operation.Caption;
			string messageElement = MessageElement(operation);
			if (!WebServiceDefinitionBuilder.IsSendableElementPath(messageElement) || messageElement.Contains('/')) {
				Warn($"Operation '{Shown(operation.Caption)}' was left out: its request element '{Shown(messageElement)}' is not "
					+ "an element name the platform can send (it cuts a prefixed name at any character other than letters, "
					+ "digits, '_' and '-').");
				return null;
			}
			string code = existing?.Code ?? UniqueCode(prefix, operation.Caption, "Method", methodCodes);
			_matched.Clear();
			List<WebServiceParameterArg> request = Parameters(operation.Request, true, string.Empty,
				CodeSet(existing?.RequestParameters), new ExistingParameters(existing?.RequestParameters));
			List<WebServiceParameterArg> response = Parameters(operation.Response, false, string.Empty,
				CodeSet(existing?.ResponseParameters), new ExistingParameters(existing?.ResponseParameters));
			if (existing is not null) {
				WarnUnmatched(existing, code);
			}
			// setMethod replaces the method, so what was set on the matched one in Studio is carried over: its timeout and
			// authentication here, each matched parameter's default and required flag in Parameter.
			return new WebServiceMethodArg(code, HttpMethod: null, Uri: null,
				Caption: operation.Caption,
				TimeoutMs: existing?.TimeoutMs,
				// False too: without it register takes the service's authentication for the method.
				UseAuth: existing?.UseAuth,
				RequestParameters: request.Count > 0 ? request : null,
				ResponseParameters: response.Count > 0 ? response : null,
				MessageElement: messageElement,
				SoapAction: SoapAction(operation));
		}

		// A SOAP action is sent as a header value and stored like the base URI: a line break or credentials there would
		// make register refuse the method, so such a value is dropped with a warning (register then names the missing
		// action).
		private string SoapAction(WsdlOperation operation) {
			if (string.IsNullOrWhiteSpace(operation.SoapAction)) {
				return null;
			}
			string action = KeptSoapAction(operation.SoapAction);
			if (action is null) {
				Warn($"Operation '{Shown(operation.Caption)}': its soapAction has spaces, a line break, non-ASCII characters "
					+ $"or credentials, or is longer than {MaxSoapActionLength} characters (a soapAction is a URI), and was "
					+ "left out; ask the API owner for the right value.");
			}
			return action;
		}

		// A parameter of the matched method the WSDL does not have (a header or credential parameter added in Studio)
		// would be removed by setMethod, which replaces the method; the agent is told which.
		private void WarnUnmatched(WebServiceMethodView existing, string code) {
			string[] unmatched = Flatten(existing.RequestParameters).Concat(Flatten(existing.ResponseParameters))
				.Where(p => !_matched.Contains(p)).Select(p => Shown(p.Code)).ToArray();
			if (unmatched.Length > 0) {
				Warn($"Method '{Shown(code)}': {string.Join(", ", unmatched.Take(10))}"
					+ (unmatched.Length > 10 ? $" and {unmatched.Length - 10} more" : string.Empty)
					+ " are not in the WSDL. setMethod replaces the method, so copy them from describe-web-service into its "
					+ "parameters, or they are removed.");
			}
		}

		// The codes a matched method's parameters already have, reserved so a new parameter cannot take one.
		private static CodePool CodeSet(IReadOnlyList<WebServiceParameterView> parameters) =>
			new(Flatten(parameters).Select(p => p.Code));

		// A matched method's parameters by place, so a parameter at the same place keeps its code and setMethod keeps its
		// UId. An array's place is its path and its sequence element: sibling arrays that repeat directly inside one
		// element share the path (Studio's wizard gives such an array its container's path). Only a SOAP body or
		// soap-header parameter can be a WSDL element; an HTTP header, query or cookie parameter is never matched, so it is
		// named among those the WSDL lacks.
		private sealed class ExistingParameters {
			private readonly Dictionary<string, WebServiceParameterView> _byPlace = new(StringComparer.Ordinal);
			private readonly Dictionary<string, List<WebServiceParameterView>> _arraysByPath = new(StringComparer.Ordinal);

			public ExistingParameters(IReadOnlyList<WebServiceParameterView> parameters) {
				foreach (WebServiceParameterView parameter in (parameters ?? [])
						.Where(p => p.SoapHeader == true || string.IsNullOrEmpty(p.In) || p.In == "body")) {
					_byPlace.TryAdd(Place(parameter.Path, parameter.Array ? parameter.SequenceElement : null), parameter);
					if (parameter.Array) {
						if (!_arraysByPath.TryGetValue(parameter.Path ?? string.Empty, out List<WebServiceParameterView> arrays)) {
							arrays = [];
							_arraysByPath[parameter.Path ?? string.Empty] = arrays;
						}
						arrays.Add(parameter);
					}
				}
			}

			// An array whose stored item element differs from the WSDL's (Studio's default item, a bare name) is still the
			// one at its path when it is the only array there.
			public WebServiceParameterView Find(string path, string sequenceElement, ISet<WebServiceParameterView> matched) {
				if (_byPlace.TryGetValue(Place(path, sequenceElement), out WebServiceParameterView exact)) {
					return matched.Contains(exact) ? null : exact;
				}
				return sequenceElement is not null && _arraysByPath.TryGetValue(path ?? string.Empty,
						out List<WebServiceParameterView> arrays) && arrays.Count == 1 && !matched.Contains(arrays[0])
					? arrays[0]
					: null;
			}

			private static string Place(string path, string sequenceElement) =>
				(path ?? string.Empty) + "\n" + (sequenceElement ?? string.Empty);
		}

		private static IEnumerable<WebServiceParameterView> Flatten(IReadOnlyList<WebServiceParameterView> parameters) =>
			(parameters ?? []).SelectMany(p => Flatten(p.Items).Prepend(p));

		private List<WebServiceParameterArg> Parameters(IReadOnlyList<WsdlParameter> parameters, bool isRequest,
			string parentName, CodePool codes, ExistingParameters existing) {
			var result = new List<WebServiceParameterArg>();
			foreach (WsdlParameter parameter in parameters) {
				WebServiceParameterArg arg = Parameter(parameter, isRequest, parentName, codes, existing);
				if (arg is not null) {
					result.Add(arg);
				}
			}
			return result;
		}

		private WebServiceParameterArg Parameter(WsdlParameter parameter, bool isRequest, string parentName,
			CodePool codes, ExistingParameters existing) {
			string path = ElementPath(parameter.Path);
			string sequenceElement = parameter.IsArray ? ElementPath(parameter.SequenceElement) : null;
			string refusal = LeaveOutReason(parameter, path, sequenceElement, isRequest);
			if (refusal is not null) {
				Warn(refusal);
				return null;
			}
			bool isObject = parameter.Type == ObjectType;
			string where = LeftOut(path, isRequest);
			WebServiceParameterView previous = existing.Find(path, sequenceElement, _matched);
			if (previous is not null) {
				_matched.Add(previous);
			}
			string name = CodeName(parentName, path, sequenceElement);
			string code = previous?.Code ?? UniqueCode(prefix, name, "Param", codes, MaxParameterCodeLength);
			List<WebServiceParameterArg> items = isObject
				? Parameters(parameter.Items, isRequest, name, codes, new ExistingParameters(previous?.Items))
				: [];
			if (isObject && items.Count == 0) {
				Warn($"{where}: none of its fields can be described, so neither can the object.");
				GiveBack(code, previous, codes);
				return null;
			}
			return Argument(parameter, isRequest, code, path, sequenceElement, previous, items);
		}

		// The name a parameter's code is built from: the names of the whole path (never empty here: LeaveOutReason refuses
		// an empty path), an array's adds the element that repeats, and an item's starts with its array's, so fields of
		// different branches (customer/name, company/name) and sibling arrays (order/lines, order/notes) differ without a
		// number, and a code does not depend on its siblings. A code keeps the end of a long name; the names of nested
		// array items add up, so only the end that can reach a code is kept.
		private static string CodeName(string parentName, string path, string sequenceElement) {
			string name = parentName + " " + PathNames(path)
				+ (string.IsNullOrEmpty(sequenceElement) ? string.Empty : " " + PathNames(sequenceElement));
			return name.Length > 4 * MaxParameterCodeLength ? name[^(4 * MaxParameterCodeLength)..] : name;
		}

		// An object left out: a code generated for it is given back; one a matched method's parameter already has stays
		// reserved, so no other parameter takes it and setMethod gives that parameter's UId to the wrong one. Not sent, the
		// matched parameter is removed by setMethod, so it is named among those the WSDL lacks.
		private void GiveBack(string code, WebServiceParameterView previous, CodePool codes) {
			if (previous is null) {
				codes.Release(code);
			} else {
				_matched.Remove(previous);
			}
		}

		// The argument, with what Studio set on the matched parameter carried over (setMethod replaces the method). A
		// constant is not carried onto a soap header, where register refuses one.
		private static WebServiceParameterArg Argument(WsdlParameter parameter, bool isRequest, string code, string path,
				string sequenceElement, WebServiceParameterView previous, List<WebServiceParameterArg> items) {
			bool soapHeader = parameter.IsSoapHeader && isRequest;
			return new WebServiceParameterArg(code,
				Type: parameter.Type,
				Path: path,
				Caption: parameter.Caption,
				Array: parameter.IsArray ? true : null,
				Required: previous?.Required == true ? true : null,
				DefaultValue: previous is { DefaultMasked: false } && !soapHeader ? previous.DefaultValue : null,
				DefaultSysSetting: previous?.DefaultSysSetting,
				Items: items.Count > 0 ? items : null,
				SoapHeader: soapHeader ? true : null,
				SequenceElement: string.IsNullOrEmpty(sequenceElement) ? null : sequenceElement);
		}

		private string LeftOut(string path, bool isRequest) =>
			$"Operation '{Shown(_operation)}': {(isRequest ? "request" : "response")} element '{Shown(path)}' was left out";

		// Why register-web-service would refuse the parameter, or null when it would take it.
		private string LeaveOutReason(WsdlParameter parameter, string path, string sequenceElement, bool isRequest) {
			string where = LeftOut(path, isRequest);
			if (parameter.IsCopy) {
				return $"{where}: Studio's wizard lists an element of a nested anonymous type a second time one level too "
					+ "high, and no such element exists there.";
			}
			if (parameter.EscapesItem) {
				return $"{where}: it is an array inside an item of another array, and Studio's wizard gives it the outer "
					+ "array's path, which would nest it wrongly; describe it by hand if it is needed.";
			}
			if (path.Length == 0) {
				return $"Operation '{Shown(_operation)}': a repeated {(isRequest ? "request" : "response")} element without a "
					+ "wrapper element was left out; Studio's wizard gives it an empty path, which the platform cannot address.";
			}
			if (isRequest && !WebServiceDefinitionBuilder.IsSendableElementPath(path)) {
				return $"{where}: the platform cuts a prefixed element name at any character other than letters, digits, '_' "
					+ "and '-', so it would send another element.";
			}
			if (parameter.IsArray && !WebServiceDefinitionBuilder.IsSendableSequenceElement(sequenceElement, isRequest)) {
				return $"{where}: its item element '{Shown(sequenceElement)}' is not a name the platform can "
					+ (isRequest ? "send." : "read.");
			}
			return parameter.Type == ObjectType && (!parameter.IsArray || parameter.Items.Count == 0)
				? $"{where}: it is an object with no readable fields, which a web service parameter cannot describe."
				: null;
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
				string segmentPrefix = colon < 0 ? null : segment[..colon];
				string name = colon < 0 ? segment : segment[(colon + 1)..];
				if (segmentPrefix is null || _unqualified.Contains(segmentPrefix)) {
					builder.Append(name);
				} else {
					builder.Append(_prefixRename.GetValueOrDefault(segmentPrefix, segmentPrefix)).Append(':').Append(name);
				}
			}
			return builder.ToString();
		}
	}
}
