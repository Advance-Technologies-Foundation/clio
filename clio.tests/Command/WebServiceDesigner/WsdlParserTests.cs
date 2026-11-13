using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.WebServiceDesigner;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

/// <summary>
/// The C# port of Studio's web service wizard parser (read-wsdl, slice C). The golden files under Examples/Wsdl are the
/// output of Studio's own parser (creatio-ui web-service-wizard) on the same WSDLs, method codes without their random
/// suffix; the other cases restate the expectations of the wizard's unit tests on WSDLs written for clio.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WsdlParserTests {

	private readonly WsdlParser _sut = new();

	private static string FixturePath(params string[] parts) =>
		Path.Combine([TestContext.CurrentContext.TestDirectory, "Examples", "Wsdl", .. parts]);

	private static WsdlSourceFile FixtureFile(params string[] parts) {
		string path = FixturePath(parts);
		return new WsdlSourceFile(path, File.ReadAllBytes(path));
	}

	private static WsdlSourceFile Inline(string xml, string key = "inline.wsdl") =>
		new(key, Encoding.UTF8.GetBytes(xml));

	private WsdlDocument ParseFixture(params string[] parts) => _sut.Parse([FixtureFile(parts)], "Usr");

	private static JsonNode Golden(WsdlDocument document) =>
		JsonSerializer.SerializeToNode(document.Ports.Select(port => new {
			type = port.ServiceType == ServiceSchemaCodes.ServiceTypeSoap12 ? "soap12" : "soap11",
			caption = port.ServiceName,
			portName = port.Name,
			baseUri = port.BaseUri,
			namespaces = port.Namespaces.Select(n => new { prefix = n.Prefix, name = n.Uri }),
			methods = port.Operations.Select(m => new {
				name = m.StudioCode, caption = m.Caption, soapAction = m.SoapAction, uri = m.StudioMessageElement,
				request = m.Request.Select(GoldenParameter), response = m.Response.Select(GoldenParameter)
			})
		}));

	private static object GoldenParameter(WsdlParameter p) => new {
		name = p.StudioCode, caption = p.Caption, path = p.Path, type = p.Type, isArray = p.IsArray,
		isSoapHeader = p.IsSoapHeader, sequenceElementName = p.SequenceElement, items = p.Items.Select(GoldenParameter)
	};

	private static WsdlOperation Operation(WsdlDocument document, string caption, string port = null) =>
		document.Ports.First(p => port is null || p.Name == port).Operations.Single(o => o.Caption == caption);

	private static string Wsdl(string types, string messages, string operations, string bindingOperations,
		string ports = null) => $"""
		<wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/"
			xmlns:soap12="http://schemas.xmlsoap.org/wsdl/soap12/" xmlns:xs="http://www.w3.org/2001/XMLSchema"
			xmlns:tns="http://example.org/t/" targetNamespace="http://example.org/t/">
			<wsdl:types><xs:schema elementFormDefault="qualified" targetNamespace="http://example.org/t/">{types}</xs:schema></wsdl:types>
			{messages}
			<wsdl:portType name="P">{operations}</wsdl:portType>
			<wsdl:binding name="B" type="tns:P"><soap:binding transport="http://schemas.xmlsoap.org/soap/http" />{bindingOperations}</wsdl:binding>
			<wsdl:service name="Svc">{ports ?? "<wsdl:port name=\"Soap\" binding=\"tns:B\"><soap:address location=\"http://localhost/svc\" /></wsdl:port>"}</wsdl:service>
		</wsdl:definitions>
		""";

	private static string SimpleOperation(string name, string requestElement = null) =>
		$"<wsdl:operation name=\"{name}\"><wsdl:input message=\"tns:{name}In\" /><wsdl:output message=\"tns:{name}Out\" /></wsdl:operation>";

	private static string Messages(string name, string requestElement, string responseElement) =>
		$"<wsdl:message name=\"{name}In\"><wsdl:part name=\"parameters\" element=\"tns:{requestElement}\" /></wsdl:message>"
		+ $"<wsdl:message name=\"{name}Out\"><wsdl:part name=\"parameters\" element=\"tns:{responseElement}\" /></wsdl:message>";

	[TestCase("calculator.wsdl", "calculator.expected.json")]
	[TestCase("wizard-features.wsdl", "wizard-features.expected.json")]
	[Description("A WSDL is read exactly as Studio's wizard reads it: ports, namespaces with their ns0..nN order, operations with SOAP action and root element, and every parameter's caption, path, type, array flag, SOAP-header flag, item element and code (the golden files are Studio's own parser output).")]
	public void Parse_Should_Match_Studio_Wizard_Output(string wsdl, string expected) {
		// Arrange
		JsonNode golden = JsonNode.Parse(File.ReadAllText(FixturePath(expected)));

		// Act
		WsdlDocument document = ParseFixture(wsdl);

		// Assert
		JsonNode actual = Golden(document);
		JsonNode.DeepEquals(actual, golden).Should().BeTrue(
			because: $"the port must match Studio's wizard output for {wsdl}; actual: {actual.ToJsonString()}");
	}

	[Test]
	[Description("A WSDL whose types come from imported and included schemas, one of them imported by another schema, is merged as the wizard merges it; a local file is found by the file name of a URL schemaLocation.")]
	public void Parse_Should_Merge_Imported_And_Included_Schemas_Like_Studio() {
		// Arrange
		JsonNode golden = JsonNode.Parse(File.ReadAllText(FixturePath("split", "orders.expected.json")));
		WsdlSourceFile[] files = [
			FixtureFile("split", "orders.wsdl"), FixtureFile("split", "orders-operations.xsd"),
			FixtureFile("split", "orders-common.xsd"), FixtureFile("split", "orders-money.xsd")
		];

		// Act
		WsdlDocument document = _sut.Parse(files, "Usr");

		// Assert
		JsonNode.DeepEquals(Golden(document), golden).Should().BeTrue(
			because: "the merged document must give Studio's output; actual: " + Golden(document).ToJsonString());
	}

	[Test]
	[Description("A schema the WSDL imports but that was not given fails with the location to add, as the wizard's merge does.")]
	public void Parse_Should_Name_A_Missing_Imported_Schema() {
		// Arrange
		WsdlSourceFile wsdl = FixtureFile("split", "orders.wsdl");

		// Act
		Action act = () => _sut.Parse([wsdl], "Usr");

		// Assert
		act.Should().Throw<ArgumentException>(because: "the types of the operations live in the missing schema")
			.WithMessage("*orders-operations.xsd*not among the given files*");
	}

	[Test]
	[Description("A missing schema imported by an absolute URL is still named after the MCP error redaction removes the URL: the file name is given separately.")]
	public void Parse_Should_Name_The_Missing_File_So_It_Survives_Redaction() {
		// Arrange
		WsdlSourceFile wsdl = FixtureFile("split", "orders.wsdl");

		// Act
		Action act = () => _sut.Parse([wsdl], "Usr");

		// Assert
		string message = act.Should().Throw<ArgumentException>(because: "the imported schema is missing").Which.Message;
		string redacted = SensitiveErrorTextRedactor.Redact(message);
		redacted.Should().Contain("[redacted-uri]", because: "the import location is an absolute URL, which the error text redacts");
		redacted.Should().Contain("a file named 'orders-operations.xsd'",
			because: "without the file name the agent cannot tell which schema to add (seen on a stand run)");
	}

	[Test]
	[Description("Studio's quick setup picks the SOAP 1.2 port when the WSDL has both; the parser marks it and keeps both, dropping the HTTP port with a warning.")]
	public void Parse_Should_Mark_The_Soap12_Port_And_Drop_Http_Ports() {
		// Arrange / Act
		WsdlDocument document = ParseFixture("wizard-features.wsdl");

		// Assert
		document.Ports.Select(p => p.Name).Should().Equal(["ClioWizardFeaturesSoap", "ClioWizardFeaturesSoap12"],
			because: "every SOAP 1.1 and SOAP 1.2 port is returned, in WSDL order (D12)");
		document.StudioPort.Should().Be("ClioWizardFeaturesSoap12", because: "Studio's section picks the SOAP 1.2 port");
		document.Warnings.Should().Contain(w => w.Contains("ClioWizardFeaturesHttpGet") && w.Contains("dropped"),
			because: "an HTTP port cannot become a SOAP service and the agent is told why it is missing");
	}

	[Test]
	[Description("With only a SOAP 1.1 port, that port is Studio's pick.")]
	public void Parse_Should_Pick_The_First_Port_When_There_Is_No_Soap12_Port() {
		// Arrange / Act
		WsdlDocument document = _sut.Parse([FixtureFile("split", "orders.wsdl"), FixtureFile("split", "orders-operations.xsd"),
			FixtureFile("split", "orders-common.xsd"), FixtureFile("split", "orders-money.xsd")], "Usr");

		// Assert
		document.StudioPort.Should().Be("OrdersSoap", because: "with no SOAP 1.2 port the only SOAP port is the pick");
	}

	[Test]
	[Description("The wizard's unit-test expectations hold: a referenced element takes its parent's namespace, nested namespaces get ns0/ns1 by first use, captions lose a common first word, and a wrapped scalar array keeps its item element.")]
	public void Parse_Should_Read_References_Namespaces_And_Arrays_As_Studio() {
		// Arrange / Act
		WsdlDocument document = ParseFixture("wizard-features.wsdl");

		// Assert
		WsdlParameter byRef = Operation(document, "GetByRef").Request.Single();
		byRef.Path.Should().Be("tem:request", because: "a referenced element without a namespace takes its parent's");
		byRef.Items.Select(i => i.Caption).Should().Equal(["identifier", "roleName"],
			because: "the type comes from the referenced element");
		Operation(document, "GetNested").Request.Select(p => p.Path).Should().Contain("tem:data/ns0:metadata/ns1:schema",
			because: "each path segment carries the prefix of its element's namespace");
		Operation(document, "GetComplex").Request.Select(p => p.Caption).Should().Contain("role identifier",
			because: "the common first word 'msg' is removed from every caption");
		WsdlParameter tags = Operation(document, "GetByArray").Request.Single();
		(tags.IsArray, tags.Type, tags.SequenceElement, tags.Items.Count).Should().Be((true, "Text", "ns0:string", 0),
			because: "a wrapped scalar array is one array parameter named by its item element");
		WsdlParameter roles = Operation(document, "GetByRoles").Request.Single(p => p.Caption == "roles");
		(roles.Path, roles.Type, roles.SequenceElement).Should().Be(("tem:msg/ns0:roles", "Object", "ns0:RoleObject"),
			because: "an object array keeps its path and its item element");
		roles.Items.Select(i => i.Path).Should().Contain("ns0:metadata/ns1:schema",
			because: "item fields are relative to the item");
		WsdlParameter regions = Operation(document, "GetRegions").Response.Single();
		(regions.Path, regions.SequenceElement, regions.Items.Count).Should().Be(("tem:GetRegionsResponse", "tem:Region", 3),
			because: "an object array in the response root takes the root element's name");
	}

	[Test]
	[Description("The XSD type map of the wizard is applied: long and int are Integer, unsignedLong is Text, decimal is Float, a guid restriction is Guid, an enumeration restriction of int is Integer, an inline string restriction is Text.")]
	public void Parse_Should_Map_Xsd_Types_As_Studio() {
		// Arrange / Act
		WsdlOperation operation = Operation(ParseFixture("wizard-features.wsdl"), "GetManyTypes");

		// Assert
		operation.Request.ToDictionary(p => p.Caption, p => p.Type).Should().BeEquivalentTo(new Dictionary<string, string> {
			["name"] = "Text", ["count"] = "Integer", ["total"] = "Integer", ["big"] = "Text", ["percents"] = "Float",
			["date"] = "DateTime", ["day"] = "Date", ["uid"] = "Guid", ["level"] = "Integer", ["code"] = "Text"
		}, because: "each XSD type maps to the platform type Studio gives it");
	}

	[Test]
	[Description("A recursive type is read two levels deep and the third is cut with a warning, as the wizard does.")]
	public void Parse_Should_Cap_Recursion_At_Two_Levels() {
		// Arrange / Act
		WsdlDocument document = ParseFixture("wizard-features.wsdl");

		// Assert
		IEnumerable<string> paths = Operation(document, "FindPerson").Response.Select(p => p.Path);
		paths.Should().Contain("tem:FindPersonResult/tem:Spouse/tem:Home/tem:City",
			because: "the second level of the type is read");
		paths.Should().NotContain(p => p.Contains("tem:Spouse/tem:Spouse"), because: "the third level is not read");
		document.Warnings.Should().Contain(w => w.Contains("FindPerson") && w.Contains("Spouse"),
			because: "the agent learns that part of the type is missing");
	}

	[Test]
	[Description("SOAP headers: the first binding input header becomes parameters flagged as SOAP headers after the body ones (complex, array), and a second header gives a warning.")]
	public void Parse_Should_Read_The_First_Soap_Header() {
		// Arrange / Act
		WsdlDocument document = ParseFixture("wizard-features.wsdl");

		// Assert
		WsdlOperation secure = Operation(document, "SecureCall");
		secure.Request.Where(p => !p.IsSoapHeader).Select(p => p.Path).Should().Equal(["tem:order/tem:id", "tem:order/tem:note"],
			because: "body parameters come first");
		secure.Request.Where(p => p.IsSoapHeader).Select(p => p.Path).Should().Contain(
			["tem:AuthHeader/tem:UserName", "tem:AuthHeader/tem:Scope/tem:Tenant"],
			because: "a complex header is flattened from its root element");
		secure.Request.Should().NotContain(p => p.Path.Contains("TraceHeader"), because: "only the first header is read");
		document.Warnings.Should().Contain(w => w.Contains("SecureCall") && w.Contains("2 SOAP headers"),
			because: "the second header must be added by hand");
		WsdlParameter tags = Operation(document, "TaggedCall").Request.Single();
		(tags.Path, tags.IsSoapHeader, tags.IsArray, tags.SequenceElement).Should().Be(("tem:TagsHeader", true, true, "tem:Tag"),
			because: "a header array keeps its item element");
	}

	[Test]
	[Description("An element of an anonymous type nested inside another element is listed a second time one level too high, as the wizard does, and is marked as a copy.")]
	public void Parse_Should_Mark_The_Wizards_Copies_Of_Nested_Elements() {
		// Arrange / Act
		WsdlDocument document = ParseFixture("wizard-features.wsdl");

		// Assert
		Operation(document, "SecureCall").Request.Single(p => p.Path == "tem:AuthHeader/tem:Tenant").IsCopy.Should().BeTrue(
			because: "Tenant sits inside Scope, so its copy directly under AuthHeader exists only in the wizard's output");
		Operation(document, "SecureCall").Request.Single(p => p.Path == "tem:AuthHeader/tem:Scope/tem:Tenant").IsCopy
			.Should().BeFalse(because: "the element at its real place is kept");
		Operation(document, "GetCatalog").Response.Where(p => p.IsCopy).Select(p => p.Path)
			.Should().Equal(["tem:items/tem:title", "tem:items/tem:short_description"],
				because: "the fields of the item array also turn up under items itself");
	}

	[Test]
	[Description("A code longer than 200 characters keeps its last 200, and a root element name is the method's message element.")]
	public void Parse_Should_Cap_Codes_And_Read_The_Root_Element() {
		// Arrange / Act
		WsdlOperation ping = Operation(ParseFixture("wizard-features.wsdl"), "Ping");

		// Assert
		ping.Request.Single().StudioCode.Should().HaveLength(200, because: "the wizard caps codes at 200 characters");
		ping.StudioMessageElement.Should().StartWith("PingWithAVeryLongRequestElementName",
			because: "the message element is the input part's element, not the operation name");
		ping.MessageElementNamespace.Should().Be("http://example.org/clio/wizard/",
			because: "clio keeps the element's namespace to give it a prefix");
	}

	[Test]
	[Description("A typed message part (no element) is read as the wizard reads it, and an operation without an element part falls back to its name as the message element.")]
	public void Parse_Should_Read_Typed_Parts() {
		// Arrange / Act
		WsdlOperation operation = Operation(ParseFixture("wizard-features.wsdl"), "CheckPhone");

		// Assert
		operation.Request.Select(p => p.Caption).Should().Equal(["PhoneNumber", "LicenseKey"],
			because: "each part is a parameter");
		operation.StudioMessageElement.Should().Be("CheckPhone", because: "a typed part has no element to name the root");
	}

	[Test]
	[Description("A complexContent extension adds its elements before the base type's, and a simpleContent extension of a simple type is that simple type (the wizard's extension test).")]
	public void Parse_Should_Read_Type_Extensions() {
		// Arrange
		string types = """
			<xs:complexType name="Contact"><xs:sequence><xs:element name="Name" type="xs:string" /><xs:element name="Age" type="xs:int" /></xs:sequence></xs:complexType>
			<xs:complexType name="Employee"><xs:complexContent><xs:extension base="tns:Contact"><xs:sequence>
				<xs:element name="Title" type="xs:string" /><xs:element name="Salary" type="xs:int" />
				<xs:element name="Picture"><xs:complexType><xs:simpleContent><xs:extension base="xs:base64Binary"><xs:attribute name="kind" type="xs:string" /></xs:extension></xs:simpleContent></xs:complexType></xs:element>
			</xs:sequence></xs:extension></xs:complexContent></xs:complexType>
			<xs:element name="Find"><xs:complexType><xs:sequence><xs:element name="id" type="xs:string" /></xs:sequence></xs:complexType></xs:element>
			<xs:element name="FindResponse"><xs:complexType><xs:sequence><xs:element name="Result" type="tns:Employee" /></xs:sequence></xs:complexType></xs:element>
			""";
		string xml = Wsdl(types, Messages("Find", "Find", "FindResponse"), SimpleOperation("Find"),
			"<wsdl:operation name=\"Find\"><soap:operation soapAction=\"urn:find\" /></wsdl:operation>");

		// Act
		WsdlOperation find = Operation(_sut.Parse([Inline(xml)], "Usr"), "Find");

		// Assert
		find.Response.Select(p => (p.Path, p.Type)).Should().Equal([
			("tem:Result/tem:Title", "Text"), ("tem:Result/tem:Salary", "Integer"), ("tem:Result/tem:Picture", "Text"),
			("tem:Result/tem:Name", "Text"), ("tem:Result/tem:Age", "Integer")
		], because: "the extension's elements come first, then the base type's, and a simple content base is its simple type");
	}

	[Test]
	[Description("A type Studio's wizard cannot read (an XSD built-in it does not map, or a type no schema defines) is read as Text with a warning, where the wizard fails.")]
	public void Parse_Should_Read_Unmapped_Types_As_Text_With_A_Warning() {
		// Arrange
		string types = """
			<xs:element name="Get"><xs:complexType><xs:sequence>
				<xs:element name="link" type="xs:anyURI" /><xs:element name="other" type="tns:Missing" />
			</xs:sequence></xs:complexType></xs:element>
			<xs:element name="GetResponse"><xs:complexType><xs:sequence /></xs:complexType></xs:element>
			""";
		string xml = Wsdl(types, Messages("Get", "Get", "GetResponse"), SimpleOperation("Get"),
			"<wsdl:operation name=\"Get\"><soap:operation soapAction=\"urn:get\" /></wsdl:operation>");

		// Act
		WsdlDocument document = _sut.Parse([Inline(xml)], "Usr");

		// Assert
		Operation(document, "Get").Request.Select(p => p.Type).Should().Equal(["Text", "Text"],
			because: "a value of an unknown type still travels as text");
		document.Warnings.Should().Contain(w => w.Contains("xs:anyURI") && w.Contains("does not map"),
			because: "the agent learns the type was guessed");
		document.Warnings.Should().Contain(w => w.Contains("tns:Missing") && w.Contains("not defined"),
			because: "a missing definition is named");
	}

	[Test]
	[Description("Constructs the wizard does not support are reported: rpc/encoded, a wsdl:import of another WSDL, MIME attachments, xs:choice and XML attributes.")]
	public void Parse_Should_Warn_About_Unsupported_Constructs() {
		// Arrange
		string types = """
			<xs:element name="Get"><xs:complexType><xs:sequence>
				<xs:element name="pick"><xs:complexType><xs:choice><xs:element name="a" type="xs:string" /><xs:element name="b" type="xs:string" /></xs:choice><xs:attribute name="kind" type="xs:string" /></xs:complexType></xs:element>
			</xs:sequence></xs:complexType></xs:element>
			<xs:element name="GetResponse"><xs:complexType><xs:sequence /></xs:complexType></xs:element>
			""";
		string xml = Wsdl(types, "<wsdl:import namespace=\"urn:other\" location=\"http://example.org/other.wsdl\" />"
			+ Messages("Get", "Get", "GetResponse"), SimpleOperation("Get"),
			"<wsdl:operation name=\"Get\"><soap:operation soapAction=\"urn:get\" style=\"rpc\" /><wsdl:input><soap:body use=\"encoded\" /><mime:multipartRelated xmlns:mime=\"http://schemas.xmlsoap.org/wsdl/mime/\" /></wsdl:input></wsdl:operation>");

		// Act
		WsdlDocument document = _sut.Parse([Inline(xml)], "Usr");

		// Assert
		document.Warnings.Should().Contain(w => w.Contains("RPC/encoded"), because: "no xsi:type is ever sent");
		document.Warnings.Should().Contain(w => w.Contains("http://example.org/other.wsdl"),
			because: "an imported WSDL is not followed");
		document.Warnings.Should().Contain(w => w.Contains("MTOM"), because: "attachments go inline");
		document.Warnings.Should().Contain(w => w.Contains("xs:choice"), because: "choice elements are not read");
		document.Warnings.Should().Contain(w => w.Contains("XML attributes"), because: "attributes are never sent");
	}

	[Test]
	[Description("A WSDL 2.0 description, a file without a WSDL service and a file with a DTD are refused with a reason.")]
	public void Parse_Should_Refuse_What_It_Cannot_Read() {
		// Arrange
		WsdlSourceFile wsdl20 = Inline("<description xmlns=\"http://www.w3.org/ns/wsdl\" />");
		WsdlSourceFile schemaOnly = Inline("<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" />");
		WsdlSourceFile dtd = Inline("<!DOCTYPE x [<!ENTITY e \"boom\">]><x>&e;</x>");

		// Act
		Action act20 = () => _sut.Parse([wsdl20], "Usr");
		Action actSchema = () => _sut.Parse([schemaOnly], "Usr");
		Action actDtd = () => _sut.Parse([dtd], "Usr");

		// Assert
		act20.Should().Throw<ArgumentException>(because: "WSDL 2.0 is not supported").WithMessage("*WSDL 2.0*");
		actSchema.Should().Throw<ArgumentException>(because: "a schema alone has no service").WithMessage("*No file holds a WSDL*");
		actDtd.Should().Throw<ArgumentException>(because: "DTD processing is prohibited").WithMessage("*DTD*");
	}

	[Test]
	[Description("A file the platform downloaded is read as ASCII text, so an encoding the declaration names (utf-16) does not break it.")]
	public void Parse_Should_Read_A_Downloaded_File_As_Text() {
		// Arrange
		string xml = Wsdl("<xs:element name=\"Op\"><xs:complexType><xs:sequence><xs:element name=\"x\" type=\"xs:string\" /></xs:sequence></xs:complexType></xs:element>"
			+ "<xs:element name=\"OpResponse\"><xs:complexType><xs:sequence /></xs:complexType></xs:element>",
			Messages("Op", "Op", "OpResponse"), SimpleOperation("Op"),
			"<wsdl:operation name=\"Op\"><soap:operation soapAction=\"urn:op\" /></wsdl:operation>");
		var downloaded = new WsdlSourceFile("http://example.org/op?wsdl",
			Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"utf-16\"?>" + xml), AsciiText: true);

		// Act
		WsdlDocument document = _sut.Parse([downloaded], "Usr");

		// Assert
		Operation(document, "Op").Request.Single().Path.Should().Be("tem:x",
			because: "the platform sends the text as ASCII codes whatever the declaration says");
	}

	[Test]
	[Description("A base type that extends itself through another, and a type that names itself through another, end with a warning instead of exhausting the stack.")]
	public void Parse_Should_Stop_At_Type_Cycles() {
		// Arrange
		string types = """
			<xs:complexType name="A"><xs:complexContent><xs:extension base="tns:B"><xs:sequence><xs:element name="a" type="xs:string" /></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
			<xs:complexType name="B"><xs:complexContent><xs:extension base="tns:A"><xs:sequence><xs:element name="b" type="xs:string" /></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
			<xs:complexType name="C" type="tns:D" /><xs:complexType name="D" type="tns:C" />
			<xs:element name="Op"><xs:complexType><xs:sequence><xs:element name="x" type="tns:A" /><xs:element name="y" type="tns:C" /></xs:sequence></xs:complexType></xs:element>
			<xs:element name="OpResponse"><xs:complexType><xs:sequence /></xs:complexType></xs:element>
			""";
		string xml = Wsdl(types, Messages("Op", "Op", "OpResponse"), SimpleOperation("Op"),
			"<wsdl:operation name=\"Op\"><soap:operation soapAction=\"urn:op\" /></wsdl:operation>");

		// Act
		WsdlDocument document = _sut.Parse([Inline(xml)], "Usr");

		// Assert
		Operation(document, "Op").Request.Select(p => p.Path).Should().Contain(["tem:x/tem:a", "tem:x/tem:b"],
			because: "the elements read before the cycle are kept");
		document.Warnings.Should().Contain(w => w.Contains("extends itself"), because: "the cycle is reported");
	}

	[Test]
	[Description("A WSDL whose types expand past the element budget (distinct types that each nest several fields of the next) is refused instead of exhausting the worker.")]
	public void Parse_Should_Refuse_An_Exponential_Expansion() {
		// Arrange
		var types = new StringBuilder();
		for (int level = 0; level < 12; level++) {
			string fields = string.Concat(Enumerable.Range(0, 5).Select(i =>
				$"<xs:element name=\"f{i}\" type=\"{(level == 11 ? "xs:string" : $"tns:T{level + 1}")}\" />"));
			types.Append($"<xs:complexType name=\"T{level}\"><xs:sequence>{fields}</xs:sequence></xs:complexType>");
		}
		types.Append("<xs:element name=\"Op\"><xs:complexType><xs:sequence><xs:element name=\"root\" type=\"tns:T0\" /></xs:sequence></xs:complexType></xs:element>");
		types.Append("<xs:element name=\"OpResponse\"><xs:complexType><xs:sequence /></xs:complexType></xs:element>");
		string xml = Wsdl(types.ToString(), Messages("Op", "Op", "OpResponse"), SimpleOperation("Op"),
			"<wsdl:operation name=\"Op\"><soap:operation soapAction=\"urn:op\" /></wsdl:operation>");

		// Act
		Action act = () => _sut.Parse([Inline(xml)], "Usr");

		// Assert
		act.Should().Throw<ArgumentException>(because: "5^12 elements cannot be read").WithMessage("*more than 200000 elements*");
	}

	[Test]
	[Description("The wizard's code shortening keeps the last capitalized word whole and the first letter of the others; a name without one is kept.")]
	public void ShortenCaption_Should_Shorten_As_The_Wizard() {
		// Arrange / Act / Assert
		WsdlParser.ShortenCaption("AddResult").Should().Be("AResult", because: "Add becomes A");
		WsdlParser.ShortenCaption("CustomComplexHeaderHeaderAge").Should().Be("CCHHAge", because: "every word but the last is cut");
		WsdlParser.ShortenCaption("intA").Should().Be("intA", because: "no capitalized word of two letters or more");
		WsdlParser.ShortenCaption("getBank.Method").Should().Be("BMethod", because: "only capitalized words are kept");
	}
}
