using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Clio.Command.WebServiceDesigner;

/// <summary>One WSDL or XSD file to parse.</summary>
/// <param name="Key">
/// The name an import finds the file by: the URL the platform downloaded it from (its <c>schemaLocation</c> as written),
/// or a local path.
/// </param>
/// <param name="Content">The file bytes.</param>
/// <param name="AsciiText">
/// True for a file the platform downloaded: its bytes are ASCII codes of the text (non-ASCII characters arrive as
/// <c>?</c>), so they are read as text and any encoding the XML declaration names is ignored.
/// </param>
public sealed record WsdlSourceFile(string Key, byte[] Content, bool AsciiText = false);

/// <summary>A namespace a SOAP service declares, in the order the wizard adds them; the first is the default.</summary>
/// <param name="Prefix">The prefix: <c>tem</c> for the target namespace, then <c>ns0</c>, <c>ns1</c>, ...</param>
/// <param name="Uri">The namespace URI; empty or null for an element without a namespace.</param>
public sealed record WsdlNamespace(string Prefix, string Uri);

/// <summary>A request or response parameter as Studio's wizard builds it.</summary>
/// <param name="Caption">The element names joined by spaces, e.g. <c>role identifier</c>.</param>
/// <param name="Path">The element path with prefixes, e.g. <c>tem:msg/ns0:role/ns0:identifier</c>.</param>
/// <param name="Type">Text, Integer, Float, Boolean, DateTime, Date, Time, Guid or Object.</param>
/// <param name="IsArray">Whether the element repeats (maxOccurs unbounded).</param>
/// <param name="IsSoapHeader">Whether it comes from the operation's binding input header.</param>
/// <param name="SequenceElement">The item element name with its prefix, e.g. <c>ns0:string</c>.</param>
/// <param name="Items">The fields of each item of an Object array.</param>
/// <param name="StudioCode">The code Studio's wizard gives it: the prefix and its shortened caption.</param>
/// <param name="IsCopy">
/// Whether the wizard read it a second time one level too high: an element of an anonymous type nested inside another
/// element of the same type (<c>Fault/faultNumber</c> next to <c>Fault/detail/faultNumber</c>).
/// </param>
public sealed record WsdlParameter(string Caption, string Path, string Type, bool IsArray, bool IsSoapHeader,
	string SequenceElement, IReadOnlyList<WsdlParameter> Items, string StudioCode, bool IsCopy = false);

/// <summary>An operation of a port as Studio's wizard builds it.</summary>
/// <param name="Caption">The operation name.</param>
/// <param name="StudioCode">Studio's method code without its random <c>_xxxxxxxx</c> suffix.</param>
/// <param name="SoapAction">The binding operation's soapAction; null when it has none.</param>
/// <param name="StudioMessageElement">
/// The request root element Studio stores: the local name of the input message's first part element, else the
/// operation name.
/// </param>
/// <param name="MessageElementNamespace">The namespace of that element; null when the part is typed (RPC style).</param>
/// <param name="Request">Body parameters, then the SOAP header parameters.</param>
/// <param name="Response">The output message parameters.</param>
public sealed record WsdlOperation(string Caption, string StudioCode, string SoapAction, string StudioMessageElement,
	string MessageElementNamespace, IReadOnlyList<WsdlParameter> Request, IReadOnlyList<WsdlParameter> Response);

/// <summary>One SOAP 1.1 or SOAP 1.2 port: what the wizard turns into one web service.</summary>
/// <param name="Name">The port name.</param>
/// <param name="ServiceName">The name of the WSDL service that holds the port; the web service caption.</param>
/// <param name="ServiceType">The platform service type: SOAP 1.1 or SOAP 1.2.</param>
/// <param name="BaseUri">The port address location.</param>
/// <param name="Namespaces">The namespaces in the order the wizard added them.</param>
/// <param name="Operations">The operations of the port type, in WSDL order.</param>
public sealed record WsdlPort(string Name, string ServiceName, int ServiceType, string BaseUri,
	IReadOnlyList<WsdlNamespace> Namespaces, IReadOnlyList<WsdlOperation> Operations);

