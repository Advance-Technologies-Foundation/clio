using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

/// <summary>clio's rules on top of the wizard's reading (ADR D11): codes, message elements and what is left out.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WsdlRegistrationMapperTests {

	private static WsdlDocument Fixture(string name) {
		string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", "Wsdl", name);
		return new WsdlParser().Parse([new WsdlSourceFile(path, File.ReadAllBytes(path))], "Usr");
	}

	private static WsdlParameter Parameter(string caption, string path, string type = "Text", bool array = false,
		string sequence = null, bool copy = false, params WsdlParameter[] items) =>
		new(caption, path, type, array, false, sequence ?? path, items, "Usr" + caption, copy);

	private static WsdlOperation Operation(string caption, params WsdlParameter[] request) =>
		new(caption, "Usr" + caption, "urn:" + caption, caption, "http://example.org/t/", request, []);

	private static WsdlPort Port(params WsdlOperation[] operations) =>
		new("Soap", "Calc", 2, "http://localhost/calc.asmx",
			[new WsdlNamespace("tem", "http://example.org/t/"), new WsdlNamespace("ns0", "http://example.org/x")], operations);

	[Test]
	[Description("An array whose item element the platform cannot name is left out, as is an array inside an array item (the wizard gives it the outer path); the rest of the method is kept.")]
	public void Map_Should_Leave_Out_Arrays_Register_Would_Refuse() {
		// Arrange
		WsdlParameter dottedItem = Parameter("tags", "tem:tags", array: true, sequence: "tem:tag.value");
		WsdlParameter escaping = new("subs", "tem:order/tem:lines", "Object", true, false, "tem:subs",
			[Parameter("code", "tem:code")], "Usrsubs", EscapesItem: true);
		WsdlPort port = Port(Operation("Op", dottedItem, Parameter("id", "tem:id"), escaping));
		var warnings = new List<string>();

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", null, warnings);

		// Assert
		result.Arguments.Methods.Single().RequestParameters.Select(p => p.Path).Should().Equal(["tem:id"],
			because: "only the parameter register accepts is kept");
		warnings.Should().Contain(w => w.Contains("tem:tag.value"), because: "the item element is named");
		warnings.Should().Contain(w => w.Contains("outer array's path"), because: "the misplaced array is explained");
	}

	[Test]
	[Description("A port address register would refuse is kept with a warning to replace it, and a soapAction with a line break is left out with a warning.")]
	public void Map_Should_Warn_About_Service_Values_Register_Would_Refuse() {
		// Arrange
		var port = new WsdlPort("Soap", "Calc", 2, "REPLACE_WITH_ACTUAL_URL",
			[new WsdlNamespace("tem", "http://example.org/t/")],
			[new WsdlOperation("Op", "UsrOp", "urn:op\nnext", "Op", "http://example.org/t/", [Parameter("x", "tem:x")], [])]);
		var warnings = new List<string>();

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", null, warnings);

		// Assert
		result.Arguments.BaseUri.Should().Be("REPLACE_WITH_ACTUAL_URL", because: "only the user can give the real endpoint");
		warnings.Should().Contain(w => w.Contains("REPLACE_WITH_ACTUAL_URL") && w.Contains("base-uri"),
			because: "the agent is told to replace it");
		result.Arguments.Methods.Single().SoapAction.Should().BeNull(because: "a header value cannot hold a line break");
		warnings.Should().Contain(w => w.Contains("soapAction"), because: "the dropped action is reported");
	}

	[Test]
	[Description("With a target service, a parameter at the same path as one of the matched method's keeps that code, so setMethod keeps its UId; a new parameter avoids the method's codes.")]
	public void Map_Should_Reuse_Parameter_Codes_Of_A_Matched_Method() {
		// Arrange
		WsdlPort port = Port(Operation("Add", Parameter("intA", "tem:intA"), Parameter("intB", "tem:intB")));
		WebServiceParameterView View(string code, string path) =>
			new(code, code, "body", path, "Text", false, false, null, null, false, []);
		var target = new WsdlTargetService("UsrCalc", [new WebServiceNamespaceView("tem", "http://example.org/t/", true)], [
			new WebServiceMethodView("UsrAdd_1a2b3c4d", "Add", "POST", null, 5000, "xml", false,
				[View("UsrintA1", "tem:intA"), View("UsrintB", "tem:other")], [], "Add", "urn:Add")
		]);

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", target, new List<string>());

		// Assert
		result.Arguments.Methods.Single().RequestParameters.Select(p => p.Code).Should().Equal(["UsrintA1", "UsrintB2"],
			because: "intA keeps the code it has at tem:intA, and intB avoids the code another parameter of the method holds");
	}

	[Test]
	[Description("A WSDL prefix that a target service uses for another namespace gets the next free ns-prefix.")]
	public void Map_Should_Rename_A_Prefix_The_Target_Uses_Otherwise() {
		// Arrange
		WsdlPort port = Port(Operation("Op", Parameter("x", "ns0:x")));
		var target = new WsdlTargetService("UsrCalc", [
			new WebServiceNamespaceView("tem", "http://example.org/t/", true),
			new WebServiceNamespaceView("ns0", "http://example.org/elsewhere", false)
		], []);

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", target, new List<string>());

		// Assert
		result.Arguments.Namespaces.Select(n => (n.Prefix, n.Uri)).Should().Contain(("ns1", "http://example.org/x"),
			because: "ns0 is taken by another namespace of the service");
		result.Arguments.Methods.Single().RequestParameters.Single().Path.Should().Be("ns1:x",
			because: "the path follows the renamed prefix");
	}

	[Test]
	[Description("Without a prefix, a code whose 200-character cut starts with a digit gets an underscore, so it stays an identifier.")]
	public void Map_Should_Keep_A_Cut_Code_An_Identifier() {
		// Arrange
		string name = new string('a', 50) + "9" + new string('b', 199);

		// Act
		string code = WsdlRegistrationMapper.UniqueCode(string.Empty, name, "Param", new HashSet<string>());

		// Assert
		code.Should().MatchRegex("^_[A-Za-z0-9_]*$", because: "an identifier cannot start with a digit").And.HaveLength(200,
			because: "the cap still holds");
	}

	[Test]
	[Description("Codes are the prefix plus the wizard's shortened name, without its random suffix, so every read gives the same codes; the message element gets the prefix of its namespace.")]
	public void Map_Should_Give_Stable_Prefixed_Codes_And_A_Prefixed_Message_Element() {
		// Arrange
		WsdlDocument document = Fixture("calculator.wsdl");
		var warnings = new List<string>();

		// Act
		WsdlServiceArguments first = WsdlRegistrationMapper.Map(document.Ports[0], false, "Usr", "dev", null, warnings);
		WsdlServiceArguments second = WsdlRegistrationMapper.Map(document.Ports[0], false, "Usr", "dev", null, warnings);

		// Assert
		WebServiceMethodArg add = first.Arguments.Methods.Single(m => m.Caption == "Add");
		(add.Code, add.MessageElement, add.SoapAction).Should().Be(("UsrAdd", "tem:Add", "http://tempuri.org/Add"),
			because: "operation Add is UsrAdd on every read (D11) and its root element is qualified");
		add.RequestParameters.Select(p => (p.Code, p.Path, p.Type)).Should().Equal(
			[("UsrintA", "tem:intA", "Integer"), ("UsrintB", "tem:intB", "Integer")],
			because: "parameter codes follow the wizard's names");
		add.ResponseParameters.Single().Code.Should().Be("UsrAResult", because: "AddResult is shortened as the wizard does");
		first.Arguments.Should().BeEquivalentTo(second.Arguments, because: "the codes do not change from one read to the next");
		(first.Arguments.Code, first.Arguments.Type, first.Arguments.Caption).Should().Be(("UsrCalculator", "soap11", "Calculator"),
			because: "the service code is the prefix and the WSDL service name");
	}

	[Test]
	[Description("With an empty schema-name prefix the codes are the names alone; a name that would start with a digit gets an underscore.")]
	public void Map_Should_Work_Without_A_Prefix() {
		// Arrange
		WsdlPort port = Port(Operation("Add", Parameter("1st", "tem:first")));

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, string.Empty, "dev", null, new List<string>());

		// Assert
		WebServiceMethodArg method = result.Arguments.Methods.Single();
		method.Code.Should().Be("Add", because: "no prefix is added when the environment has none");
		method.RequestParameters.Single().Code.Should().Be("_1st", because: "a code must start with a letter or underscore");
	}

	[Test]
	[Description("A code taken by another operation or parameter gets a number from 2, compared case-insensitively; a name with characters outside Latin letters, digits and underscore loses them.")]
	public void Map_Should_Number_Collisions_From_Two() {
		// Arrange
		WsdlPort port = Port(Operation("Add", Parameter("value", "tem:value"), Parameter("Value", "tem:Value"),
				Parameter("va-lue", "tem:va-lue")),
			Operation("add"), Operation("a-dd"));

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", null, new List<string>());

		// Assert
		result.Arguments.Methods.Select(m => m.Code).Should().Equal(["UsrAdd", "Usradd2", "Usradd3"],
			because: "Add, add and a-dd clash case-insensitively once the dash is dropped");
		result.Arguments.Methods[0].RequestParameters.Select(p => p.Code).Should().Equal(
			["Usrvalue", "UsrValue2", "Usrvalue3"], because: "parameter codes are numbered the same way");
	}

	[Test]
	[Description("A code over 200 characters keeps the prefix and the end of the name, where the wizard's cap drops the prefix.")]
	public void Map_Should_Keep_The_Prefix_On_A_Long_Code() {
		// Arrange
		WsdlOperation ping = Fixture("wizard-features.wsdl").Ports[0].Operations.Single(o => o.Caption == "Ping");

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(Port(ping), true, "Usr", "dev", null, new List<string>());

		// Assert
		string code = result.Arguments.Methods.Single().RequestParameters.Single().Code;
		code.Should().HaveLength(200, because: "codes are capped at 200 characters");
		code.Should().StartWith("Usr", because: "register-web-service refuses a code without the prefix");
		code.Should().EndWith("front_of_the_code", because: "the end of the name is kept, as the wizard keeps it");
	}

	[Test]
	[Description("What register-web-service would refuse is left out with a warning: the wizard's copies of nested elements, a request element the platform would cut at a dot, an operation whose root element has a dot, an object without fields and a repeated element without a wrapper.")]
	public void Map_Should_Leave_Out_What_Register_Refuses() {
		// Arrange
		WsdlPort port = Port(
			Operation("Keep", Parameter("id", "tem:order/tem:id"), Parameter("copy", "tem:order/tem:Tenant", copy: true),
				Parameter("dotted", "tem:blz.parameter"), Parameter("empty", "tem:empty", "Object"),
				Parameter(string.Empty, string.Empty, array: true)),
			new WsdlOperation("get.Bank", "UsrBank", null, "getBank.Method", "http://example.org/t/", [], []));
		var warnings = new List<string>();

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", null, warnings);

		// Assert
		result.Arguments.Methods.Should().ContainSingle(because: "the platform would send another root element for get.Bank")
			.Which.RequestParameters.Select(p => p.Path).Should().Equal(["tem:order/tem:id"],
				because: "only the element register-web-service accepts is kept");
		warnings.Should().Contain(w => w.Contains("tem:order/tem:Tenant") && w.Contains("second time"),
			because: "a copy is named and explained");
		warnings.Should().Contain(w => w.Contains("tem:blz.parameter") && w.Contains("cuts"), because: "a cut name is named");
		warnings.Should().Contain(w => w.Contains("tem:empty") && w.Contains("no readable fields"), because: "an empty object is named");
		warnings.Should().Contain(w => w.Contains("without a wrapper"), because: "an empty path is explained");
		warnings.Should().Contain(w => w.Contains("get.Bank") && w.Contains("left out"), because: "a dropped method is named");
	}

	[Test]
	[Description("Optional members are written only where they apply: soap-header on header elements, sequence-element on arrays, array only when true, soap-action only when the WSDL has one.")]
	public void Map_Should_Write_Soap_Members_Only_Where_They_Apply() {
		// Arrange
		WsdlOperation secure = Fixture("wizard-features.wsdl").Ports[0].Operations.Single(o => o.Caption == "TaggedCall");
		WsdlOperation noAction = new("Plain", "UsrPlain", null, "Plain", "http://example.org/t/",
			[Parameter("x", "tem:x")], []);

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(Port(secure, noAction), true, "Usr", "dev", null,
			new List<string>());

		// Assert
		WebServiceParameterArg tags = result.Arguments.Methods[0].RequestParameters.Single();
		(tags.SoapHeader, tags.Array, tags.SequenceElement).Should().Be((true, true, "tem:Tag"),
			because: "a header array keeps its flag and its item element");
		WebServiceParameterArg x = result.Arguments.Methods[1].RequestParameters.Single();
		(x.SoapHeader, x.Array, x.SequenceElement).Should().Be((null, null, null),
			because: "a plain body element carries none of the SOAP members");
		result.Arguments.Methods[1].SoapAction.Should().BeNull(because: "no soap-action is made up");
	}

	[Test]
	[Description("An element with no namespace loses its prefix, because the platform cannot declare an empty namespace, with a warning.")]
	public void Map_Should_Drop_The_Prefix_Of_An_Element_Without_Namespace() {
		// Arrange
		var port = new WsdlPort("Soap", "Calc", 2, "http://localhost/calc",
			[new WsdlNamespace("tem", "http://example.org/t/"), new WsdlNamespace("ns0", string.Empty)],
			[Operation("Op", Parameter("x", "tem:wrap/ns0:x"))]);
		var warnings = new List<string>();

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(port, true, "Usr", "dev", null, warnings);

		// Assert
		result.Arguments.Namespaces.Select(n => n.Prefix).Should().Equal(["tem"], because: "an empty namespace cannot be declared");
		result.Arguments.Methods.Single().RequestParameters.Single().Path.Should().Be("tem:wrap/x",
			because: "an unprefixed element is the closest the platform can send");
		warnings.Should().Contain(w => w.Contains("no namespace"), because: "the agent is told to test a call");
	}

	[Test]
	[Description("With a target service, an operation whose root element and SOAP action match one of its methods keeps that method's code, another one is numbered past the codes the service has, and the namespaces start with the service's own prefixes.")]
	public void Map_Should_Reuse_The_Codes_Of_A_Target_Service() {
		// Arrange
		WsdlDocument document = Fixture("calculator.wsdl");
		var target = new WsdlTargetService("UsrCalcOld",
			[new WebServiceNamespaceView("t", "http://tempuri.org/", true)],
			[
				new WebServiceMethodView("UsrAdd_1a2b3c4d", "Add", "POST", null, 5000, "xml", false, [], [], "Add",
					"http://tempuri.org/Add"),
				new WebServiceMethodView("UsrSubtract", "Other", "POST", null, 5000, "xml", false, [], [], "Other",
					"http://tempuri.org/Other")
			]);

		// Act
		WsdlServiceArguments result = WsdlRegistrationMapper.Map(document.Ports[0], false, "Usr", "dev", target,
			new List<string>());

		// Assert
		result.Arguments.Code.Should().Be("UsrCalcOld", because: "the operations go into the target service");
		result.ExistingMethods.Should().Equal(["UsrAdd_1a2b3c4d"], because: "Add is the same operation as the stored method");
		result.Arguments.Methods.Single(m => m.Caption == "Add").Code.Should().Be("UsrAdd_1a2b3c4d",
			because: "a matched method keeps its code, so setMethod replaces it");
		result.Arguments.Methods.Single(m => m.Caption == "Subtract").Code.Should().Be("UsrSubtract2",
			because: "UsrSubtract belongs to another operation of the service");
		result.Arguments.Namespaces.Select(n => (n.Prefix, n.Uri)).Should().Equal([("t", "http://tempuri.org/")],
			because: "the service's own prefix is reused for the same namespace");
		result.Arguments.Methods.Single(m => m.Caption == "Add").MessageElement.Should().Be("t:Add",
			because: "paths and the message element use the service's prefix");
	}
}
