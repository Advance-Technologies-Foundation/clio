using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
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
/// <param name="Uri">The namespace URI; empty for an element without a namespace, null for a prefix the WSDL never declares.</param>
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
/// <param name="EscapesItem">
/// Whether it is an object array inside an item of another array, to which the wizard gives the outer array's path
/// (<c>tem:order/tem:lines</c> for the <c>subs</c> of each <c>lines</c> item), so the platform would nest it wrongly.
/// </param>
public sealed record WsdlParameter(string Caption, string Path, string Type, bool IsArray, bool IsSoapHeader,
	string SequenceElement, IReadOnlyList<WsdlParameter> Items, string StudioCode, bool IsCopy = false,
	bool EscapesItem = false);

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
/// <c>XmlSchemaTypeUtils</c>, <c>WsdlMergeService</c> and its XSD type map), so a service registered from its output
/// matches the one Studio's SOAP quick setup creates.
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

	/// <summary>
	/// The deepest XML nesting a file may have. Copying an <see cref="XDocument"/> recurses once per level, so a file
	/// nested ten thousand deep would end the whole server with a stack overflow nothing can catch.
	/// </summary>
	internal const int MaxXmlDepth = 256;

	/// <summary>The deepest an element may sit below a message part; deeper elements are not read.</summary>
	internal const int MaxElementDepth = 64;

	/// <summary>The most warnings one parse reports; the rest are counted.</summary>
	internal const int MaxWarnings = 100;

	/// <summary>
	/// A name or value taken from the WSDL as it may appear in a warning or an error: line breaks and other control or
	/// format characters become spaces and the text is cut, so a hostile WSDL cannot pass an instruction block to the
	/// agent through a warning.
	/// </summary>
	/// <param name="value">The raw text.</param>
	/// <returns>The text to show.</returns>
	internal static string Shown(string value) {
		if (string.IsNullOrEmpty(value)) {
			return string.Empty;
		}
		var builder = new StringBuilder(Math.Min(value.Length, 101));
		foreach (char c in value) {
			bool blank = char.IsControl(c) || char.IsWhiteSpace(c)
				|| char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format;
			if (blank && (builder.Length == 0 || builder[^1] == ' ')) {
				continue;
			}
			builder.Append(blank ? ' ' : c);
			if (builder.Length >= 100) {
				return builder.ToString().TrimEnd() + "…";
			}
		}
		return builder.ToString().TrimEnd();
	}

	/// <summary>Whether a WSDL or XSD name is an NCName, the type WSDL 1.1 and XSD give every name.</summary>
	internal static bool IsNCName(string value) {
		if (string.IsNullOrEmpty(value) || !XmlConvert.IsStartNCNameChar(value[0])) {
			return false;
		}
		for (int i = 1; i < value.Length; i++) {
			if (!XmlConvert.IsNCNameChar(value[i])) {
				return false;
			}
		}
		return true;
	}

	/// <summary>The wizard's XSD type map.</summary>
	internal static readonly IReadOnlyDictionary<string, string> XsdTypes = new Dictionary<string, string>(StringComparer.Ordinal) {
		["string"] = TextType, ["boolean"] = "Boolean", ["decimal"] = FloatType, ["float"] = FloatType,
		["double"] = FloatType, ["dateTime"] = "DateTime", ["time"] = "Time", ["date"] = "Date", ["hexBinary"] = TextType,
		["base64Binary"] = TextType, ["integer"] = IntegerType, ["nonPositiveInteger"] = TextType,
		["negativeInteger"] = TextType, ["long"] = IntegerType, ["int"] = IntegerType, ["short"] = IntegerType,
		["byte"] = IntegerType, ["nonNegativeInteger"] = TextType, ["unsignedLong"] = TextType, ["unsignedInt"] = TextType,
		["unsignedShort"] = IntegerType, ["unsignedByte"] = IntegerType, ["positiveInteger"] = TextType,
		["char"] = IntegerType, ["guid"] = "Guid", ["anyType"] = TextType
	};

	[GeneratedRegex("[A-Z][a-z]+")]
	private static partial Regex CapitalizedWordPattern();

	private const string ElementTag = "element";
	private const string ComplexTypeTag = "complexType";
	private const string SimpleTypeTag = "simpleType";
	private const string SchemaTag = "schema";
	private const string SequenceTag = "sequence";
	private const string MessageTag = "message";
	private const string IntegerType = "Integer";
	private const string TextType = "Text";
	private const string FloatType = "Float";

	// Studio's target namespace when a WSDL declares none: a namespace NAME, never a URL anything connects to.
	private static readonly string DefaultTargetNamespace = Uri.UriSchemeHttp + "://tempuri.org";

	private static readonly char[] QueryOrFragment = ['?', '#'];
	private static readonly char[] PathSeparators = ['/', '\\'];

	// Adds a value to the list a key holds, creating the list on first use.
	private static void AddTo<TKey>(Dictionary<TKey, List<XElement>> map, TKey key, XElement value) {
		if (!map.TryGetValue(key, out List<XElement> list)) {
			list = [];
			map[key] = list;
		}
		list.Add(value);
	}

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
		string name = Shown(FileNameOf(file.Key));
		try {
			// The depth is checked on a first, forward-only pass: loading is iterative, but copying the document later
			// is recursive.
			using (XmlReader probe = CreateReader(file, settings)) {
				while (probe.Read()) {
					if (probe.Depth > MaxXmlDepth) {
						throw new ArgumentException($"'{name}' nests elements more than {MaxXmlDepth} levels deep; it is not a "
							+ "WSDL clio can read.");
					}
				}
			}
			using XmlReader reader = CreateReader(file, settings);
			return XDocument.Load(reader, LoadOptions.None);
		} catch (XmlException exception) {
			bool dtd = exception.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase);
			throw new ArgumentException($"'{name}' is not a readable XML file"
				+ (dtd ? " (a DTD is not allowed)." : $" ({Shown(exception.Message)})."));
		}
	}

	private static XmlReader CreateReader(WsdlSourceFile file, XmlReaderSettings settings) =>
		file.AsciiText
			? XmlReader.Create(new StringReader(Encoding.ASCII.GetString(file.Content ?? [])), settings)
			: XmlReader.Create(new MemoryStream(file.Content ?? []), settings);

	/// <summary>The last segment of a path or URL, without its query: what a file is shown and matched by.</summary>
	internal static string FileNameOf(string key) {
		string path = (key ?? string.Empty).Split(QueryOrFragment)[0];
		return path[(path.LastIndexOfAny(PathSeparators) + 1)..];
	}

	// The wizard takes the file that holds definitions > service; a WSDL 2.0 description has no such element.
	private static string FindWsdl(IReadOnlyDictionary<string, XDocument> documents, List<string> warnings) {
		foreach ((string key, XDocument document) in documents) {
			if (document.Root?.Name.NamespaceName == Wsdl20Namespace) {
				throw new ArgumentException($"'{Shown(FileNameOf(key))}' is a WSDL 2.0 description, which Studio's wizard and the platform "
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
			warnings.Add($"Several files hold a WSDL service; '{Shown(FileNameOf(wsdlKeys[0]))}' was read.");
		}
		return wsdlKeys[0];
	}

	#region DOM helpers (the selector semantics the wizard relies on: names match by local name, any namespace)

	private static bool Is(XElement element, string localName) =>
		element is not null && element.Name.LocalName == localName;

	private static string Attr(XElement element, string name) => element?.Attribute(name)?.Value;

	private static XElement FirstChild(XElement element, string localName) =>
		element?.Elements().FirstOrDefault(e => Is(e, localName));

	/// <summary>The wizard's local name of a QName value: the part after the first colon (the second piece), else the value.</summary>
	internal static string LocalPart(string value) =>
		value is not null && value.Contains(':') ? value.Split(':')[1] : value;

	/// <summary>
	/// The wizard's namespace of a QName value: the URI the value's prefix is bound to at the element, empty for a value
	/// without a prefix, null for an undeclared prefix.
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
	/// <c>WsdlMergeService</c>: every <c>types &gt; schema &gt; import</c> and <c>include</c> of the WSDL is replaced by
	/// the schema elements of the file its <c>schemaLocation</c> names (that file's own imports first), each file once,
	/// inserted before the importing schema.
	/// </summary>
	private sealed class WsdlMerge(IReadOnlyDictionary<string, XDocument> documents) {
		private readonly HashSet<string> _merged = new(StringComparer.Ordinal);
		// Each file is merged once even when it is imported under several locations (a.xsd, ./a.xsd, a.xsd#x), which the
		// file-name match would otherwise follow into a recursion as deep as the number of spellings.
		private readonly HashSet<XDocument> _mergedDocuments = [];

		public XDocument Merge(string wsdlKey) {
			_mergedDocuments.Add(documents[wsdlKey]);
			var wsdl = new XDocument(documents[wsdlKey]);
			MergeSchemas(wsdl, "import", wsdlKey);
			MergeSchemas(wsdl, "include", wsdlKey);
			return wsdl;
		}

		private void MergeSchemas(XDocument wsdl, string directive, string wsdlKey) {
			XElement[] nodes = wsdl.Descendants().Where(e => Is(e, directive) && Is(e.Parent, SchemaTag)
				&& Is(e.Parent.Parent, "types") && Is(e.Parent.Parent.Parent, "definitions")).ToArray();
			foreach (XElement node in nodes) {
				IReadOnlyList<XElement> schemas = SchemaNodes(Attr(node, "schemaLocation"), wsdlKey);
				XElement parentSchema = node.Parent;
				foreach (XElement schema in schemas) {
					parentSchema.AddBeforeSelf(schema);
				}
				node.Remove();
			}
		}

		private List<XElement> SchemaNodes(string location, string importerKey) {
			if (string.IsNullOrEmpty(location) || !_merged.Add(location)) {
				return [];
			}
			(string key, XDocument source) = Resolve(location, importerKey);
			if (!_mergedDocuments.Add(source)) {
				return [];
			}
			var document = new XDocument(source);
			var result = new List<XElement>();
			result.AddRange(Inserted(document, "import", key));
			result.AddRange(Inserted(document, "include", key));
			result.AddRange(document.Descendants().Where(e => Is(e, SchemaTag)).Select(Detached));
			return result;
		}

		// The platform keys a downloaded schema by its schemaLocation as written. A local file is also found by the location
		// resolved against the importing file's folder, by its file name (an import of http://host/types.xsd finds a given
		// types.xsd), and, for a WCF-style location (Service.svc?xsd=xsd0), by the query value (xsd0.xsd).
		private (string Key, XDocument Document) Resolve(string location, string importerKey) {
			if (documents.TryGetValue(location, out XDocument exact)) {
				return (location, exact);
			}
			if (TryRelative(location, importerKey, out string relativeKey)) {
				return (relativeKey, documents[relativeKey]);
			}
			string query = location.Contains('?') ? location[(location.IndexOf('?') + 1)..].Split('#')[0] : string.Empty;
			string queryName = query.Length == 0 ? null : query[(query.LastIndexOf('=') + 1)..];
			string fileName = FileNameOf(location);
			KeyValuePair<string, XDocument>[] matches = documents.Where(d => {
				string name = FileNameOf(d.Key);
				return queryName is null
					? string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)
					: string.Equals(name, queryName, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(name, queryName + ".xsd", StringComparison.OrdinalIgnoreCase);
			}).ToArray();
			if (matches.Length == 1) {
				return (matches[0].Key, matches[0].Value);
			}
			string shown = Shown(location);
			if (matches.Length > 1) {
				throw new ArgumentException($"The WSDL imports '{shown}', and several given files are named "
					+ $"'{Shown(FileNameOf(matches[0].Key))}'; give only the one it means.");
			}
			string queryHint = queryName is null ? "." : $", or as {Shown(queryName)}.xsd for this location.";
			throw new ArgumentException($"The WSDL imports '{shown}', which is not among the given files. Give that schema "
				+ "too: a local file is found by the location relative to the importing file, by its file name" + queryHint);
		}

		private bool TryRelative(string location, string importerKey, out string key) {
			key = null;
			if (location.Contains("://", StringComparison.Ordinal) || importerKey.Contains("://", StringComparison.Ordinal)) {
				return false;
			}
			int slash = importerKey.LastIndexOfAny(PathSeparators);
			if (slash < 0) {
				return false;
			}
			string combined = NormalizePath(importerKey[..(slash + 1)] + location.Split(QueryOrFragment)[0]);
			key = documents.Keys.FirstOrDefault(k => string.Equals(NormalizePath(k), combined, StringComparison.OrdinalIgnoreCase));
			return key is not null;
		}

		// Resolves . and .. segments without touching the file system.
		private static string NormalizePath(string path) {
			var segments = new List<string>();
			foreach (string segment in path.Replace('\\', '/').Split('/')) {
				if (segment == "..") {
					if (segments.Count > 1) {
						segments.RemoveAt(segments.Count - 1);
					}
				} else if (segment != "." && !(segment.Length == 0 && segments.Count > 0)) {
					segments.Add(segment);
				}
			}
			return string.Join('/', segments);
		}

		private List<XElement> Inserted(XDocument document, string directive, string importerKey) {
			var result = new List<XElement>();
			foreach (XElement node in document.Descendants().Where(e => Is(e, directive) && Is(e.Parent, SchemaTag)).ToArray()) {
				result.AddRange(SchemaNodes(Attr(node, "schemaLocation"), importerKey));
				node.Remove();
			}
			return result;
		}

		// A schema moved into the WSDL keeps the prefixes its old ancestors declared, so its QName values still resolve.
		private static XElement Detached(XElement schema) {
			var copy = new XElement(schema);
			for (XElement ancestor = schema.Parent; ancestor is not null; ancestor = ancestor.Parent) {
				foreach (XAttribute declaration in ancestor.Attributes()
						.Where(a => a.IsNamespaceDeclaration && copy.Attribute(a.Name) is null)) {
					copy.Add(new XAttribute(declaration));
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
		public bool EscapesItem;
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
		public bool EscapesItem;

		public WsdlParameter ToResult() => new(Caption, Path, Type, IsArray, IsSoapHeader, SequenceElement,
			Items.Select(i => i.ToResult()).ToList(), Code, IsCopy, EscapesItem);
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
		private readonly Dictionary<string, List<XElement>> _messages = new(StringComparer.Ordinal);
		private Dictionary<string, List<XElement>> _bindingOperations = new(StringComparer.Ordinal);
		private List<WsdlNamespace> _namespaces;
		private string _operation;
		private int _elementsRead;
		private int _warningsDropped;

		public Session(XDocument doc, string prefix, List<string> warnings) {
			_doc = doc;
			_prefix = prefix;
			_warnings = warnings;
			foreach (XElement element in doc.Descendants()) {
				IndexSchemaItem(element);
			}
			foreach (XElement message in doc.Root.Elements().Where(e => Is(e, MessageTag))) {
				AddTo(_messages, Attr(message, "name") ?? string.Empty, message);
			}
		}

		// The wizard's selectors: types > schema > complexType|simpleType[name] anywhere, and global elements and
		// complex types under definitions > types > schema.
		private void IndexSchemaItem(XElement element) {
			string name = Attr(element, "name");
			if (name is null || !Is(element.Parent, SchemaTag) || !Is(element.Parent.Parent, "types")) {
				return;
			}
			bool underDefinitions = Is(element.Parent.Parent.Parent, "definitions");
			if (Is(element, ElementTag)) {
				if (underDefinitions) {
					_globalElements.TryAdd(name, element);
				}
			} else if (Is(element, ComplexTypeTag)) {
				_complexTypes.TryAdd(name, element);
				if (underDefinitions) {
					AddTo(_definitionComplexTypes, name, element);
				}
			} else if (Is(element, SimpleTypeTag)) {
				_simpleTypes.TryAdd(name, element);
			}
		}

		private void Warn(string message) {
			if (!_warningSet.Add(message)) {
				return;
			}
			if (_warnings.Count >= MaxWarnings) {
				_warningsDropped++;
				return;
			}
			_warnings.Add(message);
		}

		private string OperationPrefix => _operation is null ? string.Empty : $"operation '{Shown(_operation)}': ";

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
			if (_warningsDropped > 0) {
				_warnings.Add($"...and {_warningsDropped} more warnings.");
			}
			return new WsdlDocument(ports, studioPort, _warnings);
		}

		private void ReportUnsupported(XElement definitions) {
			foreach (XElement import in definitions.Elements().Where(e => Is(e, "import"))) {
				Warn($"The WSDL imports another WSDL ('{Shown(Attr(import, "location") ?? Attr(import, "namespace"))}'); Studio's "
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
			if (!IsNCName(portName)) {
				Warn($"A port named '{Shown(portName)}' was dropped: WSDL names are XML names (no spaces or punctuation).");
				return null;
			}
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
				Warn($"Port '{portName}': its binding '{Shown(bindingName)}' is not in the WSDL; the port was dropped.");
				return null;
			}
			ReportBindingStyle(portName, binding);
			_bindingOperations = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
			foreach (XElement bindingOperation in binding.Elements().Where(e => Is(e, "operation"))) {
				AddTo(_bindingOperations, Attr(bindingOperation, "name") ?? string.Empty, bindingOperation);
			}
			string portTypeName = LocalPart(Attr(binding, "type"));
			XElement[] operations = definitions.Elements()
				.Where(e => Is(e, "portType") && Attr(e, "name") == portTypeName)
				.SelectMany(e => e.Elements().Where(o => Is(o, "operation")))
				.ToArray();
			_namespaces = [new WsdlNamespace("tem", Attr(definitions, "targetNamespace") ?? DefaultTargetNamespace)];
			var built = new List<(WsdlOperation Operation, string Namespace)>();
			foreach (XElement operation in operations) {
				WsdlOperation method = BuildOperation(operation);
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
			if (!IsNCName(caption)) {
				Warn($"The WSDL service name '{Shown(caption)}' is not an XML name; it was cleaned up for the caption.");
				caption = Shown(caption);
			}
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

		private WsdlOperation BuildOperation(XElement operation) {
			string caption = Attr(operation, "name");
			_operation = null;
			if (!IsNCName(caption)) {
				Warn($"An operation named '{Shown(caption)}' was skipped: WSDL names are XML names (no spaces or punctuation).");
				return null;
			}
			_operation = caption;
			XElement input = FirstChild(operation, "input");
			if (input is null) {
				Warn($"Operation '{caption}' has no input message (a notification operation); it was skipped.");
				return null;
			}
			string studioCode = PrefixedName(caption);
			List<XElement> bindingOperations = _bindingOperations.GetValueOrDefault(caption) ?? [];
			XElement soapOperation = bindingOperations.SelectMany(o => o.Elements().Where(e => Is(e, "operation"))).FirstOrDefault();
			string soapAction = Attr(soapOperation, "soapAction");
			(string rootElement, string rootNamespace) = RootElement(input, bindingOperations);
			ReportPartSelection(input, bindingOperations);
			List<Parameter> body = ParametersForMessage(input, fromRoot: false);
			List<Parameter> header = HeaderParameters(bindingOperations);
			XElement output = FirstChild(operation, "output");
			List<Parameter> response = output is null ? [] : ParametersForMessage(output, fromRoot: false);
			return new WsdlOperation(caption, studioCode, soapAction, rootElement ?? caption, rootNamespace,
				[.. body.Concat(header).Select(p => p.ToResult())], [.. response.Select(p => p.ToResult())]);
		}

		// The wizard's root element: the local name of the first part's element. The namespace is clio's addition: Studio stores
		// the bare name, which the platform qualifies with the default namespace.
		private (string Name, string Namespace) RootElement(XElement input, List<XElement> bindingOperations) {
			XElement part = MessageParts(LocalPart(Attr(input, "message"))).FirstOrDefault();
			string element = Attr(part, "element");
			if (element is not null) {
				return (LocalPart(element), NamespaceOf(element, part));
			}
			// A typed part (rpc style): the wrapper is the operation name in the soap:body namespace.
			XElement body = BindingInputs(bindingOperations).SelectMany(i => i.Elements().Where(e => Is(e, "body")))
				.FirstOrDefault();
			return (null, Attr(body, "namespace") ?? _namespaces[0].Uri);
		}

		private static IEnumerable<XElement> BindingInputs(List<XElement> bindingOperations) =>
			bindingOperations.SelectMany(o => o.Elements().Where(e => Is(e, "input")));

		private IEnumerable<XElement> MessageParts(string messageName) =>
			(_messages.GetValueOrDefault(messageName ?? string.Empty) ?? [])
				.SelectMany(m => m.Elements().Where(p => Is(p, "part")));

		// The wizard reads every part of the input message into the body and every part of the header message into the
		// Header; a binding that splits one message between them (soap:body parts=, a header part of the input message)
		// therefore gets parameters in both places.
		private void ReportPartSelection(XElement input, List<XElement> bindingOperations) {
			string inputMessage = LocalPart(Attr(input, "message"));
			IEnumerable<XElement> bindingInputs = BindingInputs(bindingOperations).ToArray();
			bool bodyParts = bindingInputs.SelectMany(i => i.Elements()).Any(e => Is(e, "body") && Attr(e, "parts") is not null);
			bool headerFromInput = bindingInputs.SelectMany(i => i.Elements())
				.Any(e => Is(e, "header") && LocalPart(Attr(e, "message")) == inputMessage);
			if (bodyParts || headerFromInput) {
				Warn($"{OperationPrefix}its binding splits the input message between the body and the SOAP header (part/parts); "
					+ "Studio's wizard reads every part into both, so remove the header elements from the body parameters and "
					+ "the body elements from the soap-header ones.");
			}
		}

		private List<Parameter> HeaderParameters(List<XElement> bindingOperations) {
			XElement[] headers = BindingInputs(bindingOperations).SelectMany(i => i.Elements().Where(e => Is(e, "header")))
				.ToArray();
			if (headers.Length == 0) {
				return [];
			}
			if (headers.Length > 1) {
				Warn($"{OperationPrefix}its binding input has {headers.Length} SOAP headers; Studio's wizard reads only the first "
					+ $"('{Shown(LocalPart(Attr(headers[0], "message")))}'). Add the others by hand if the API needs them.");
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
				Warn($"{OperationPrefix}its message element '{Shown(elementName)}' is not defined in the WSDL or its schemas; it "
					+ "has no parameters.");
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
					.SelectMany(t => t.Elements().Where(s => Is(s, SequenceTag)))
					.SelectMany(s => s.Elements().Where(e => Is(e, ElementTag)));
				if (!sequence.Any() && XsdTypes.ContainsKey(LocalPart(complexTypeName))) {
					Warn($"{OperationPrefix}its message element '{Shown(elementName)}' has the simple type "
						+ $"'{Shown(complexTypeName)}'; Studio's wizard reads no parameter from it. Add one by hand if the value is "
						+ "needed.");
				}
				foreach (XElement type in types ?? []) {
					ReportRootContent(elementName, type);
				}
			} else {
				foreach (XElement type in elementNode.Elements().Where(c => Is(c, ComplexTypeTag))) {
					ReportRootContent(elementName, type);
				}
				sequence = elementNode.Elements().Where(c => Is(c, ComplexTypeTag))
					.SelectMany(c => c.Elements().Where(s => Is(s, SequenceTag)))
					.SelectMany(s => s.Elements().Where(e => Is(e, ElementTag)));
			}
			var parameters = new List<Parameter>();
			foreach (XElement element in sequence.ToArray()) {
				parameters.AddRange(ParametersOf(element, namespaceUri, elementName));
			}
			return parameters;
		}

		// The wizard reads a message element's own fields from the xs:sequence directly in its type only.
		private void ReportRootContent(string elementName, XElement type) {
			if (type.Elements().Any(e => Is(e, "complexContent") || Is(e, "simpleContent"))) {
				Warn($"{OperationPrefix}the type of its message element '{Shown(elementName)}' extends another type; Studio's "
					+ "wizard reads no parameter from an extension at that level, so add them by hand.");
			}
			ReportUnreadContent(elementName, type);
		}

		private List<Parameter> ParametersOf(XElement sequenceElement, string parentNamespace, string rootElementName = null) {
			Node node = NodeOf(sequenceElement, parentNamespace, new Dictionary<string, int>(StringComparer.Ordinal), 0);
			return node is null ? [] : Flatten(node, rootElementName).Select(ToParameter).ToList();
		}

		#endregion

		#region Elements and types (WsdlElement, XmlSchemaTypeUtils)

		// SoapWebServiceMethodBuilder: one element of a sequence, with its item element name.
		private Node NodeOf(XElement sequenceElement, string parentNamespace, Dictionary<string, int> nesting, int depth) {
			RuntimeHelpers.EnsureSufficientExecutionStack();
			if (depth > MaxElementDepth) {
				Warn($"{OperationPrefix}elements nested more than {MaxElementDepth} levels deep were not read.");
				return null;
			}
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
			return ComplexType(node, parentNamespace, new Dictionary<string, int>(nesting, StringComparer.Ordinal), depth);
		}

		// SoapWebServiceMethodBuilder: a type may appear twice on one path; the third time the element is not read. Every anonymous
		// type counts as one type, as in the wizard.
		private Node ComplexType(Node node, string parentNamespace, Dictionary<string, int> nesting, int depth) {
			string key = node.ReferenceTypeName ?? "\0anonymous";
			nesting.TryGetValue(key, out int count);
			if (count == 2) {
				Warn($"{OperationPrefix}'{Shown(node.TypeName)}' was not read: Studio's wizard stops after two levels of the same type "
					+ "(a recursive type, or anonymous types nested three deep).");
				return null;
			}
			nesting[key] = count + 1;
			string ns = string.IsNullOrEmpty(node.Namespace) ? parentNamespace : node.Namespace;
			HashSet<XElement> copies = node.Copies;
			foreach (XElement element in node.Elements) {
				Node child = NodeOf(element, ns, nesting, depth + 1);
				if (child is not null) {
					child.IsCopy = copies.Contains(element);
					node.Nested.Add(child);
				}
			}
			node.Namespace = parentNamespace;
			return node;
		}

		// WsdlElement: an element and its type. A base type that extends itself, directly or through others, ends the chain with a
		// warning where the wizard would recurse until the stack runs out.
		private Node FromElement(XElement elementRef, HashSet<XElement> resolvingBases = null) {
			XElement definition = ElementDefinition(elementRef);
			string elementName = Attr(definition, "name");
			if (elementName is null) {
				return null;
			}
			if (!IsNCName(elementName)) {
				Warn($"{OperationPrefix}an element named '{Shown(elementName)}' was not read: element names are XML names.");
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
					Warn($"{OperationPrefix}the base type '{Shown(baseTypeName)}' of '{node.TypeName}' is not defined in the WSDL or its "
						+ "schemas; only the elements the extension adds were read.");
					node.Elements = elements;
					node.Copies = Copies(elements, ExtensionOf(TypeDefinition(definition)));
					return node;
				}
				resolvingBases ??= [];
				if (!resolvingBases.Add(baseType) || resolvingBases.Count > MaxTypeChain) {
					Warn($"{OperationPrefix}the base type '{Shown(baseTypeName)}' of '{node.TypeName}' extends itself; only the elements read "
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

		// XmlSchemaTypeUtils: a ref resolves to the global element, which takes the referencing element's maxOccurs.
		private XElement ElementDefinition(XElement elementRef) {
			string reference = Attr(elementRef, "ref");
			if (reference is null) {
				return elementRef;
			}
			if (!_globalElements.TryGetValue(LocalPart(reference), out XElement target)) {
				Warn($"{OperationPrefix}the element reference '{Shown(reference)}' is not defined in the WSDL or its schemas; it was "
					+ "not read.");
				return elementRef;
			}
			target.SetAttributeValue("maxOccurs", Attr(elementRef, "maxOccurs"));
			return target;
		}

		// XmlSchemaTypeUtils: the platform type of an element, plus a read of a type no schema defines: the wizard fails there; clio reads it as Text.
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
					Warn($"{OperationPrefix}element '{Shown(Attr(element, "name"))}' has the type '{Shown(typeAttribute)}', which "
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

		// XmlSchemaTypeUtils: an element with a type attribute resolves to that named complex or simple type. A type
		// that names a type (not valid XSD, but nothing stops it) is followed MaxTypeChain steps at most, so a cycle ends.
		private XElement TypeDefinition(XElement element, int depth = 0) {
			if (element is null || depth > MaxTypeChain) {
				return null;
			}
			string typeAttribute = Attr(element, "type");
			return string.IsNullOrEmpty(typeAttribute) ? element : TypeByName(LocalPart(typeAttribute), depth + 1);
		}

		// XmlSchemaTypeUtils: types > schema > complexType[name], else simpleType[name].
		private XElement TypeByName(string name, int depth = 0) {
			if (name is null) {
				return null;
			}
			XElement type = _complexTypes.GetValueOrDefault(name) ?? _simpleTypes.GetValueOrDefault(name);
			return type is null ? null : TypeDefinition(type, depth);
		}

		// XmlSchemaTypeUtils: whether the type is a simpleType restriction.
		private static bool IsRestriction(XElement element) {
			if (element is null) {
				return false;
			}
			XElement simpleType = Is(element, SimpleTypeTag)
				? element
				: element.Descendants().FirstOrDefault(e => Is(e, SimpleTypeTag) && !Is(e.Parent, ElementTag));
			return simpleType is not null && simpleType.Descendants().Any(e => Is(e, "restriction")
				&& !(Is(e.Parent, SimpleTypeTag) && Is(e.Parent.Parent, ElementTag)));
		}

		// XmlSchemaTypeUtils: a mixed complexType with attributes only reads as text.
		private static bool IsMixedText(XElement complexType) =>
			Attr(complexType, "mixed") == "true" && complexType.Elements().All(c => Is(c, "anyAttribute"));

		// XmlSchemaTypeUtils: the restriction base of the first simpleType inside (or of the element itself when it is
		// one), "string" for a simpleType without a restriction, empty when there is none.
		private static string SimpleTypeName(XElement element) {
			if (element is null) {
				return string.Empty;
			}
			XElement simpleType = Is(element, SimpleTypeTag)
				? element
				: element.Descendants().FirstOrDefault(e => Is(e, SimpleTypeTag));
			if (simpleType is null) {
				return string.Empty;
			}
			XElement restriction = simpleType.Descendants().FirstOrDefault(e => Is(e, "restriction"));
			return restriction is null ? "string" : LocalPart(Attr(restriction, "base"));
		}

		// XmlSchemaTypeUtils: whether the type extends another.
		private bool IsExtensionType(XElement element) {
			XElement definition = TypeDefinition(element);
			return definition is not null && definition.Descendants().Any(e => Is(e, "extension")
				&& (Is(e.Parent, "complexContent") || Is(e.Parent, "simpleContent")) && Is(e.Parent.Parent, ComplexTypeTag));
		}

		// XmlSchemaTypeUtils: the base type and the elements an extension adds.
		private (string BaseTypeName, XElement BaseType, List<XElement> Elements) ExtensionInfo(XElement element) {
			XElement definition = TypeDefinition(element);
			XElement extension = ExtensionOf(definition);
			string baseXmlTypeName = LocalPart(Attr(extension, "base"));
			XElement baseType = TypeByName(baseXmlTypeName);
			string simpleTypeName = SimpleTypeName(baseType);
			List<XElement> elements = extension.Descendants()
				.Where(e => Is(e, ElementTag) && Is(e.Parent, SequenceTag) && Is(e.Parent.Parent, "extension")).ToList();
			return (string.IsNullOrEmpty(simpleTypeName) ? baseXmlTypeName : simpleTypeName, baseType, elements);
		}

		// XmlSchemaTypeUtils: every element under a sequence anywhere inside the type, as the wizard's
		// querySelectorAll finds them. That includes the elements of an anonymous type nested inside, which so turn up a
		// second time one level too high (Fault/faultNumber next to Fault/detail/faultNumber); they are kept for parity
		// and marked, and WsdlRegistrationMapper leaves them out.
		private List<XElement> ComplexTypeChildElements(XElement element) {
			XElement definition = TypeDefinition(element);
			if (definition is null) {
				return [];
			}
			bool isComplexType = Is(definition, ComplexTypeTag);
			ReportUnreadContent(Attr(element, "name"), definition);
			return definition.Descendants()
				.Where(e => Is(e, ElementTag) && Is(e.Parent, SequenceTag) && (isComplexType || Is(e.Parent.Parent, ComplexTypeTag)))
				.ToList();
		}

		// "complexType extension": the first extension inside the type definition.
		private static XElement ExtensionOf(XElement definition) =>
			definition.Descendants().First(e => Is(e, "extension") && e.Ancestors().Any(a => Is(a, ComplexTypeTag)));

		// An element that sits inside another element of the type is read twice by the wizard: once there, and once here
		// as a copy.
		private static HashSet<XElement> Copies(IEnumerable<XElement> elements, XElement root) =>
			root is null ? [] : [.. elements.Where(e => IsInsideAnotherElement(e, root))];

		private static bool IsInsideAnotherElement(XElement element, XElement root) {
			for (XElement ancestor = element.Parent; ancestor is not null && ancestor != root; ancestor = ancestor.Parent) {
				if (Is(ancestor, ElementTag)) {
					return true;
				}
			}
			return false;
		}

		// The wizard reads the elements of an xs:sequence only; the platform sends no XML attribute.
		private void ReportUnreadContent(string name, XElement definition) {
			if (definition.Descendants().Any(e => Is(e, "choice") || Is(e, "all"))) {
				Warn($"{OperationPrefix}'{Shown(name)}' uses xs:choice or xs:all; Studio's wizard reads only the elements of an xs:sequence, "
					+ "so the others are missing. Add them by hand if the API needs them.");
			}
			if (definition.Descendants().Any(e => Is(e, "attribute") || Is(e, "attributeGroup"))) {
				Warn($"{OperationPrefix}'{Shown(name)}' has XML attributes; the platform sends and reads elements only, so they are not "
					+ "parameters.");
			}
		}

		#endregion

		#region Parameters (SoapWebServiceMethodBuilder, WsdlElement)

		private List<Node> Flatten(Node node, string rootElementName = null) {
			if (node.Nested.Count == 0) {
				return [node];
			}
			if (node.IsArray && node.IsObject) {
				Node holder = node.Parent ?? node;
				bool escapesItem = node.Parent is { IsArray: true };
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
					IsCopy = node.InCopy || holder.InCopy,
					EscapesItem = escapesItem
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
			IsCopy = node.InCopy,
			EscapesItem = node.EscapesItem
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

		// WsdlElement: an unknown namespace is added as the first free ns0..ns999.
		private string PrefixFor(string ns) {
			WsdlNamespace existing = _namespaces.FirstOrDefault(n => n.Uri == ns);
			if (existing is not null) {
				return existing.Prefix;
			}
			string prefix = Enumerable.Range(0, 1000).Select(i => "ns" + i)
				.FirstOrDefault(p => _namespaces.All(n => n.Prefix != p))
				?? throw new ArgumentException("The WSDL uses more than 1000 namespaces; it is not a WSDL clio can read.");
			_namespaces.Add(new WsdlNamespace(prefix, ns));
			return prefix;
		}

		// SoapWebServiceMethodBuilder: when every top-level caption has two words or more and the same first word, the
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
				int index = 1;
				while (taken.Contains(code)) {
					code = Truncate(local + index);
					index++;
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
	/// The wizard's code shortening: the capitalized words of the name, every one but
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