/// <summary>The SOAP ports of a WSDL and what the parser could not read.</summary>
/// <param name="Ports">The SOAP 1.1 and SOAP 1.2 ports, in WSDL order.</param>
/// <param name="StudioPort">The port Studio's quick setup picks: the first SOAP 1.2 one, else the first; null when none.</param>
/// <param name="Warnings">Ports dropped, elements not read and constructs the wizard does not support.</param>
public sealed record WsdlDocument(IReadOnlyList<WsdlPort> Ports, string StudioPort, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a WSDL 1.1 document with the rules of Studio's web service wizard (the creatio-ui web-service-wizard
/// library: <c>SoapWebServiceBuilder</c>, <c>SoapWebServiceMethodBuilder</c>, <c>WsdlElement</c>,
/// <c>XmlSchemaTypeUtils</c>, <c>WsdlMergeService</c> and the <c>xmlDataValueTypeNamesMap</c> type map), so a service
/// registered from its output matches the one Studio's SOAP quick setup creates.
/// </summary>
public interface IWsdlParser {
	/// <summary>
	/// Merges the schemas the WSDL imports or includes and builds one result per SOAP port. Where the wizard would fail
	/// (a missing input, a type no schema defines, a base type that is not there) the parser reads what it can and adds a
	/// warning instead.
	/// </summary>
	/// <param name="files">The WSDL and every schema it imports by <c>schemaLocation</c>.</param>
	/// <param name="schemaNamePrefix">The environment's schema-name prefix, for Studio's codes.</param>
	/// <returns>The SOAP ports with warnings.</returns>
	/// <exception cref="ArgumentException">
	/// No file holds a WSDL 1.1 service, a file is not well-formed XML or has a DTD, or an imported schema is missing.
	/// </exception>
	WsdlDocument Parse(IReadOnlyList<WsdlSourceFile> files, string schemaNamePrefix);
}

/// <inheritdoc cref="IWsdlParser"/>
public sealed partial class WsdlParser : IWsdlParser {

	internal const string Soap11BindingNamespace = "http://schemas.xmlsoap.org/wsdl/soap/";
	internal const string Soap12BindingNamespace = "http://schemas.xmlsoap.org/wsdl/soap12/";
	private const string Wsdl20Namespace = "http://www.w3.org/ns/wsdl";
	private const string XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema";
	private const string MimeNamespace = "http://schemas.xmlsoap.org/wsdl/mime/";
	private const int MaxCharactersInDocument = 20_000_000;
	private const int MaxCodeLength = 200;

	/// <summary>
	/// The most elements one parse reads. The wizard's recursion cap bounds each type to two levels per path, but a chain of
	/// distinct types that each hold several fields of the next one still grows exponentially; past this the WSDL is
	/// refused instead of exhausting the worker.
	/// </summary>
	internal const int MaxElementsRead = 200_000;

	/// <summary>The deepest a type may name another type (<c>type=</c> on a type, or <c>extension base=</c>).</summary>
	private const int MaxTypeChain = 32;

	/// <summary>The wizard's XSD type map (<c>enums/types-map.ts</c>).</summary>
	internal static readonly IReadOnlyDictionary<string, string> XsdTypes = new Dictionary<string, string>(StringComparer.Ordinal) {
		["string"] = "Text", ["boolean"] = "Boolean", ["decimal"] = "Float", ["float"] = "Float", ["double"] = "Float",
		["dateTime"] = "DateTime", ["time"] = "Time", ["date"] = "Date", ["hexBinary"] = "Text",
		["base64Binary"] = "Text", ["integer"] = "Integer", ["nonPositiveInteger"] = "Text",
		["negativeInteger"] = "Text", ["long"] = "Integer", ["int"] = "Integer", ["short"] = "Integer",
		["byte"] = "Integer", ["nonNegativeInteger"] = "Text", ["unsignedLong"] = "Text", ["unsignedInt"] = "Text",
		["unsignedShort"] = "Integer", ["unsignedByte"] = "Integer", ["positiveInteger"] = "Text",
		["char"] = "Integer", ["guid"] = "Guid", ["anyType"] = "Text"
	};

	[GeneratedRegex("[A-Z][a-z]+")]
	private static partial Regex CapitalizedWordPattern();

	/// <inheritdoc />
	public WsdlDocument Parse(IReadOnlyList<WsdlSourceFile> files, string schemaNamePrefix) {
		if (files is not { Count: > 0 }) {
			throw new ArgumentException("No WSDL file was given.");
		}
		var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
		foreach (WsdlSourceFile file in files) {
			documents.TryAdd(file.Key, Load(file));
		}
		var warnings = new List<string>();
		string wsdlKey = FindWsdl(documents, warnings);
		XDocument merged = new WsdlMerge(documents).Merge(wsdlKey);
		return new Session(merged, schemaNamePrefix ?? string.Empty, warnings).Build();
	}

	private static XDocument Load(WsdlSourceFile file) {
		var settings = new XmlReaderSettings {
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			MaxCharactersInDocument = MaxCharactersInDocument,
			IgnoreComments = true,
			IgnoreProcessingInstructions = true
		};
		try {
			using XmlReader reader = file.AsciiText
				? XmlReader.Create(new StringReader(Encoding.ASCII.GetString(file.Content ?? [])), settings)
				: XmlReader.Create(new MemoryStream(file.Content ?? []), settings);
			return XDocument.Load(reader, LoadOptions.None);
		} catch (XmlException exception) {
			throw new ArgumentException($"'{file.Key}' is not a readable XML file (a DTD is not allowed): "
				+ exception.Message);
		}
	}

	// The wizard takes the file that holds definitions > service; a WSDL 2.0 description has no such element.
	private static string FindWsdl(IReadOnlyDictionary<string, XDocument> documents, List<string> warnings) {
		foreach ((string key, XDocument document) in documents) {
			if (document.Root?.Name.NamespaceName == Wsdl20Namespace) {
				throw new ArgumentException($"'{key}' is a WSDL 2.0 description, which Studio's wizard and the platform "
					+ "do not support; ask the API owner for a WSDL 1.1 document, or describe the service by hand.");
			}
		}
		string[] wsdlKeys = documents.Where(d => FirstChild(d.Value.Root, "service") is not null && Is(d.Value.Root, "definitions"))
			.Select(d => d.Key).ToArray();
		if (wsdlKeys.Length == 0) {
			throw new ArgumentException("No file holds a WSDL 1.1 service (definitions > service); give the WSDL itself, "
				+ "not only its schemas.");
		}
		if (wsdlKeys.Length > 1) {
			warnings.Add($"Several files hold a WSDL service; '{wsdlKeys[0]}' was read.");
		}
		return wsdlKeys[0];
	}

	#region DOM helpers (the selector semantics the wizard relies on: names match by local name, any namespace)

	private static bool Is(XElement element, string localName) =>
		element is not null && element.Name.LocalName == localName;

	private static string Attr(XElement element, string name) => element?.Attribute(name)?.Value;

	private static XElement FirstChild(XElement element, string localName) =>
		element?.Elements().FirstOrDefault(e => Is(e, localName));

	/// <summary><c>getLocalName</c>: the part after the first colon (the second piece), else the value.</summary>
	internal static string LocalPart(string value) =>
		value is not null && value.Contains(':') ? value.Split(':')[1] : value;

	/// <summary>
	/// <c>getPrefix</c>: the namespace URI the value's prefix is bound to at the element, empty for a value without a
	/// prefix, null for an undeclared prefix.
	/// </summary>
	private static string NamespaceOf(string value, XElement context) {
		if (context is null) {
			return string.Empty;
		}
		string prefix = value is not null && value.Contains(':') ? value.Split(':')[0] : string.Empty;
		return prefix.Length > 0 ? context.GetNamespaceOfPrefix(prefix)?.NamespaceName : string.Empty;
	}

	#endregion

	/// <summary>
	/// <c>WsdlMergeService.mergeWsdl</c>: every <c>types &gt; schema &gt; import</c> and <c>include</c> of the WSDL is
	/// replaced by the schema elements of the file its <c>schemaLocation</c> names (that file's own imports first), each
	/// file once, inserted before the importing schema.
	/// </summary>
	private sealed class WsdlMerge(IReadOnlyDictionary<string, XDocument> documents) {
		private readonly HashSet<string> _merged = new(StringComparer.Ordinal);

		public XDocument Merge(string wsdlKey) {
			var wsdl = new XDocument(documents[wsdlKey]);
			MergeSchemas(wsdl, "import");
			MergeSchemas(wsdl, "include");
			return wsdl;
		}

		private void MergeSchemas(XDocument wsdl, string directive) {
			XElement[] nodes = wsdl.Descendants().Where(e => Is(e, directive) && Is(e.Parent, "schema")
				&& Is(e.Parent.Parent, "types") && Is(e.Parent.Parent.Parent, "definitions")).ToArray();
			foreach (XElement node in nodes) {
				IReadOnlyList<XElement> schemas = SchemaNodes(Attr(node, "schemaLocation"));
				XElement parentSchema = node.Parent;
				foreach (XElement schema in schemas) {
					parentSchema.AddBeforeSelf(schema);
				}
				node.Remove();
			}
		}

		private List<XElement> SchemaNodes(string location) {
			if (string.IsNullOrEmpty(location) || !_merged.Add(location)) {
				return [];
			}
			if (!TryResolve(location, out XDocument source)) {
				throw new ArgumentException($"The WSDL imports '{location}', which was not given. In wsdl-files mode, add "
					+ "that schema file to wsdl-files (it is matched by its schemaLocation or its file name).");
			}
			var document = new XDocument(source);
			var result = new List<XElement>();
			result.AddRange(Inserted(document, "import"));
			result.AddRange(Inserted(document, "include"));
			result.AddRange(document.Descendants().Where(e => Is(e, "schema")).Select(Detached));
			return result;
		}

		// The platform keys a downloaded schema by its schemaLocation as written; a local file is also found by its file
		// name, so an import of http://host/types.xsd finds a given types.xsd.
		private bool TryResolve(string location, out XDocument document) {
			if (documents.TryGetValue(location, out document)) {
				return true;
			}
			string name = FileName(location);
			XDocument[] matches = documents.Where(d => string.Equals(FileName(d.Key), name, StringComparison.OrdinalIgnoreCase))
				.Select(d => d.Value).ToArray();
			document = matches.Length == 1 ? matches[0] : null;
			return document is not null;
		}

		private static string FileName(string location) {
			string path = location.Split('?', '#')[0];
			return path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
		}

		private List<XElement> Inserted(XDocument document, string directive) {
			var result = new List<XElement>();
			foreach (XElement node in document.Descendants().Where(e => Is(e, directive) && Is(e.Parent, "schema")).ToArray()) {
				result.AddRange(SchemaNodes(Attr(node, "schemaLocation")));
				node.Remove();
			}
			return result;
		}

		// A schema moved into the WSDL keeps the prefixes its old ancestors declared, so its QName values still resolve.
		private static XElement Detached(XElement schema) {
			var copy = new XElement(schema);
			for (XElement ancestor = schema.Parent; ancestor is not null; ancestor = ancestor.Parent) {
				foreach (XAttribute declaration in ancestor.Attributes().Where(a => a.IsNamespaceDeclaration)) {
					if (copy.Attribute(declaration.Name) is null) {
						copy.Add(new XAttribute(declaration));
					}
				}
			}
			return copy;
		}
	}

	/// <summary>An element of a message or a type while it is read (<c>WsdlElement</c>).</summary>
	private sealed class Node {
		public string TypeName;
		public string ReferenceTypeName;
		public string Caption;
		public string Path;
		public string Type;
		public bool IsArray;
		public bool IsSoapHeader;
		public string SequenceElement;
		public string Namespace;
		public List<XElement> Elements = [];
		public HashSet<XElement> Copies = [];
		public List<Node> Nested = [];
		public Node Parent;
		public bool IsCopy;
		public bool IsObject => Type == "Object";
		public bool InCopy => IsCopy || (Parent?.InCopy ?? false);
	}

	/// <summary>A parameter while its caption and code are settled (<c>ServiceParameter</c>).</summary>
	private sealed class Parameter {
		public string Caption;
		public string Path;
		public string Type;
		public bool IsArray;
		public bool IsSoapHeader;
		public string SequenceElement;
		public List<Parameter> Items = [];
		public string Code;
		public bool IsCopy;

		public WsdlParameter ToResult() => new(Caption, Path, Type, IsArray, IsSoapHeader, SequenceElement,
			Items.Select(i => i.ToResult()).ToList(), Code, IsCopy);
	}

	/// <summary>One parse of one merged document: indexes, the per-port namespace list and the warnings.</summary>
	private sealed class Session {
		private readonly XDocument _doc;
		private readonly string _prefix;
		private readonly List<string> _warnings;
		private readonly HashSet<string> _warningSet = new(StringComparer.Ordinal);
		private readonly Dictionary<string, XElement> _globalElements = new(StringComparer.Ordinal);
		private readonly Dictionary<string, XElement> _complexTypes = new(StringComparer.Ordinal);
		private readonly Dictionary<string, XElement> _simpleTypes = new(StringComparer.Ordinal);
		private readonly Dictionary<string, List<XElement>> _definitionComplexTypes = new(StringComparer.Ordinal);
		private List<WsdlNamespace> _namespaces;
		private string _operation;
		private int _elementsRead;

		public Session(XDocument doc, string prefix, List<string> warnings) {
			_doc = doc;
			_prefix = prefix;
			_warnings = warnings;
			foreach (XElement element in doc.Descendants()) {
				string name = Attr(element, "name");
				if (name is null || !Is(element.Parent, "schema") || !Is(element.Parent.Parent, "types")) {
					continue;
				}
				bool underDefinitions = Is(element.Parent.Parent.Parent, "definitions");
				if (Is(element, "element") && underDefinitions) {
					_globalElements.TryAdd(name, element);
				} else if (Is(element, "complexType")) {
					_complexTypes.TryAdd(name, element);
					if (underDefinitions) {
						(_definitionComplexTypes.TryGetValue(name, out List<XElement> list)
							? list
							: _definitionComplexTypes[name] = []).Add(element);
					}
				} else if (Is(element, "simpleType")) {
					_simpleTypes.TryAdd(name, element);
				}
			}
		}

		private void Warn(string message) {
			if (_warningSet.Add(message)) {
				_warnings.Add(message);
			}
		}

		private string Where => _operation is null ? string.Empty : $"operation '{_operation}': ";

		public WsdlDocument Build() {
			XElement definitions = _doc.Root;
			ReportUnsupported(definitions);
			var ports = new List<WsdlPort>();
			foreach (XElement service in definitions.Elements().Where(e => Is(e, "service"))) {
				foreach (XElement port in service.Elements().Where(e => Is(e, "port"))) {
					WsdlPort result = BuildPort(definitions, service, port);
					if (result is not null) {
						ports.Add(result);
					}
				}
			}
			string studioPort = (ports.FirstOrDefault(p => p.ServiceType == ServiceSchemaCodes.ServiceTypeSoap12)
				?? ports.FirstOrDefault())?.Name;
			return new WsdlDocument(ports, studioPort, _warnings);
		}

		private void ReportUnsupported(XElement definitions) {
			foreach (XElement import in definitions.Elements().Where(e => Is(e, "import"))) {
				Warn($"The WSDL imports another WSDL ('{Attr(import, "location") ?? Attr(import, "namespace")}'); Studio's "
					+ "wizard and the platform download do not follow a wsdl:import, so the messages, port types and "
					+ "bindings it holds are not read.");
			}
			if (definitions.Descendants().Any(e => e.Name.NamespaceName == MimeNamespace
					|| e.Name.LocalName == "OptimizedMimeSerialization")) {
				Warn("The WSDL uses MIME attachments or MTOM; the platform sends a plain SOAP envelope, so binary content "
					+ "goes inline as base64 text, which such an API may refuse.");
			}
		}

		private WsdlPort BuildPort(XElement definitions, XElement service, XElement port) {
			string portName = Attr(port, "name");
			XElement address = port.Descendants().FirstOrDefault(e => Is(e, "address"));
			int serviceType = address?.Name.NamespaceName switch {
				Soap11BindingNamespace => ServiceSchemaCodes.ServiceTypeSoap11,
				Soap12BindingNamespace => ServiceSchemaCodes.ServiceTypeSoap12,
				_ => 0
			};
			if (serviceType == 0) {
				Warn($"Port '{portName}' is not a SOAP 1.1 or SOAP 1.2 port (HTTP GET/POST or another binding); it was "
					+ "dropped, as Studio's quick setup does.");
				return null;
			}
			string bindingName = LocalPart(Attr(port, "binding"));
			XElement binding = definitions.Elements().FirstOrDefault(e => Is(e, "binding") && Attr(e, "name") == bindingName);
			if (binding is null) {
				Warn($"Port '{portName}': its binding '{bindingName}' is not in the WSDL; the port was dropped.");
				return null;
			}
			ReportBindingStyle(portName, binding);
			string portTypeName = LocalPart(Attr(binding, "type"));
			XElement[] operations = definitions.Elements()
				.Where(e => Is(e, "portType") && Attr(e, "name") == portTypeName)
				.SelectMany(e => e.Elements().Where(o => Is(o, "operation")))
				.ToArray();
			_namespaces = [new WsdlNamespace("tem", Attr(definitions, "targetNamespace") ?? "http://tempuri.org")];
			var built = new List<(WsdlOperation Operation, string Namespace)>();
			foreach (XElement operation in operations) {
				WsdlOperation method = BuildOperation(binding, operation);
				if (method is not null) {
					built.Add((method, method.MessageElementNamespace));
				}
			}
			_operation = null;
			// The message element's prefix is settled after every parameter, so it adds a namespace only after the ones
			// the wizard adds and the ns0..nN numbering stays the wizard's.
			foreach ((WsdlOperation _, string ns) in built) {
				if (ns is not null) {
					PrefixFor(ns);
				}
			}
			string caption = Attr(service, "name");
			return new WsdlPort(portName, caption, serviceType, Attr(address, "location"), _namespaces.ToList(),
				built.Select(b => b.Operation).ToList());
		}

		private void ReportBindingStyle(string portName, XElement binding) {
			bool rpc = binding.Descendants().Any(e => (Is(e, "binding") || Is(e, "operation"))
				&& e.Name.NamespaceName is Soap11BindingNamespace or Soap12BindingNamespace && Attr(e, "style") == "rpc");
			bool encoded = binding.Descendants().Any(e => Is(e, "body") && Attr(e, "use") == "encoded");
			if (encoded) {
				Warn($"Port '{portName}' uses RPC/encoded messages, which Studio's wizard and the platform do not support: "
					+ "no xsi:type is ever sent. Test a call before relying on it.");
			} else if (rpc) {
				Warn($"Port '{portName}' uses the rpc style; its parameters were read with the wizard's document rules. "
					+ "Test a call before relying on the element paths.");
			}
		}

		#region Operation (SoapWebServiceMethodBuilder)

		private WsdlOperation BuildOperation(XElement binding, XElement operation) {
			string caption = Attr(operation, "name");
			_operation = caption;
			XElement input = FirstChild(operation, "input");
			if (input is null) {
				Warn($"Operation '{caption}' has no input message (a notification operation); it was skipped.");
				return null;
			}
			string studioCode = PrefixedName(caption);
			XElement bindingOperation = binding.Descendants()
				.FirstOrDefault(e => Is(e, "operation") && Is(e.Parent, "operation") && Attr(e.Parent, "name") == caption);
			string soapAction = Attr(bindingOperation, "soapAction");
			(string rootElement, string rootNamespace) = RootElement(input, binding, caption);
			List<Parameter> body = ParametersForMessage(input, fromRoot: false);
			List<Parameter> header = HeaderParameters(binding, caption);
			XElement output = FirstChild(operation, "output");
			List<Parameter> response = output is null ? [] : ParametersForMessage(output, fromRoot: false);
			return new WsdlOperation(caption, studioCode, soapAction, rootElement ?? caption, rootNamespace,
				[.. body.Concat(header).Select(p => p.ToResult())], [.. response.Select(p => p.ToResult())]);
		}

		// _getRootElementCode: the local name of the first part's element. The namespace is clio's addition: Studio stores
		// the bare name, which the platform qualifies with the default namespace.
		private (string Name, string Namespace) RootElement(XElement input, XElement binding, string operation) {
			XElement part = MessageParts(LocalPart(Attr(input, "message"))).FirstOrDefault();
			string element = Attr(part, "element");
			if (element is not null) {
				return (LocalPart(element), NamespaceOf(element, part));
			}
			// A typed part (rpc style): the wrapper is the operation name in the soap:body namespace.
			XElement body = binding.Descendants().FirstOrDefault(e => Is(e, "body") && Is(e.Parent, "input")
				&& Is(e.Parent.Parent, "operation") && Attr(e.Parent.Parent, "name") == operation);
			return (null, Attr(body, "namespace") ?? _namespaces[0].Uri);
		}

		private IEnumerable<XElement> MessageParts(string messageName) =>
			_doc.Root.Elements().Where(e => Is(e, "message") && Attr(e, "name") == messageName)
				.SelectMany(m => m.Elements().Where(p => Is(p, "part")));

		private List<Parameter> HeaderParameters(XElement binding, string operation) {
			XElement[] headers = binding.Descendants().Where(e => Is(e, "header") && Is(e.Parent, "input")
				&& Is(e.Parent.Parent, "operation") && Attr(e.Parent.Parent, "name") == operation).ToArray();
			if (headers.Length == 0) {
				return [];
			}
			if (headers.Length > 1) {
				Warn($"{Where}its binding input has {headers.Length} SOAP headers; Studio's wizard reads only the first "
					+ $"('{LocalPart(Attr(headers[0], "message"))}'). Add the others by hand if the API needs them.");
			}
			List<Parameter> parameters = ParametersForMessage(headers[0], fromRoot: true);
			MarkAsHeader(parameters);
			return parameters;
		}

		private static void MarkAsHeader(IEnumerable<Parameter> parameters) {
			foreach (Parameter parameter in parameters) {
				parameter.IsSoapHeader = true;
				MarkAsHeader(parameter.Items);
			}
		}

		private List<Parameter> ParametersForMessage(XElement node, bool fromRoot) {
			string messageName = LocalPart(Attr(node, "message"));
			var parameters = new List<Parameter>();
			foreach (XElement part in MessageParts(messageName)) {
				string element = Attr(part, "element");
				if (element is null) {
					string partType = Attr(part, "type");
					parameters.AddRange(ParametersOf(part, NamespaceOf(partType, part)));
					continue;
				}
				parameters.AddRange(ParametersByElementName(LocalPart(element), NamespaceOf(element, part), fromRoot));
			}
			NormalizeCaptions(parameters);
			GenerateStudioCodes(parameters, string.Empty, new HashSet<string>(StringComparer.Ordinal));
			return parameters;
		}

		private List<Parameter> ParametersByElementName(string elementName, string namespaceUri, bool fromRoot) {
			if (!_globalElements.TryGetValue(elementName ?? string.Empty, out XElement elementNode)) {
				Warn($"{Where}its message element '{elementName}' is not defined in the WSDL or its schemas; it has no "
					+ "parameters.");
				return [];
			}
			if (fromRoot) {
				return ParametersOf(elementNode, namespaceUri, elementName);
			}
			string complexTypeName = Attr(elementNode, "type");
			IEnumerable<XElement> sequence;
			if (complexTypeName is not null) {
				sequence = (_definitionComplexTypes.TryGetValue(LocalPart(complexTypeName), out List<XElement> types)
						? types
						: [])
					.SelectMany(t => t.Elements().Where(s => Is(s, "sequence")))
					.SelectMany(s => s.Elements().Where(e => Is(e, "element")));
				if (!sequence.Any() && XsdTypes.ContainsKey(LocalPart(complexTypeName))) {
					Warn($"{Where}its message element '{elementName}' has the simple type '{complexTypeName}'; Studio's "
						+ "wizard reads no parameter from it. Add one by hand if the value is needed.");
				}
			} else {
				sequence = elementNode.Elements().Where(c => Is(c, "complexType"))
					.SelectMany(c => c.Elements().Where(s => Is(s, "sequence")))
					.SelectMany(s => s.Elements().Where(e => Is(e, "element")));
			}
			var parameters = new List<Parameter>();
			foreach (XElement element in sequence.ToArray()) {
				parameters.AddRange(ParametersOf(element, namespaceUri, elementName));
			}
			return parameters;
		}

		private List<Parameter> ParametersOf(XElement sequenceElement, string parentNamespace, string rootElementName = null) {
			Node node = NodeOf(sequenceElement, parentNamespace, new Dictionary<string, int>(StringComparer.Ordinal));
			return node is null ? [] : Flatten(node, rootElementName).Select(ToParameter).ToList();
		}

		#endregion

		#region Elements and types (WsdlElement, XmlSchemaTypeUtils)

		// _getWsdlElementFromSequenceElement
		private Node NodeOf(XElement sequenceElement, string parentNamespace, Dictionary<string, int> nesting) {
			if (++_elementsRead > MaxElementsRead) {
				throw new ArgumentException($"The WSDL's types expand to more than {MaxElementsRead} elements (types that "
					+ "nest many fields of other types); it cannot be read. Describe the operations you need by hand.");
			}
			Node node = FromElement(sequenceElement);
			if (node is null) {
				return null;
			}
			node.SequenceElement = PrefixFor(parentNamespace) + ":" + node.TypeName;
			if (!node.IsObject) {
				node.Namespace = parentNamespace;
				return node;
			}
			return ComplexType(node, parentNamespace, new Dictionary<string, int>(nesting, StringComparer.Ordinal));
		}

		// _getComplexType: a type may appear twice on one path; the third time the element is not read. Every anonymous
		// type counts as one type, as in the wizard.
		private Node ComplexType(Node node, string parentNamespace, Dictionary<string, int> nesting) {
			string key = node.ReferenceTypeName ?? "\0anonymous";
			nesting.TryGetValue(key, out int count);
			if (count == 2) {
				Warn($"{Where}'{node.TypeName}' was not read: Studio's wizard stops after two levels of the same type "
					+ "(a recursive type, or anonymous types nested three deep).");
				return null;
			}
			nesting[key] = count + 1;
			string ns = string.IsNullOrEmpty(node.Namespace) ? parentNamespace : node.Namespace;
			HashSet<XElement> copies = node.Copies;
			foreach (XElement element in node.Elements) {
				Node child = NodeOf(element, ns, nesting);
				if (child is not null) {
					child.IsCopy = copies.Contains(element);
					node.Nested.Add(child);
				}
			}
			node.Namespace = parentNamespace;
			return node;
		}

		// WsdlElement.fromElement. A base type that extends itself, directly or through others, ends the chain with a
		// warning where the wizard would recurse until the stack runs out.
		private Node FromElement(XElement elementRef, HashSet<XElement> resolvingBases = null) {
			XElement definition = ElementDefinition(elementRef);
			if (Attr(definition, "name") is null) {
				return null;
			}
			string typeAttribute = Attr(definition, "type");
			string type = DataValueType(definition);
			var node = new Node {
				TypeName = Attr(definition, "name"),
				ReferenceTypeName = LocalPart(typeAttribute),
				Caption = Attr(definition, "name"),
				Path = Attr(definition, "name"),
				Type = type ?? "Object",
				IsArray = Attr(definition, "maxOccurs") == "unbounded",
				Namespace = NamespaceOf(typeAttribute, definition)
			};
			if (!node.IsObject) {
				return node;
			}
			if (IsExtensionType(definition)) {
				(string baseTypeName, XElement baseType, List<XElement> elements) = ExtensionInfo(definition);
				if (XsdTypes.TryGetValue(baseTypeName ?? string.Empty, out string baseSimple)) {
					node.Type = baseSimple;
					return node;
				}
				if (baseType is null) {
					Warn($"{Where}the base type '{baseTypeName}' of '{node.TypeName}' is not defined in the WSDL or its "
						+ "schemas; only the elements the extension adds were read.");
					node.Elements = elements;
					node.Copies = Copies(elements, ExtensionOf(TypeDefinition(definition)));
					return node;
				}
				resolvingBases ??= [];
				if (!resolvingBases.Add(baseType) || resolvingBases.Count > MaxTypeChain) {
					Warn($"{Where}the base type '{baseTypeName}' of '{node.TypeName}' extends itself; only the elements read "
						+ "before the cycle were kept.");
					node.Elements = elements;
					node.Copies = Copies(elements, ExtensionOf(TypeDefinition(definition)));
					return node;
				}
				Node baseNode = FromElement(baseType, resolvingBases);
				node.Elements = [.. elements, .. baseNode?.Elements ?? []];
				node.Copies = [.. Copies(elements, ExtensionOf(TypeDefinition(definition))), .. baseNode?.Copies ?? []];
			} else {
				node.Elements = ComplexTypeChildElements(definition);
				node.Copies = Copies(node.Elements, TypeDefinition(definition));
			}
			return node;
		}

		// getElementDefinition: a ref resolves to the global element, which takes the referencing element's maxOccurs.
		private XElement ElementDefinition(XElement elementRef) {
			string reference = Attr(elementRef, "ref");
			if (reference is null) {
				return elementRef;
			}
			if (!_globalElements.TryGetValue(LocalPart(reference), out XElement target)) {
				Warn($"{Where}the element reference '{reference}' is not defined in the WSDL or its schemas; it was not read.");
				return elementRef;
			}
			target.SetAttributeValue("maxOccurs", Attr(elementRef, "maxOccurs"));
			return target;
		}

		// getDataValeTypeName, plus a read of a type no schema defines: the wizard fails there; clio reads it as Text.
		private string DataValueType(XElement element) {
			string typeAttribute = Attr(element, "type");
			string xmlTypeName = LocalPart(typeAttribute);
			if (string.IsNullOrEmpty(xmlTypeName)) {
				xmlTypeName = SimpleTypeName(element);
			}
			if (XsdTypes.TryGetValue(xmlTypeName ?? string.Empty, out string mapped)) {
				return mapped;
			}
			XElement definition = TypeDefinition(element);
			if (definition is null) {
				if (!string.IsNullOrEmpty(typeAttribute)) {
					Warn($"{Where}element '{Attr(element, "name")}' has the type '{typeAttribute}', which "
						+ (NamespaceOf(typeAttribute, element) == XmlSchemaNamespace
							? "Studio's wizard does not map"
							: "is not defined in the WSDL or its schemas")
						+ "; it was read as Text.");
					return "Text";
				}
				return null;
			}
			if (IsRestriction(definition)) {
				xmlTypeName = SimpleTypeName(definition);
			}
			if (IsMixedText(definition)) {
				return "Text";
			}
			return XsdTypes.TryGetValue(xmlTypeName ?? string.Empty, out mapped) ? mapped : null;
		}

		// _getTypeDefinitionElement: an element with a type attribute resolves to that named complex or simple type. A type
		// that names a type (not valid XSD, but nothing stops it) is followed MaxTypeChain steps at most, so a cycle ends.
		private XElement TypeDefinition(XElement element, int depth = 0) {
			if (element is null || depth > MaxTypeChain) {
				return null;
			}
			string typeAttribute = Attr(element, "type");
			return string.IsNullOrEmpty(typeAttribute) ? element : TypeByName(LocalPart(typeAttribute), depth + 1);
		}

		// _getTypeElementByName: types > schema > complexType[name], else simpleType[name].
		private XElement TypeByName(string name, int depth = 0) {
			if (name is null) {
				return null;
			}
			XElement type = _complexTypes.GetValueOrDefault(name) ?? _simpleTypes.GetValueOrDefault(name);
			return type is null ? null : TypeDefinition(type, depth);
		}

		// _getIsRestrictionElement
		private static bool IsRestriction(XElement element) {
			if (element is null) {
				return false;
			}
			XElement simpleType = Is(element, "simpleType")
				? element
				: element.Descendants().FirstOrDefault(e => Is(e, "simpleType") && !Is(e.Parent, "element"));
			return simpleType is not null && simpleType.Descendants().Any(e => Is(e, "restriction")
				&& !(Is(e.Parent, "simpleType") && Is(e.Parent.Parent, "element")));
		}

		// _isMixedTextElement
		private static bool IsMixedText(XElement complexType) =>
			Attr(complexType, "mixed") == "true" && complexType.Elements().All(c => Is(c, "anyAttribute"));

		// getSimpleTypeName: the restriction base of the first simpleType inside (or of the element itself when it is
		// one), "string" for a simpleType without a restriction, empty when there is none.
		private static string SimpleTypeName(XElement element) {
			if (element is null) {
				return string.Empty;
			}
			XElement simpleType = Is(element, "simpleType")
				? element
				: element.Descendants().FirstOrDefault(e => Is(e, "simpleType"));
			if (simpleType is null) {
				return string.Empty;
			}
			XElement restriction = simpleType.Descendants().FirstOrDefault(e => Is(e, "restriction"));
			return restriction is null ? "string" : LocalPart(Attr(restriction, "base"));
		}

		// getIsExtensionType
		private bool IsExtensionType(XElement element) {
			XElement definition = TypeDefinition(element);
			return definition is not null && definition.Descendants().Any(e => Is(e, "extension")
				&& (Is(e.Parent, "complexContent") || Is(e.Parent, "simpleContent")) && Is(e.Parent.Parent, "complexType"));
		}

		// getExtensionInfo
		private (string BaseTypeName, XElement BaseType, List<XElement> Elements) ExtensionInfo(XElement element) {
			XElement definition = TypeDefinition(element);
			XElement extension = ExtensionOf(definition);
			string baseXmlTypeName = LocalPart(Attr(extension, "base"));
			XElement baseType = TypeByName(baseXmlTypeName);
			string simpleTypeName = SimpleTypeName(baseType);
			List<XElement> elements = extension.Descendants()
				.Where(e => Is(e, "element") && Is(e.Parent, "sequence") && Is(e.Parent.Parent, "extension")).ToList();
			return (string.IsNullOrEmpty(simpleTypeName) ? baseXmlTypeName : simpleTypeName, baseType, elements);
		}

		// getComplexTypeChildElements: every element under a sequence anywhere inside the type, as the wizard's
		// querySelectorAll finds them. That includes the elements of an anonymous type nested inside, which so turn up a
		// second time one level too high (Fault/faultNumber next to Fault/detail/faultNumber); they are kept for parity
		// and marked, and WsdlRegistrationMapper leaves them out.
		private List<XElement> ComplexTypeChildElements(XElement element) {
			XElement definition = TypeDefinition(element);
			if (definition is null) {
				return [];
			}
			bool isComplexType = Is(definition, "complexType");
			ReportUnreadContent(Attr(element, "name"), definition);
			return definition.Descendants()
				.Where(e => Is(e, "element") && Is(e.Parent, "sequence") && (isComplexType || Is(e.Parent.Parent, "complexType")))
				.ToList();
		}

		// "complexType extension": the first extension inside the type definition.
		private static XElement ExtensionOf(XElement definition) =>
			definition.Descendants().First(e => Is(e, "extension") && e.Ancestors().Any(a => Is(a, "complexType")));

		// An element that sits inside another element of the type is read twice by the wizard: once there, and once here
		// as a copy.
		private static HashSet<XElement> Copies(IEnumerable<XElement> elements, XElement root) =>
			root is null ? [] : [.. elements.Where(e => IsInsideAnotherElement(e, root))];

		private static bool IsInsideAnotherElement(XElement element, XElement root) {
			for (XElement ancestor = element.Parent; ancestor is not null && ancestor != root; ancestor = ancestor.Parent) {
				if (Is(ancestor, "element")) {
					return true;
				}
			}
			return false;
		}

		// The wizard reads the elements of an xs:sequence only; the platform sends no XML attribute.
		private void ReportUnreadContent(string name, XElement definition) {
			if (definition.Descendants().Any(e => Is(e, "choice") || Is(e, "all"))) {
				Warn($"{Where}'{name}' uses xs:choice or xs:all; Studio's wizard reads only the elements of an xs:sequence, "
					+ "so the others are missing. Add them by hand if the API needs them.");
			}
			if (definition.Descendants().Any(e => Is(e, "attribute") || Is(e, "attributeGroup"))) {
				Warn($"{Where}'{name}' has XML attributes; the platform sends and reads elements only, so they are not "
					+ "parameters.");
			}
		}

		#endregion

		#region Parameters (_flattenWsdlElement, toServiceParameter)

		private List<Node> Flatten(Node node, string rootElementName = null) {
			if (node.Nested.Count == 0) {
				return [node];
			}
			if (node.IsArray && node.IsObject) {
				Node holder = node.Parent ?? node;
				var items = node.Nested.SelectMany(item => {
					item.Parent = node;
					return Flatten(item);
				}).ToList();
				return [new Node {
					Caption = holder.Caption,
					Path = string.IsNullOrEmpty(rootElementName) ? holder.Path : rootElementName,
					Type = holder.Type,
					IsArray = true,
					IsSoapHeader = node.IsSoapHeader,
					SequenceElement = node.SequenceElement,
					Namespace = holder.Namespace,
					Nested = items,
					Parent = holder.Parent,
					IsCopy = node.InCopy || holder.InCopy
				}];
			}
			return node.Nested.SelectMany(child => {
				child.Parent = node;
				return Flatten(child);
			}).ToList();
		}

		private Parameter ToParameter(Node node) => new() {
			Caption = BuildCaption(node),
			Path = BuildPath(node),
			Type = node.Type,
			IsArray = node.IsArray,
			IsSoapHeader = node.IsSoapHeader,
			SequenceElement = node.SequenceElement,
			Items = node.Nested.Select(ToParameter).ToList(),
			IsCopy = node.InCopy
		};

		private string BuildPath(Node node) {
			string prefix = PrefixFor(node.Namespace);
			string path = node.IsArray && !node.IsObject ? string.Empty : $"{prefix}:{node.Path}";
			for (Node parent = node.Parent; parent is not null && !parent.IsArray; parent = parent.Parent) {
				string parentPrefix = PrefixFor(parent.Namespace);
				path = $"{parentPrefix}:{parent.Path}" + (path.Length > 0 ? "/" + path : string.Empty);
			}
			return path;
		}

		private static string BuildCaption(Node node) {
			string caption = node.IsArray && !node.IsObject ? string.Empty : node.Path;
			for (Node parent = node.Parent; parent is not null && !parent.IsArray; parent = parent.Parent) {
				caption = parent.Path + (caption.Length > 0 ? " " + caption : string.Empty);
			}
			return caption;
		}

		// WsdlElement.getPrefixForNamespace: an unknown namespace is added as the first free ns0..ns999.
		private string PrefixFor(string ns) {
			WsdlNamespace existing = _namespaces.FirstOrDefault(n => n.Uri == ns);
			if (existing is not null) {
				return existing.Prefix;
			}
			string prefix = Enumerable.Range(0, 1000).Select(i => "ns" + i)
				.First(p => _namespaces.All(n => n.Prefix != p));
			_namespaces.Add(new WsdlNamespace(prefix, ns));
			return prefix;
		}

		// _normalizeParametersCaption: when every top-level caption has two words or more and the same first word, the
		// first word goes.
		private static void NormalizeCaptions(List<Parameter> parameters) {
			if (parameters.Count == 0) {
				return;
			}
			string first = parameters[0].Caption.Split(' ')[0];
			bool common = parameters.All(p => {
				string[] parts = p.Caption.Split(' ');
				return parts.Length > 1 && parts[0] == first;
			});
			if (!common) {
				return;
			}
			foreach (Parameter parameter in parameters) {
				parameter.Caption = string.Join(' ', parameter.Caption.Split(' ').Skip(1));
			}
		}

		#endregion

		#region Studio codes (for parity; clio's own codes come from WsdlRegistrationMapper)

		private void GenerateStudioCodes(List<Parameter> parameters, string parentName, HashSet<string> taken) {
			foreach (Parameter parameter in parameters) {
				string local = PrefixedName(parameter.Caption, parentName);
				string code = local;
				for (int index = 1; taken.Contains(code); index++) {
					code = Truncate(local + index);
				}
				taken.Add(code);
				parameter.Code = code;
				GenerateStudioCodes(parameter.Items, parameter.Caption, taken);
			}
		}

		private string PrefixedName(string name, string parentName = "") =>
			Truncate(_prefix + ShortenCaption((parentName + name).Replace(" ", string.Empty)));

		#endregion
	}

	/// <summary>
	/// The wizard's code shortening (<c>_reduceByUpperCaseChars</c>): the capitalized words of the name, every one but
	/// the last cut to its first letter (<c>CustomerName</c> → <c>CName</c>); a name without one is kept.
	/// </summary>
	/// <param name="name">The name.</param>
	/// <returns>The shortened name.</returns>
	internal static string ShortenCaption(string name) {
		string[] words = CapitalizedWordPattern().Matches(name ?? string.Empty).Select(m => m.Value).ToArray();
		return words.Length == 0
			? name
			: string.Concat(words.Select((word, index) => index == words.Length - 1 ? word : word[..1]));
	}

	/// <summary>The wizard's 200-character cap, which keeps the end of the name.</summary>
	internal static string Truncate(string name) =>
		name.Length <= MaxCodeLength ? name : name.Substring(name.Length - MaxCodeLength, MaxCodeLength);
}
