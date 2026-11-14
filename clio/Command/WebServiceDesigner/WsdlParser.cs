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
/// <param name="Warnings">
/// Ports dropped, elements not read, constructs the wizard does not support, and ports the platform may not be able to
/// call (SOAP 1.2, WS-Addressing, message security).
/// </param>
/// <param name="WarningsDropped">How many more distinct warnings there were past <see cref="WsdlParser.MaxWarnings"/>.</param>
/// <param name="DroppedNamesWithQuestionMark">
/// Whether a name left out for not being an XML name contained '?', which is what the platform's ASCII download makes of a
/// non-ASCII character.
/// </param>
public sealed record WsdlDocument(IReadOnlyList<WsdlPort> Ports, string StudioPort, IReadOnlyList<string> Warnings,
	int WarningsDropped = 0, bool DroppedNamesWithQuestionMark = false);

/// <summary>What one parse reads: a port and operations named here are the only ones built.</summary>
/// <param name="Port">The one port to read (exact name first, then ignoring case); null reads every SOAP port.</param>
/// <param name="Operations">The operations to read (ignoring case); null or empty reads all.</param>
/// <param name="TimeLimit">How long the parse may run; null is <see cref="WsdlParser.MaxParseTime"/>.</param>
public sealed record WsdlParseOptions(string Port = null, IReadOnlyCollection<string> Operations = null,
	TimeSpan? TimeLimit = null);

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
	/// <param name="options">
	/// The port and operations to read. They are applied before anything is built, so the work and the element budget go
	/// to them only; Studio's pick is still decided over every port.
	/// </param>
	/// <returns>The SOAP ports with warnings.</returns>
	/// <exception cref="ArgumentException">
	/// No file holds a WSDL 1.1 service, a file is not well-formed XML or has a DTD, an imported schema is missing, the
	/// port or an operation named in <paramref name="options"/> is not in the WSDL, or the WSDL is past a reading limit.
	/// </exception>
	WsdlDocument Parse(IReadOnlyList<WsdlSourceFile> files, string schemaNamePrefix, WsdlParseOptions options = null);
}

/// <inheritdoc cref="IWsdlParser"/>
public sealed partial class WsdlParser : IWsdlParser {

	internal const string Soap11BindingNamespace = "http://schemas.xmlsoap.org/wsdl/soap/";
	internal const string Soap12BindingNamespace = "http://schemas.xmlsoap.org/wsdl/soap12/";
	private const string Wsdl20Namespace = "http://www.w3.org/ns/wsdl";
	// WSDL 1.1, whose wsdl:required marks an extension element in a binding as required.
	private const string Wsdl11Namespace = "http://schemas.xmlsoap.org/wsdl/";
	private const string XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema";
	private const string MimeNamespace = "http://schemas.xmlsoap.org/wsdl/mime/";
	// WS-Addressing markers a binding's policy carries (WCF writes wsaw:UsingAddressing; WS-Policy 1.5 uses wsam:Addressing).
	private const string WsawNamespace = "http://www.w3.org/2006/05/addressing/wsdl";
	private const string WsamNamespace = "http://www.w3.org/2007/05/addressing/metadata";
	// WCF's marker for WS-Addressing August 2004 (MessageVersion.Soap12WSAddressingAugust2004).
	private const string WsapNamespace = "http://schemas.xmlsoap.org/ws/2004/08/addressing/policy";
	// The namespaces of the wsa:Action and wsa:To headers such a binding expects.
	private const string WsaNamespace = "http://www.w3.org/2005/08/addressing";
	private const string WsaAugust2004Namespace = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
	// The namespace WCF declares on every WSDL it writes.
	private const string WcfContractNamespace = "http://schemas.microsoft.com/ws/2005/12/wsdl/contract";
	private const int MaxCharactersInDocument = 20_000_000;

	/// <summary>The wizard's longest code; read-wsdl keeps it only for the service code, a schema name (ADR D11).</summary>
	internal const int MaxCodeLength = 200;

	/// <summary>The data type of an element with child elements.</summary>
	internal const string ObjectType = "Object";

	/// <summary>
	/// The most SOAP ports one parse builds without a <see cref="WsdlParseOptions.Port"/>; past it the first ones and
	/// Studio's pick are read and the agent names the one it needs.
	/// </summary>
	internal const int MaxPorts = 20;

	/// <summary>
	/// The most operations one parse builds. An operation whose messages have no parts reads no element, so the element
	/// budget alone does not bound a WSDL that repeats many such operations over many ports.
	/// </summary>
	internal const int MaxOperations = 10_000;

	/// <summary>
	/// The longest port, operation or element name read. Paths and captions are built from names once per operation and
	/// per message, so a name of a million characters would cost gigabytes over the operations that use it.
	/// </summary>
	internal const int MaxNameLength = 1024;

	/// <summary>
	/// The most binding operations of one name kept (WSDL 1.1 overloads an operation name only a few times); every
	/// operation of that name walks them, so a binding repeating one name could otherwise cost operations x repeats.
	/// </summary>
	internal const int MaxSameNameBindingOperations = 8;

	/// <summary>
	/// How long one parse may run. The limits above bound what is read; this bounds what an input none of them
	/// foresaw can cost the host, which runs the parse on one of its own threads.
	/// </summary>
	internal static readonly TimeSpan MaxParseTime = TimeSpan.FromSeconds(30);

	/// <summary>
	/// The most elements one parse reads. The wizard's recursion cap bounds each type to two levels per path, but a chain of
	/// distinct types that each hold several fields of the next one still grows exponentially; past this the WSDL is
	/// refused instead of exhausting the worker.
	/// </summary>
	internal const int MaxElementsRead = 200_000;

	/// <summary>
	/// The most characters of parameter paths and captions one parse builds. A leaf's path and caption join the names of
	/// its ancestors up to the nearest array, so names of 1,024 characters 64 levels deep make one path about 65,000
	/// characters, and the element budget allows tens of thousands of leaves; past this the WSDL is refused instead of
	/// exhausting the host. A WSDL at the element budget whose leaves are a few levels deep under names of a few dozen
	/// characters builds about 30 million, so the budget leaves it room; a large result is listed by operation name later.
	/// </summary>
	internal const int MaxPathCharacters = 100_000_000;

	/// <summary>
	/// The most schema elements the imported files of one WSDL hold. A WSDL has a handful; each one is copied into the WSDL
	/// with the namespace declarations it inherits, so many of them under many declarations multiply.
	/// </summary>
	internal const int MaxMergedSchemas = 1_000;

	/// <summary>
	/// The most namespace declarations a schema of an imported file inherits from the elements around it. Each one is
	/// copied onto the schema and checked against those already there, so the cost grows with their square.
	/// </summary>
	internal const int MaxInheritedDeclarations = 256;

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
	internal static string Shown(string value) => ShownUpTo(value, 100);

	/// <summary>Like <see cref="Shown"/>, with a cap of its own (a platform error is longer than a name).</summary>
	/// <param name="value">The raw text.</param>
	/// <param name="maxLength">The most characters kept.</param>
	/// <returns>The text to show.</returns>
	internal static string ShownUpTo(string value, int maxLength) {
		if (string.IsNullOrEmpty(value)) {
			return string.Empty;
		}
		var builder = new StringBuilder(Math.Min(value.Length, maxLength + 1));
		// By rune, so an invisible character outside the basic plane (a Unicode tag, U+E0000-E007F) is caught as the
		// format character it is, not passed as two surrogate halves.
		foreach (Rune rune in value.EnumerateRunes()) {
			bool blank = Rune.IsControl(rune) || Rune.IsWhiteSpace(rune)
				|| Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Format
					or System.Globalization.UnicodeCategory.PrivateUse or System.Globalization.UnicodeCategory.OtherNotAssigned
				|| rune == Rune.ReplacementChar;
			if (blank && (builder.Length == 0 || builder[^1] == ' ')) {
				continue;
			}
			if (blank) {
				builder.Append(' ');
			} else {
				builder.Append(rune.ToString());
			}
			if (builder.Length >= maxLength) {
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
	private const string OperationTag = "operation";
	private const string MessageAttribute = "message";
	private const char ElementPathSeparator = '/';
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
	public WsdlDocument Parse(IReadOnlyList<WsdlSourceFile> files, string schemaNamePrefix, WsdlParseOptions options = null) {
		if (files is not { Count: > 0 }) {
			throw new ArgumentException("No WSDL file was given.");
		}
		// One clock for the whole parse: loading and merging the files count against the backstop too.
		var clock = new Deadline(options?.TimeLimit ?? MaxParseTime);
		var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
		foreach (WsdlSourceFile file in files) {
			clock.Check();
			documents.TryAdd(file.Key, Load(file));
		}
		var warnings = new List<string>();
		string wsdlKey = FindWsdl(documents, warnings);
		XDocument merged = new WsdlMerge(documents, clock).Merge(wsdlKey);
		return new Session(merged, schemaNamePrefix ?? string.Empty, warnings, options ?? new WsdlParseOptions(), clock)
			.Build();
	}

	// The time one parse may take, from the first file loaded.
	private sealed class Deadline(TimeSpan limit) {
		private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

		public void Check() {
			if (_watch.Elapsed >= limit) {
				throw new ArgumentException($"The WSDL takes longer than {limit.TotalSeconds:0} seconds to read, so it "
					+ "cannot be read whole. Read it again with port and operations, or describe the operations you need by "
					+ "hand.");
			}
		}
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
	private sealed class WsdlMerge(IReadOnlyDictionary<string, XDocument> documents, Deadline clock) {
		// Each file is merged once even when it is imported under several locations (a.xsd, ./a.xsd, a.xsd#x), which the
		// file-name match would otherwise follow into a recursion as deep as the number of spellings.
		private readonly HashSet<XDocument> _mergedDocuments = [];
		private readonly Dictionary<XElement, List<XAttribute>> _declarations = new();
		private int _schemasMerged;

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
				// All at once: each AddBeforeSelf walks the siblings before the importing schema, which grow with each insert.
				node.Parent.AddBeforeSelf(schemas);
			}
			RemoveAll(nodes);
		}

		// One pass per parent: removing nodes one at a time walks the siblings before each, which is quadratic for a schema
		// holding many directives.
		private static void RemoveAll(IReadOnlyCollection<XElement> nodes) {
			foreach (IGrouping<XElement, XElement> group in nodes.Where(n => n.Parent is not null).GroupBy(n => n.Parent)) {
				HashSet<XElement> removed = [.. group];
				List<XNode> kept = group.Key.Nodes().Where(n => n is not XElement e || !removed.Contains(e)).ToList();
				group.Key.ReplaceNodes(kept);
			}
		}

		// A file is merged once by what the location resolves to, not by the location as written: two schemas in different
		// folders may each import their own common.xsd.
		private List<XElement> SchemaNodes(string location, string importerKey) {
			if (string.IsNullOrEmpty(location)) {
				return [];
			}
			clock.Check();
			(string key, XDocument source) = Resolve(location, importerKey);
			if (!_mergedDocuments.Add(source)) {
				return [];
			}
			var document = new XDocument(source);
			var result = new List<XElement>();
			result.AddRange(Inserted(document, "import", key));
			result.AddRange(Inserted(document, "include", key));
			result.AddRange(SchemasCutApart(document));
			return result;
		}

		// Every schema element of the file, each a schema of its own as the wizard takes them, a nested one too (inside
		// xs:appinfo, or invalid XSD). A nested schema is cut out of its parent before anything is copied: copying each with
		// its content copied a file nested 250 levels deep once per level (654 MB from a 206 KB file). Each keeps the
		// namespace declarations it inherited, read before the cut, so its QName values still resolve.
		private List<XElement> SchemasCutApart(XDocument document) {
			List<XElement> schemas = document.Descendants().Where(e => Is(e, SchemaTag)).ToList();
			_schemasMerged += schemas.Count;
			if (_schemasMerged > MaxMergedSchemas) {
				throw new ArgumentException($"The WSDL's imported files hold more than {MaxMergedSchemas} schema elements; it "
					+ "cannot be read. Describe the operations you need by hand.");
			}
			List<List<XAttribute>> inherited = schemas.Select(InheritedDeclarations).ToList();
			var all = new HashSet<XElement>(schemas);
			RemoveAll(schemas.Where(s => s.Ancestors().Any(all.Contains)).ToList());
			var result = new List<XElement>(schemas.Count);
			for (int i = 0; i < schemas.Count; i++) {
				clock.Check();
				var copy = new XElement(schemas[i]);
				copy.Add(inherited[i]);
				result.Add(copy);
			}
			return result;
		}

		// The namespace declarations a schema inherits and does not make itself, the nearest ancestor's first. An element's
		// own declarations are listed once for every schema under it.
		private List<XAttribute> InheritedDeclarations(XElement schema) {
			var names = new HashSet<XName>(schema.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => a.Name));
			var result = new List<XAttribute>();
			for (XElement ancestor = schema.Parent; ancestor is not null; ancestor = ancestor.Parent) {
				if (!_declarations.TryGetValue(ancestor, out List<XAttribute> declarations)) {
					declarations = ancestor.Attributes().Where(a => a.IsNamespaceDeclaration).ToList();
					_declarations[ancestor] = declarations;
				}
				foreach (XAttribute declaration in declarations.Where(d => names.Add(d.Name))) {
					if (result.Count == MaxInheritedDeclarations) {
						throw new ArgumentException($"A schema of the WSDL's imported files sits inside more than "
							+ $"{MaxInheritedDeclarations} namespace declarations; it cannot be read. Describe the operations you "
							+ "need by hand.");
					}
					result.Add(new XAttribute(declaration));
				}
			}
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
			// The location is often an absolute URL, which the MCP error text redacts; the file name alone survives, so it is
			// named separately and the agent still learns which schema to add.
			string expected = queryName is null ? fileName : queryName + ".xsd";
			string named = string.IsNullOrEmpty(expected) ? string.Empty : $", as a file named '{Shown(expected)}'";
			throw new ArgumentException($"The WSDL imports '{shown}', which is not among the given files. Give that schema "
				+ $"too{named}: a local file is found by the location relative to the importing file, or by its file name.");
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
			XElement[] nodes = document.Descendants().Where(e => Is(e, directive) && Is(e.Parent, SchemaTag)).ToArray();
			foreach (XElement node in nodes) {
				result.AddRange(SchemaNodes(Attr(node, "schemaLocation"), importerKey));
			}
			RemoveAll(nodes);
			return result;
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
		public bool IsObject => Type == ObjectType;
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
		// A binding's operations are built once and shared by every port on it (ports x operations would otherwise grow
		// without the element budget noticing, since an operation without parts reads no element).
		private readonly Dictionary<XElement, (List<WsdlNamespace> Namespaces, List<WsdlOperation> Operations)> _builtBindings
			= new();
		// Facts of a type definition, read once: an element read scans its type, and a type referenced by many elements would
		// otherwise be scanned again for each of them.
		private readonly Dictionary<XElement, bool> _isExtension = new();
		private readonly Dictionary<XElement, bool> _isRestriction = new();
		private readonly Dictionary<XElement, List<XElement>> _childElements = new();
		private readonly Dictionary<XElement, (bool ChoiceOrAll, bool Attributes)> _content = new();
		private readonly Dictionary<XElement, (string BaseTypeName, XElement BaseType, List<XElement> Elements)> _extensions = new();
		private readonly Dictionary<XElement, HashSet<XElement>> _copiesByRoot = new();
		private readonly WsdlParseOptions _options;
		private readonly HashSet<string> _wantedOperations;
		private readonly Dictionary<XElement, XElement> _extensionOf = new();
		private readonly Dictionary<XElement, string> _simpleTypeNames = new();
		private readonly Deadline _clock;
		private long _pathCharacters;
		private List<WsdlNamespace> _namespaces;
		// The prefix of each namespace URI of the binding being built, and the next free ns-number: a lookup per element
		// path segment must not scan every namespace.
		private Dictionary<string, string> _prefixByUri;
		private int _nextPrefix;
		private string _operation;
		private int _elementsRead;
		private int _operationsBuilt;
		private int _warningsDropped;
		private bool _droppedNamesWithQuestionMark;

		public Session(XDocument doc, string prefix, List<string> warnings, WsdlParseOptions options, Deadline clock) {
			_clock = clock;
			_doc = doc;
			_prefix = prefix;
			_warnings = warnings;
			_options = options;
			string[] wanted = (options.Operations ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToArray();
			_wantedOperations = wanted.Length == 0 ? null : new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
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

		private string OperationPrefix => _operation is null ? string.Empty : $"Operation '{Shown(_operation)}': ";

		public WsdlDocument Build() {
			XElement definitions = _doc.Root;
			ReportUnsupported(definitions);
			var bindings = new Dictionary<string, XElement>(StringComparer.Ordinal);
			foreach (XElement binding in definitions.Elements().Where(e => Is(e, "binding"))) {
				bindings.TryAdd(Attr(binding, "name") ?? string.Empty, binding);
			}
			var candidates = new List<PortCandidate>();
			foreach (XElement service in definitions.Elements().Where(e => Is(e, "service"))) {
				foreach (XElement port in service.Elements().Where(e => Is(e, "port"))) {
					PortCandidate candidate = Candidate(service, port, bindings);
					if (candidate is not null) {
						candidates.Add(candidate);
					}
				}
			}
			// Studio's pick is decided over every SOAP port, before the port option narrows what is built.
			string studioPort = (candidates.FirstOrDefault(p => p.ServiceType == ServiceSchemaCodes.ServiceTypeSoap12)
				?? candidates.FirstOrDefault())?.Name;
			List<PortCandidate> selected = SelectPorts(candidates, studioPort);
			ReportDispatchRisks(definitions, candidates, selected);
			List<WsdlPort> ports = selected.Select(c => BuildPort(definitions, c)).ToList();
			CheckWantedOperations(definitions, candidates, selected, ports);
			return new WsdlDocument(ports, studioPort, _warnings, _warningsDropped, _droppedNamesWithQuestionMark);
		}

		private sealed record PortCandidate(string Name, XElement Service, XElement Address, int ServiceType, XElement Binding);

		private List<PortCandidate> SelectPorts(List<PortCandidate> candidates, string studioPort) {
			string port = _options.Port?.Trim();
			if (!string.IsNullOrEmpty(port)) {
				if (candidates.Count == 0) {
					return [];
				}
				PortCandidate selected = candidates.FirstOrDefault(c => string.Equals(c.Name, port, StringComparison.Ordinal))
					?? candidates.FirstOrDefault(c => string.Equals(c.Name, port, StringComparison.OrdinalIgnoreCase));
				return selected is null
					? throw new ArgumentException($"port '{Shown(port)}' is not a SOAP port of the WSDL; its SOAP ports: "
						+ string.Join(", ", candidates.Select(c => c.Name).Take(50)) + ".")
					: [selected];
			}
			if (candidates.Count <= MaxPorts) {
				return candidates;
			}
			Warn($"The WSDL has {candidates.Count} SOAP ports; the first {MaxPorts} and Studio's pick were read. Read "
				+ "another one with port.");
			List<PortCandidate> kept = candidates.Take(MaxPorts).ToList();
			PortCandidate pick = candidates.First(c => c.Name == studioPort);
			if (!kept.Contains(pick)) {
				kept.Add(pick);
			}
			return kept;
		}

		// An operation named in the options that the read ports do not give is refused with the reason: not in the WSDL
		// (with the operations there are), only on ports past the limit (with those ports), or left out with a warning
		// (with the warning). The names come from the port types, so the check builds nothing.
		private void CheckWantedOperations(XElement definitions, List<PortCandidate> candidates, List<PortCandidate> selected,
			List<WsdlPort> ports) {
			if (_wantedOperations is null || selected.Count == 0) {
				return;
			}
			// Built once: asking each candidate on its own would scan the whole WSDL once per port.
			Dictionary<string, string[]> byPortType = OperationsByPortType(definitions);
			string[] OperationNames(IEnumerable<PortCandidate> ports) => ports
				.Select(c => LocalPart(Attr(c.Binding, "type")) ?? string.Empty)
				.Distinct(StringComparer.Ordinal)
				.SelectMany(type => byPortType.GetValueOrDefault(type) ?? [])
				.Distinct(StringComparer.Ordinal)
				.ToArray();
			string[] everywhere = OperationNames(candidates);
			string[] unknown = _wantedOperations.Where(n => !everywhere.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
			if (unknown.Length > 0) {
				throw new ArgumentException($"operations: {string.Join(", ", unknown.Select(Shown).Take(20))} not found; the "
					+ "operations: " + string.Join(", ", everywhere.Select(Shown).Take(200)) + ".");
			}
			string[] onRead = OperationNames(selected);
			string[] elsewhere = _wantedOperations.Where(n => !onRead.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
			if (elsewhere.Length > 0) {
				string[] portsWithThem = candidates
					.Where(c => OperationNames([c]).Intersect(elsewhere, StringComparer.OrdinalIgnoreCase).Any())
					.Select(c => c.Name).Take(20).ToArray();
				// The port option or the port limit decided what was read; the refusal says which.
				string why = string.IsNullOrWhiteSpace(_options.Port)
					? $"are only on ports that were not read (the first {MaxPorts} were)"
					: $"are not on port '{Shown(selected[0].Name)}'";
				throw new ArgumentException($"operations: {string.Join(", ", elsewhere.Select(Shown).Take(20))} {why}; read the "
					+ "WSDL again with port, one of: " + string.Join(", ", portsWithThem) + ".");
			}
			HashSet<string> built = ports.SelectMany(p => p.Operations).Select(o => o.Caption)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			string[] skipped = _wantedOperations.Where(n => !built.Contains(n)).ToArray();
			if (skipped.Length > 0) {
				string[] reasons = skipped.SelectMany(n => _warnings.Where(w => w.Contains($"'{Shown(n)}'", StringComparison.Ordinal)))
					.Distinct().Take(3).ToArray();
				throw new ArgumentException($"operations: {string.Join(", ", skipped.Select(Shown).Take(20))} could not be read"
					+ (reasons.Length > 0 ? ": " + string.Join(" ", reasons) : "; see the WSDL."));
			}
		}

		// The operation names of each port type (port types of one name pooled, as the bindings find them by name).
		private static Dictionary<string, string[]> OperationsByPortType(XElement definitions) =>
			definitions.Elements()
				.Where(e => Is(e, "portType"))
				.GroupBy(e => Attr(e, "name") ?? string.Empty, StringComparer.Ordinal)
				.ToDictionary(g => g.Key, g => g.SelectMany(e => e.Elements().Where(o => Is(o, OperationTag)))
					.Select(o => Attr(o, "name") ?? string.Empty)
					.Distinct(StringComparer.Ordinal)
					.ToArray(), StringComparer.Ordinal);

		private void CheckClock() => _clock.Check();

		// Counts the characters of a path or caption about to be built against MaxPathCharacters, before it is built.
		private void ChargePath(long length) {
			_pathCharacters += length;
			if (_pathCharacters > MaxPathCharacters) {
				throw new ArgumentException($"The WSDL's parameter paths add up to more than {MaxPathCharacters} characters; "
					+ "it cannot be read whole. Read it again with port and operations, or describe the operations you need by "
					+ "hand.");
			}
		}

		// An XML name no longer than MaxNameLength. A long one gets its own warning here, so the caller's "not an XML name"
		// warning stays true.
		private bool IsReadableName(string name) {
			if (!IsNCName(name)) {
				return false;
			}
			if (name.Length > MaxNameLength) {
				Warn($"{OperationPrefix}a name of {name.Length} characters ('{Shown(name)}') was not read: names longer than "
					+ $"{MaxNameLength} characters are not read.");
				return false;
			}
			return true;
		}

		private void NoteDroppedName(string name) {
			if ((name ?? string.Empty).Contains('?')) {
				_droppedNamesWithQuestionMark = true;
			}
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

		private PortCandidate Candidate(XElement service, XElement port, Dictionary<string, XElement> bindings) {
			CheckClock();
			string portName = Attr(port, "name");
			if (!IsReadableName(portName)) {
				NoteDroppedName(portName);
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
			if (!bindings.TryGetValue(bindingName ?? string.Empty, out XElement binding)) {
				Warn($"Port '{portName}': its binding '{Shown(bindingName)}' is not in the WSDL; the port was dropped.");
				return null;
			}
			return new PortCandidate(portName, service, address, serviceType, binding);
		}

		private WsdlPort BuildPort(XElement definitions, PortCandidate candidate) {
			// Per port, not per build: every port on an rpc binding carries the same risk and names itself in the warning.
			ReportBindingStyle(candidate.Name, candidate.Binding);
			if (!_builtBindings.TryGetValue(candidate.Binding, out var built)) {
				built = BuildBinding(definitions, candidate.Binding);
				_builtBindings[candidate.Binding] = built;
			}
			string caption = Attr(candidate.Service, "name");
			if (!IsNCName(caption) || caption.Length > MaxNameLength) {
				NoteDroppedName(caption);
				// The caption reaches the arguments the agent forwards, so a sentence there is not kept even flattened.
				Warn($"The WSDL service name '{Shown(caption)}' is not an XML name of at most {MaxNameLength} characters; the "
					+ $"port name '{candidate.Name}' was used as the caption.");
				caption = candidate.Name;
			}
			return new WsdlPort(candidate.Name, caption, candidate.ServiceType, Attr(candidate.Address, "location"),
				built.Namespaces.ToList(), built.Operations.ToList());
		}

		private (List<WsdlNamespace> Namespaces, List<WsdlOperation> Operations) BuildBinding(XElement definitions,
			XElement binding) {
			_bindingOperations = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
			foreach (XElement bindingOperation in binding.Elements().Where(e => Is(e, OperationTag))) {
				string name = Attr(bindingOperation, "name") ?? string.Empty;
				if (_bindingOperations.GetValueOrDefault(name) is not { Count: >= MaxSameNameBindingOperations }) {
					AddTo(_bindingOperations, name, bindingOperation);
				}
			}
			string portTypeName = LocalPart(Attr(binding, "type"));
			XElement[] operations = definitions.Elements()
				.Where(e => Is(e, "portType") && Attr(e, "name") == portTypeName)
				.SelectMany(e => e.Elements().Where(o => Is(o, OperationTag)))
				.Where(o => _wantedOperations is null || _wantedOperations.Contains(Attr(o, "name") ?? string.Empty))
				.ToArray();
			string targetNamespace = Attr(definitions, "targetNamespace") ?? DefaultTargetNamespace;
			_namespaces = [new WsdlNamespace("tem", targetNamespace)];
			_prefixByUri = new Dictionary<string, string>(StringComparer.Ordinal) { [targetNamespace] = "tem" };
			_nextPrefix = 0;
			var built = new List<(WsdlOperation Operation, string Namespace)>();
			foreach (XElement operation in operations) {
				CheckClock();
				if (++_operationsBuilt > MaxOperations) {
					throw new ArgumentException($"The WSDL has more than {MaxOperations} operations to read over its ports; read it "
						+ "again with port and operations, naming the ones you need.");
				}
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
			return (_namespaces.ToList(), built.Select(b => b.Operation).ToList());
		}

		/// <summary>
		/// Warns about ports the platform may not be able to call as the WSDL describes them (measured against WCF): a SOAP
		/// 1.2 port gets its action only as a SOAPAction header, never as the action parameter of Content-Type that SOAP 1.2
		/// stacks such as WCF dispatch by; a port whose binding requires WS-Addressing gets no wsa:Action or wsa:To in the
		/// envelope; a port whose policy requires WS-Security message security cannot be called at all. Each warning names
		/// a SOAP 1.1 port of the same port type that needs neither, preferring one of the same service at the same scheme
		/// and host; one elsewhere is named with that difference and left to the user. A plain SOAP 1.2 port is a definite
		/// failure only on WCF (an ASMX service answers, measured), so outside a WSDL WCF wrote the SOAP 1.1 port is named
		/// as the way out after a failed test call, not as the port to register.
		/// </summary>
		private void ReportDispatchRisks(XElement definitions, List<PortCandidate> candidates, List<PortCandidate> selected) {
			var requirements = new BindingRequirements(definitions, CheckClock);
			bool? wcf = null;
			foreach (PortCandidate port in selected) {
				BindingRequirement required = requirements.Of(port.Binding);
				if (!required.MessageSecurity && required.AddressingNamespace is null
						&& port.ServiceType != ServiceSchemaCodes.ServiceTypeSoap12) {
					continue;
				}
				Soap11Port alternative = Soap11Alternative(port, candidates, requirements);
				string instead = alternative?.Instead();
				if (required.MessageSecurity) {
					Warn($"Port '{port.Name}' requires WS-Security message security (its binding policy), which the platform "
						+ "cannot produce, so it cannot be called from Creatio." + (instead ?? " Ask the API owner for an "
							+ "endpoint without message security (for WCF, a basicHttpBinding)."));
				} else if (required.AddressingNamespace is not null) {
					Warn($"Port '{port.Name}' requires WS-Addressing (its binding): the platform writes no wsa:Action or "
						+ "wsa:To into the envelope, so such a service (WCF wsHttpBinding with security mode None among them) "
						+ "refuses every call (DestinationUnreachable, To '')."
						+ (instead ?? $" If the user needs this port, declare the namespace {required.AddressingNamespace} and "
							+ "give each method two soap-header body parameters (prefixed codes) with the paths wsa:Action (its "
							+ "soap-action) and wsa:To (the base URI), filled from system settings or the process; then test a "
							+ "call."));
				} else if (wcf ??= WrittenByWcf(definitions)) {
					Warn($"Port '{port.Name}' is SOAP 1.2 and the WSDL was written by WCF: the platform sends the action only as "
						+ "a SOAPAction header, not as the action parameter of Content-Type that WCF dispatches SOAP 1.2 by, so "
						+ "WCF answers every call with ActionNotSupported (Action '')."
						+ (instead ?? " If the user needs this port, give each method a request header parameter (a prefixed "
							+ "code) with the path Content-Type and the value "
							+ "application/soap+xml;charset=utf-8;action=\"<its soap-action>\", one per method, from a system "
							+ "setting or the process; then test a call."));
				} else {
					Warn($"Port '{port.Name}' is SOAP 1.2: the platform sends the action only as a SOAPAction header. An ASMX "
						+ "service answers (measured); one that dispatches SOAP 1.2 by the action parameter of Content-Type "
						+ "fails because the action or operation is not found (WCF: ActionNotSupported, Action ''). Keep this "
						+ "port; if a test call fails so, "
						+ (alternative is null
							? "give the method a request header parameter (a prefixed code) with the path Content-Type and the "
								+ "value application/soap+xml;charset=utf-8;action=\"<its soap-action>\" from a system setting or "
								+ "the process."
							: $"switch to port '{alternative.Port.Name}' (SOAP 1.1){alternative.Caveat()}."));
				}
			}
		}

		/// <summary>
		/// A SOAP 1.1 port to use instead of a warned one.
		/// </summary>
		/// <param name="Port">The SOAP 1.1 port.</param>
		/// <param name="Difference">
		/// Null when it is of the same scheme and host as the warned port; otherwise how its address differs, because the
		/// credentials set up later would go there (readable, when it is http and the warned port https).
		/// </param>
		private sealed record Soap11Port(PortCandidate Port, string Difference) {
			/// <summary>The sentence a definite failure ends with: register it, or ask first when its address differs.</summary>
			public string Instead() => Difference is null
				? $" Register port '{Port.Name}' (SOAP 1.1) instead."
				: $" Port '{Port.Name}' (SOAP 1.1) can be called but has {Difference}: show the user its base-uri and register "
					+ "it only on their yes.";

			/// <summary>The clause a conditional advice adds: how to switch, and the user's yes when the address differs.</summary>
			public string Caveat() => Difference is null
				? " (modify-web-service setType soap11, and setBaseUri to its base-uri when that differs)"
				: $", which has {Difference}: show the user its base-uri and switch only on their yes";
		}

		// Whether WCF wrote the WSDL: WCF declares its contract namespace on every WSDL document it writes (?singleWsdl and
		// the root of ?wsdl, which the merge keeps). An addressing Action on a port-type message is not a sign: Axis2 and the
		// JAX-WS RI write one too.
		private static bool WrittenByWcf(XElement definitions) =>
			definitions.Attributes().Any(a => a.IsNamespaceDeclaration && a.Value == WcfContractNamespace);

		// A SOAP 1.1 port of the same port type that needs neither WS-Addressing nor message security, or null when there is
		// none: one of the same service at the warned port's scheme and host first, else the first with its difference.
		private static Soap11Port Soap11Alternative(PortCandidate port, List<PortCandidate> candidates,
			BindingRequirements requirements) {
			string portType = LocalPart(Attr(port.Binding, "type"));
			List<PortCandidate> alternatives = candidates.Where(c => c.ServiceType == ServiceSchemaCodes.ServiceTypeSoap11
					&& LocalPart(Attr(c.Binding, "type")) == portType && requirements.Of(c.Binding).IsPlain)
				.OrderBy(c => c.Service == port.Service ? 0 : 1)
				.ToList();
			if (alternatives.Count == 0) {
				return null;
			}
			Uri warned = AddressOf(port);
			PortCandidate same = alternatives.FirstOrDefault(c => AddressOf(c) is { } a && warned is not null
				&& string.Equals(a.Scheme, warned.Scheme, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(a.Host, warned.Host, StringComparison.OrdinalIgnoreCase));
			if (same is not null) {
				return new Soap11Port(same, null);
			}
			PortCandidate other = alternatives[0];
			return new Soap11Port(other, AddressDifference(warned, AddressOf(other)));
		}

		// How the alternative's address differs from the warned port's, as the warning names it.
		private static string AddressDifference(Uri warned, Uri address) {
			if (address is null || warned is null) {
				return "an address that could not be compared with this port's";
			}
			bool dropsHttps = string.Equals(warned.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
			return dropsHttps
				? "an http address where this port's is https, so authentication would travel readable"
				: $"an address on another host ({Shown(address.Host)})";
		}

		private static Uri AddressOf(PortCandidate port) =>
			Uri.TryCreate(Attr(port.Address, "location"), UriKind.Absolute, out Uri uri) ? uri : null;

		/// <summary>What a binding requires that the platform cannot send.</summary>
		/// <param name="AddressingNamespace">The WS-Addressing namespace its headers would need; null when not required.</param>
		/// <param name="MessageSecurity">Whether its policy requires WS-Security message security.</param>
		private sealed record BindingRequirement(string AddressingNamespace, bool MessageSecurity) {
			public bool IsPlain => AddressingNamespace is null && !MessageSecurity;
		}

		/// <summary>
		/// Reads what a binding requires from markers in the binding itself and in the wsp:Policy elements it references by
		/// <c>wsp:PolicyReference URI="#id"</c>. In a policy a marker is required unless it is under wsp:Optional="true";
		/// an extension element directly in the binding (not in a policy) only with wsdl:required="true", as WSDL 1.1 reads
		/// extensions. Each binding and each policy is walked once, top-down, with the parse clock checked, so a hostile WSDL
		/// cannot make the scan outrun the parse limit. Policy normal form with an empty alternative, nested policy
		/// references and policies attached to a port are not read.
		/// </summary>
		private sealed class BindingRequirements(XElement definitions, Action checkClock) {
			// The WS-Addressing markers and the addressing namespace the envelope headers would need for each.
			private static readonly Dictionary<XName, string> AddressingMarkers = new() {
				[XName.Get("UsingAddressing", WsawNamespace)] = WsaNamespace,
				[XName.Get("Addressing", WsamNamespace)] = WsaNamespace,
				[XName.Get("UsingAddressing", WsapNamespace)] = WsaAugust2004Namespace
			};

			// WS-SecurityPolicy assertions for message security (a transport binding is plain HTTPS and is not one).
			private static readonly HashSet<string> SecurityPolicyNamespaces = new(StringComparer.Ordinal) {
				"http://docs.oasis-open.org/ws-sx/ws-securitypolicy/200702",
				"http://docs.oasis-open.org/ws-sx/ws-securitypolicy/200512",
				"http://schemas.xmlsoap.org/ws/2005/07/securitypolicy"
			};

			private readonly Dictionary<XElement, BindingRequirement> _byScope = new();
			private Dictionary<string, XElement> _policies;
			private int _visited;

			/// <summary>What the binding requires, its policies included.</summary>
			public BindingRequirement Of(XElement binding) {
				if (_byScope.TryGetValue(binding, out BindingRequirement known)) {
					return known;
				}
				BindingRequirement result = Scan(binding, inPolicy: false);
				_policies ??= PoliciesById();
				foreach (string id in binding.Descendants().Where(e => Is(e, "PolicyReference"))
						.Select(r => Attr(r, "URI"))
						.Where(uri => uri is { Length: > 1 } && uri[0] == '#')
						.Select(uri => uri[1..])
						.Distinct(StringComparer.Ordinal)) {
					Tick();
					if (_policies.TryGetValue(id, out XElement policy)) {
						BindingRequirement fromPolicy = Scan(policy, inPolicy: true);
						result = new BindingRequirement(result.AddressingNamespace ?? fromPolicy.AddressingNamespace,
							result.MessageSecurity || fromPolicy.MessageSecurity);
					}
				}
				_byScope[binding] = result;
				return result;
			}

			private BindingRequirement Scan(XElement scope, bool inPolicy) {
				if (inPolicy && _byScope.TryGetValue(scope, out BindingRequirement cached)) {
					return cached;
				}
				string addressing = null;
				bool security = false;
				var stack = new Stack<(XElement Element, bool Optional, bool InPolicy)>();
				stack.Push((scope, false, inPolicy));
				while (stack.Count > 0 && (addressing is null || !security)) {
					Tick();
					(XElement element, bool inherited, bool underPolicy) = stack.Pop();
					bool optional = inherited || IsTrue(Attribute(element, "Optional"));
					bool policy = underPolicy || Is(element, "Policy");
					if (!optional) {
						addressing ??= RequiredAddressing(element, policy);
						security = security || (policy && IsMessageSecurity(element));
					}
					foreach (XElement child in element.Elements()) {
						stack.Push((child, optional, policy));
					}
				}
				var found = new BindingRequirement(addressing, security);
				if (inPolicy) {
					_byScope[scope] = found;
				}
				return found;
			}

			// The addressing namespace a marker requires: in a policy by its presence, directly in a binding only with
			// wsdl:required="true" (WSDL 1.1 reads an extension without it as optional). Null for any other element.
			private static string RequiredAddressing(XElement element, bool inPolicy) =>
				AddressingMarkers.TryGetValue(element.Name, out string ns)
				&& (inPolicy || IsTrue((string)element.Attribute(XName.Get("required", Wsdl11Namespace))))
					? ns
					: null;

			// A WS-SecurityPolicy symmetric or asymmetric binding: message security (a transport binding is plain HTTPS).
			private static bool IsMessageSecurity(XElement element) =>
				SecurityPolicyNamespaces.Contains(element.Name.NamespaceName)
				&& element.Name.LocalName is "SymmetricBinding" or "AsymmetricBinding";

			// The wsp:Policy elements of the document by their wsu:Id (or xml:id).
			private Dictionary<string, XElement> PoliciesById() {
				var policies = new Dictionary<string, XElement>(StringComparer.Ordinal);
				foreach (XElement policy in definitions.Descendants()) {
					Tick();
					if (!Is(policy, "Policy")) {
						continue;
					}
					string id = policy.Attributes().FirstOrDefault(a => a.Name.LocalName is "Id" or "id")?.Value;
					if (!string.IsNullOrEmpty(id)) {
						policies.TryAdd(id, policy);
					}
				}
				return policies;
			}

			private static string Attribute(XElement element, string localName) =>
				element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

			// xs:boolean true: "true" or "1".
			private static bool IsTrue(string value) =>
				value is not null && (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

			private void Tick() {
				if ((++_visited & 1023) == 0) {
					checkClock();
				}
			}
		}

		private void ReportBindingStyle(string portName, XElement binding) {
			bool rpc = binding.Descendants().Any(e => (Is(e, "binding") || Is(e, OperationTag))
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
			if (!IsReadableName(caption)) {
				NoteDroppedName(caption);
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
			XElement soapOperation = bindingOperations.SelectMany(o => o.Elements().Where(e => Is(e, OperationTag))).FirstOrDefault();
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
			XElement part = MessageParts(LocalPart(Attr(input, MessageAttribute))).FirstOrDefault();
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
			string inputMessage = LocalPart(Attr(input, MessageAttribute));
			IEnumerable<XElement> bindingInputs = BindingInputs(bindingOperations).ToArray();
			bool bodyParts = bindingInputs.SelectMany(i => i.Elements()).Any(e => Is(e, "body") && Attr(e, "parts") is not null);
			bool headerFromInput = bindingInputs.SelectMany(i => i.Elements())
				.Any(e => Is(e, "header") && LocalPart(Attr(e, MessageAttribute)) == inputMessage);
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
					+ $"('{Shown(LocalPart(Attr(headers[0], MessageAttribute)))}'). Add the others by hand if the API needs them.");
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
			string messageName = LocalPart(Attr(node, MessageAttribute));
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
			GenerateStudioCodes(parameters, string.Empty, new HashSet<string>(StringComparer.Ordinal),
				new Dictionary<string, int>(StringComparer.Ordinal));
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
			(bool choiceOrAll, bool attributes) = ContentFacts(type);
			ReportUnreadContent(elementName, choiceOrAll, attributes);
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
			CheckClock();
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
			(XElement definition, string maxOccurs) = ElementDefinition(elementRef);
			string elementName = Attr(definition, "name");
			if (elementName is null) {
				return null;
			}
			if (!IsReadableName(elementName)) {
				NoteDroppedName(elementName);
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
				Type = type ?? ObjectType,
				IsArray = maxOccurs == "unbounded",
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
					node.Copies = CopiesOf(elements, ExtensionOfOnce(TypeDefinition(definition)));
					return node;
				}
				resolvingBases ??= [];
				if (!resolvingBases.Add(baseType) || resolvingBases.Count > MaxTypeChain) {
					Warn($"{OperationPrefix}the base type '{Shown(baseTypeName)}' of '{node.TypeName}' extends itself; only the elements read "
						+ "before the cycle were kept.");
					node.Elements = elements;
					node.Copies = CopiesOf(elements, ExtensionOfOnce(TypeDefinition(definition)));
					return node;
				}
				Node baseNode = FromElement(baseType, resolvingBases);
				node.Elements = [.. elements, .. baseNode?.Elements ?? []];
				node.Copies = [.. CopiesOf(elements, ExtensionOfOnce(TypeDefinition(definition))), .. baseNode?.Copies ?? []];
			} else {
				node.Elements = ComplexTypeChildElements(definition);
				node.Copies = CopiesOf(node.Elements, TypeDefinition(definition));
			}
			return node;
		}

		// XmlSchemaTypeUtils: a ref resolves to the global element, with the referencing element's maxOccurs. The wizard writes
		// that maxOccurs onto the shared global element; here it travels with this read, so another use of the element (a
		// header part, a ref without maxOccurs) does not depend on which operation was read before it.
		private (XElement Definition, string MaxOccurs) ElementDefinition(XElement elementRef) {
			string reference = Attr(elementRef, "ref");
			if (reference is null) {
				return (elementRef, Attr(elementRef, "maxOccurs"));
			}
			if (!_globalElements.TryGetValue(LocalPart(reference), out XElement target)) {
				Warn($"{OperationPrefix}the element reference '{Shown(reference)}' is not defined in the WSDL or its schemas; it was "
					+ "not read.");
				return (elementRef, Attr(elementRef, "maxOccurs"));
			}
			return (target, Attr(elementRef, "maxOccurs"));
		}

		// XmlSchemaTypeUtils: the platform type of an element, plus a read of a type no schema defines: the wizard fails there; clio reads it as Text.
		private string DataValueType(XElement element) {
			string typeAttribute = Attr(element, "type");
			string xmlTypeName = LocalPart(typeAttribute);
			if (string.IsNullOrEmpty(xmlTypeName)) {
				xmlTypeName = SimpleTypeNameOnce(element);
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
			if (IsRestrictionOnce(definition)) {
				xmlTypeName = SimpleTypeNameOnce(definition);
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

		private string SimpleTypeNameOnce(XElement element) {
			if (!_simpleTypeNames.TryGetValue(element, out string name)) {
				name = SimpleTypeName(element);
				_simpleTypeNames[element] = name;
			}
			return name;
		}

		private bool IsRestrictionOnce(XElement definition) {
			if (!_isRestriction.TryGetValue(definition, out bool value)) {
				value = IsRestriction(definition);
				_isRestriction[definition] = value;
			}
			return value;
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
			if (definition is null) {
				return false;
			}
			if (!_isExtension.TryGetValue(definition, out bool value)) {
				value = OwnExtension(definition) is not null;
				_isExtension[definition] = value;
			}
			return value;
		}

		// XmlSchemaTypeUtils: the base type and the elements an extension adds.
		private (string BaseTypeName, XElement BaseType, List<XElement> Elements) ExtensionInfo(XElement element) {
			XElement definition = TypeDefinition(element);
			if (_extensions.TryGetValue(definition, out var known)) {
				return known;
			}
			XElement extension = ExtensionOfOnce(definition);
			string baseXmlTypeName = LocalPart(Attr(extension, "base"));
			XElement baseType = TypeByName(baseXmlTypeName);
			string simpleTypeName = SimpleTypeName(baseType);
			List<XElement> elements = extension.Descendants()
				.Where(e => Is(e, ElementTag) && Is(e.Parent, SequenceTag) && Is(e.Parent.Parent, "extension")).ToList();
			var info = (string.IsNullOrEmpty(simpleTypeName) ? baseXmlTypeName : simpleTypeName, baseType, elements);
			_extensions[definition] = info;
			return info;
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
			if (!_childElements.TryGetValue(definition, out List<XElement> elements)) {
				bool isComplexType = Is(definition, ComplexTypeTag);
				elements = definition.Descendants()
					.Where(e => Is(e, ElementTag) && Is(e.Parent, SequenceTag) && (isComplexType || Is(e.Parent.Parent, ComplexTypeTag)))
					.ToList();
				_childElements[definition] = elements;
			}
			(bool choiceOrAll, bool attributes) = ContentFacts(definition);
			ReportUnreadContent(Attr(element, "name"), choiceOrAll, attributes);
			return elements;
		}

		// Whether a type holds an xs:choice / xs:all or XML attributes, which the wizard does not read; scanned once per type.
		private (bool ChoiceOrAll, bool Attributes) ContentFacts(XElement definition) {
			if (!_content.TryGetValue(definition, out var facts)) {
				facts = (definition.Descendants().Any(e => Is(e, "choice") || Is(e, "all")),
					definition.Descendants().Any(e => Is(e, "attribute") || Is(e, "attributeGroup")));
				_content[definition] = facts;
			}
			return facts;
		}

		private XElement ExtensionOfOnce(XElement definition) {
			if (!_extensionOf.TryGetValue(definition, out XElement extension)) {
				extension = ExtensionOf(definition);
				_extensionOf[definition] = extension;
			}
			return extension;
		}

		// "complexType extension": the first extension inside the type definition.
		private static XElement ExtensionOf(XElement definition) => OwnExtension(definition);

		// The extension of the type itself: complexType > complexContent|simpleContent > extension, where the complexType is
		// the definition or the element's own anonymous type. The wizard's selector also finds an extension inside a nested
		// element's anonymous type and then reads the outer type as that base, losing its own fields; clio does not.
		private static XElement OwnExtension(XElement definition) {
			XElement type = Is(definition, ComplexTypeTag) ? definition : definition?.Elements().FirstOrDefault(e => Is(e, ComplexTypeTag));
			return type?.Elements().Where(c => Is(c, "complexContent") || Is(c, "simpleContent"))
				.SelectMany(c => c.Elements()).FirstOrDefault(e => Is(e, "extension"));
		}

		// An element that sits inside another element of the type is read twice by the wizard: once there, and once here
		// as a copy. The elements are always the ones read from the same root, so the set is kept per root.
		private HashSet<XElement> CopiesOf(IEnumerable<XElement> elements, XElement root) {
			if (root is null) {
				return [];
			}
			if (!_copiesByRoot.TryGetValue(root, out HashSet<XElement> copies)) {
				copies = [.. elements.Where(e => IsInsideAnotherElement(e, root))];
				_copiesByRoot[root] = copies;
			}
			return copies;
		}

		private static bool IsInsideAnotherElement(XElement element, XElement root) {
			for (XElement ancestor = element.Parent; ancestor is not null && ancestor != root; ancestor = ancestor.Parent) {
				if (Is(ancestor, ElementTag)) {
					return true;
				}
			}
			return false;
		}

		// The wizard reads the elements of an xs:sequence only; the platform sends no XML attribute.
		private void ReportUnreadContent(string name, bool choiceOrAll, bool attributes) {
			if (choiceOrAll) {
				Warn($"{OperationPrefix}'{Shown(name)}' uses xs:choice or xs:all; Studio's wizard reads only the elements of an xs:sequence, "
					+ "so the others are missing. Add them by hand if the API needs them.");
			}
			if (attributes) {
				Warn($"{OperationPrefix}'{Shown(name)}' has XML attributes; the platform sends and reads elements only, so they are not "
					+ "parameters.");
			}
		}

		#endregion

		#region Parameters (SoapWebServiceMethodBuilder, WsdlElement)

		private List<Node> Flatten(Node node, string rootElementName = null) {
			CheckClock();
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

		private Parameter ToParameter(Node node) {
			CheckClock();
			return new Parameter {
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
		}

		// A leaf's path and caption join the names of its ancestors up to the nearest array. Their length is counted from
		// the names and charged against MaxPathCharacters first, then the string is built once, so a path costs its own
		// length, not its length per level, and a refused one is never built.
		private string BuildPath(Node node) => BuildJoined(node, ElementPathSeparator, withPrefix: true);

		private string BuildCaption(Node node) => BuildJoined(node, ' ', withPrefix: false);

		private string BuildJoined(Node node, char separator, bool withPrefix) {
			// PrefixFor numbers a namespace it meets first, so it is asked in the order it always was: the node, then up.
			string nodePrefix = withPrefix ? PrefixFor(node.Namespace) : null;
			var chain = new List<(string Name, string Prefix)>();
			if (!(node.IsArray && !node.IsObject)) {
				chain.Add((node.Path, nodePrefix));
			}
			for (Node parent = node.Parent; parent is not null && !parent.IsArray; parent = parent.Parent) {
				chain.Add((parent.Path, withPrefix ? PrefixFor(parent.Namespace) : null));
			}
			long length = Math.Max(chain.Count - 1, 0)
				+ chain.Sum(segment => (long)segment.Name.Length + (withPrefix ? segment.Prefix.Length + 1 : 0));
			ChargePath(length);
			var builder = new StringBuilder((int)length);
			for (int i = chain.Count - 1; i >= 0; i--) {
				if (builder.Length > 0) {
					builder.Append(separator);
				}
				if (withPrefix) {
					builder.Append(chain[i].Prefix).Append(':');
				}
				builder.Append(chain[i].Name);
			}
			return builder.ToString();
		}

		// WsdlElement: an unknown namespace is added as the first free ns0..ns999.
		private string PrefixFor(string ns) {
			string key = ns ?? "\0";
			if (_prefixByUri.TryGetValue(key, out string known)) {
				return known;
			}
			if (_nextPrefix >= 1000) {
				throw new ArgumentException("The WSDL uses more than 1000 namespaces; it is not a WSDL clio can read.");
			}
			string prefix = "ns" + _nextPrefix++;
			_namespaces.Add(new WsdlNamespace(prefix, ns));
			_prefixByUri[key] = prefix;
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

		// The wizard numbers a repeated code from 1; the next number per code is remembered, so many equal names cost linear
		// time instead of probing every number again.
		private void GenerateStudioCodes(List<Parameter> parameters, string parentName, HashSet<string> taken,
			Dictionary<string, int> next) {
			foreach (Parameter parameter in parameters) {
				CheckClock();
				string local = PrefixedName(parameter.Caption, parentName);
				string code = local;
				if (taken.Contains(code)) {
					int index = next.GetValueOrDefault(local, 1);
					do {
						code = Truncate(local + index);
						index++;
					} while (taken.Contains(code));
					next[local] = index;
				}
				taken.Add(code);
				parameter.Code = code;
				GenerateStudioCodes(parameter.Items, parameter.Caption, taken, next);
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
