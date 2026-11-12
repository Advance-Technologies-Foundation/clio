using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.WebServiceDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

/// <summary>Registering and changing SOAP services: the SOAP input, the validator and the SOAP operations.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceSoapWriteTests {

	private static readonly Guid PackageUId = Guid.Parse("6d7929ef-24da-1079-a4e8-dfb3c988735c");

	private IWebServiceDesignerClient _client;
	private Clio.Command.WebServiceDesigner.WebServiceDesigner _sut;
	private ServiceSchemaDto _saved;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IWebServiceDesignerClient>();
		ICreatioRightsClient rights = Substitute.For<ICreatioRightsClient>();
		IWebServicePrefixReader prefix = Substitute.For<IWebServicePrefixReader>();
		rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(true);
		prefix.Read().Returns("Usr");
		_client.IsSchemaNameUnique(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_client.FindExistingSysSettingCodes(Arg.Any<IReadOnlyCollection<string>>())
			.Returns(call => call.Arg<IReadOnlyCollection<string>>());
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		GivenSelect("SysSchema");
		// The read after a save returns what was saved, as the platform does.
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => _saved = call.Arg<ServiceSchemaDto>());
		_client.ReadSchema(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_ => _saved);
		_sut = new Clio.Command.WebServiceDesigner.WebServiceDesigner(_client, rights, prefix);
	}

	private void GivenSelect(string root, params JsonObject[] rows) =>
		_client.Select(root, Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>()).Returns(rows);

	private void GivenRegistered(ServiceSchemaDto schema) {
		_saved = schema;
		GivenSelect("VwWebServiceV2", new JsonObject {
			["UId"] = schema.UId.ToString(), ["PackageUId"] = schema.PackageUId.ToString(), ["Name"] = schema.Name,
			["Caption"] = schema.Name, ["TypeName"] = WebServiceSchemaView.TypeName(schema.Type), ["SysPackageName"] = "Custom"
		});
	}

	private static JsonObject ProcessCalling(Guid serviceUId, string method) => new() {
		["Name"] = "UsrCallerProcess", ["Caption"] = "Caller", ["SysPackageName"] = "Custom",
		["MetaData"] = JsonSerializer.Serialize(new {
			MetaData = new { Schema = new { BK4 = new object[] {
				new { A2 = "CallIt", IX1 = serviceUId.ToString(), IX2 = method }
			} } }
		})
	};

	private static readonly WebServiceNamespaceArg[] Tem = [new("tem", "http://tempuri.org/")];

	private static WebServiceMethodArg AddMethod(params WebServiceParameterArg[] request) =>
		new("UsrAdd", null, null, MessageElement: "tem:Add", SoapAction: "http://tempuri.org/Add",
			RequestParameters: request.Length > 0 ? request : [
				new WebServiceParameterArg("UsrIntA", Type: "Integer", Path: "tem:intA"),
				new WebServiceParameterArg("UsrIntB", Type: "Integer", Path: "tem:intB")
			],
			ResponseParameters: [new WebServiceParameterArg("UsrAddResult", Type: "Integer", Path: "AddResult")]);

	private Action Register(WebServiceMethodArg method, IReadOnlyList<WebServiceNamespaceArg> namespaces = null,
		string type = "soap11") =>
		() => _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://www.dneonline.com/calculator.asmx", null, null, [method],
			type, namespaces ?? Tem));

	[Test]
	[Description("A SOAP 1.1 service is saved with its namespace as the default, the message element as the request uri, POST and xml, the SOAP action, Studio's parameter defaults, and the base URI as given.")]
	public void Register_Should_Save_A_Soap_Service_As_Studio_Does() {
		// Arrange
		WebServiceMethodArg methodArg = AddMethod();

		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://www.dneonline.com/calculator.asmx", null, null, [methodArg], "soap11", Tem));

		// Assert
		result.Success.Should().BeTrue(because: "a valid SOAP service registers");
		_saved.Type.Should().Be(ServiceSchemaCodes.ServiceTypeSoap11, because: "soap11 is SOAP 1.1");
		_saved.BaseUri.Should().Be("http://www.dneonline.com/calculator.asmx",
			because: "a trailing slash would make the .asmx endpoint answer 500");
		ServiceNamespaceDto ns = _saved.Namespaces.Items.Values.Should().ContainSingle(because: "one namespace was given").Subject;
		ns.IsDefault.Should().BeTrue(because: "the first namespace is the default");
		ServiceMethodDto method = _saved.Methods.Items.Values.Single();
		method.Request.Uri.Should().Be("tem:Add", because: "a SOAP method stores its message element where REST keeps the address");
		method.Request.HttpMethodType.Should().Be(ServiceSchemaCodes.HttpMethods["POST"], because: "SOAP is always POST");
		method.Request.ContentType.Should().Be(ServiceSchemaCodes.ContentTypeXml, because: "a SOAP request is XML");
		method.Response.ContentType.Should().Be(ServiceSchemaCodes.ContentTypeXml, because: "a SOAP response is XML");
		method.SoapAction.Should().Be("http://tempuri.org/Add", because: "the SOAP action is stored");
		method.Request.Parameters.Items.Values.Should().OnlyContain(p => p.IsSoapHeader == false && p.SequenceElementName == "item",
			because: "Studio writes these defaults on every SOAP parameter");
		result.Service.Type.Should().Be("SOAP 1.1", because: "the reply is the saved service as describe shows it");
	}

	[Test]
	[Description("Every SOAP rule the platform would accept and then fail at call time is refused, all in one answer, and nothing is saved.")]
	public void Register_Should_Refuse_Every_Broken_Soap_Rule_At_Once() {
		// Arrange
		var method = new WebServiceMethodArg("UsrBad", "GET", "calc", ContentType: "json", MessageElement: "zz:Add",
			RequestParameters: [
				new WebServiceParameterArg("UsrQ", "query", Path: "q"),
				new WebServiceParameterArg("UsrUndeclared", Path: "ns9:Value"),
				new WebServiceParameterArg("UsrCut", Path: "tem:Api.Key"),
				new WebServiceParameterArg("UsrScalar", Path: "tem:S", SequenceElement: "item2"),
				new WebServiceParameterArg("UsrList", Type: "Object", Array: true, Path: "tem:List", Items: [
					new WebServiceParameterArg("UsrField", Path: "tem:Field", SoapHeader: true)
				])
			],
			ResponseParameters: [
				new WebServiceParameterArg("UsrJson", Path: "$.AddResult"),
				new WebServiceParameterArg("UsrHead", Path: "AddResult", SoapHeader: true)
			]);

		// Act
		Action act = Register(method);

		// Assert
		string message = act.Should().Throw<ArgumentException>(because: "the definition breaks SOAP rules").Which.Message;
		message.Should().Contain("has no uri", because: "a SOAP URL is the base URI");
		message.Should().Contain("always POST", because: "Studio fixes the verb");
		message.Should().Contain("always xml", because: "Studio fixes the content type");
		message.Should().Contain("prefix 'zz'", because: "an undeclared message-element prefix fails every call");
		message.Should().Contain("prefix 'ns9'", because: "an undeclared path prefix fails every call");
		message.Should().Contain("not in 'query'", because: "Studio offers body, header and cookie for SOAP");
		message.Should().Contain("tem:Api.Key", because: "the platform would cut the name at the dot");
		message.Should().Contain("not one", because: "sequence-element belongs to an array");
		message.Should().Contain("soap-header on a field of an array item follows its parent",
			because: "a field under a non-header parent cannot be a SOAP header");
		message.Should().Contain("a response parameter cannot be a soap-header",
			because: "a response value is never sent in the request Header");
		message.Should().Contain("'$.AddResult' is a JSONPath", because: "a SOAP response is read by element path");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[TestCase("none", "*needs at least one namespace*", Description = "no namespace")]
	[TestCase("two-defaults", "*at most one namespace as default*", Description = "two defaults")]
	[TestCase("duplicate", "*'tem' is used twice*", Description = "duplicate prefix")]
	[TestCase("no-uri", "*needs a uri*", Description = "namespace without uri")]
	[Description("Namespaces Studio cannot save or the platform cannot call are refused, each with its own reason.")]
	public void Register_Should_Refuse_Broken_Namespaces(string variant, string expected) {
		// Arrange
		WebServiceNamespaceArg[] namespaces = variant switch {
			"none" => [],
			"two-defaults" => [new("tem", "http://tempuri.org/", true), new("ns1", "http://x/", true)],
			"duplicate" => [new("tem", "http://tempuri.org/"), new("tem", "http://x/")],
			_ => [new("tem", " ")]
		};

		// Act
		Action act = Register(AddMethod(), namespaces);

		// Assert
		act.Should().Throw<ArgumentException>(because: $"the namespaces are broken ({variant})")
			.WithMessage(expected, because: "the refusal names the rule the namespaces break");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("When one namespace is marked default it is the default, whatever its position.")]
	public void Register_Should_Keep_The_Marked_Default_Namespace() {
		// Arrange
		WebServiceNamespaceArg[] namespaces = [new("ns1", "http://other/"), new("tem", "http://tempuri.org/", true)];

		// Act
		_sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://x.example/svc", null, null, [AddMethod()], "soap12", namespaces));

		// Assert
		_saved.Type.Should().Be(ServiceSchemaCodes.ServiceTypeSoap12, because: "soap12 is SOAP 1.2");
		_saved.Namespaces.Items.Values.Single(n => n.IsDefault == true).Prefix.Should().Be("tem",
			because: "the namespace marked default wins over the first one");
	}

	[Test]
	[Description("SOAP members on a REST service are refused: namespaces, message-element, soap-action, soap-header and sequence-element.")]
	public void Register_Should_Refuse_Soap_Members_On_A_Rest_Service() {
		// Arrange
		var method = new WebServiceMethodArg("UsrGet", "GET", "orders", MessageElement: "tem:Get", SoapAction: "x",
			RequestParameters: [new WebServiceParameterArg("UsrQ", "query", Path: "q", SoapHeader: true, SequenceElement: "i")]);

		// Act
		Action act = Register(method, Tem, type: null);

		// Assert
		string message = act.Should().Throw<ArgumentException>(because: "a REST service has no SOAP members").Which.Message;
		message.Should().Contain("namespaces belong to a SOAP service", because: "namespaces are SOAP only");
		message.Should().Contain("message-element and soap-action belong to a SOAP service", because: "they are SOAP only");
		message.Should().Contain("soap-header and sequence-element belong to a SOAP service", because: "they are SOAP only");
	}

	[Test]
	[Description("An empty soap-action is saved but the reply says that no SOAPAction header is sent.")]
	public void Register_Should_Notice_An_Empty_Soap_Action() {
		// Arrange
		WebServiceMethodArg method = AddMethod() with { SoapAction = null };

		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://x.example/svc", null, null,
			[method], "soap11", Tem));

		// Assert
		result.Notices.Should().Contain(n => n.StartsWith("Tell the user") && n.Contains("UsrAdd")
				&& n.Contains("no SOAPAction header") && n.Contains("refuse such a call"),
			because: "the notice is passed on whole, risk included: many SOAP 1.1 servers refuse a call without the header");
	}

	[Test]
	[Description("A SOAP-header parameter passes the flag to its items, and a constant default on it is refused like a header constant.")]
	public void Register_Should_Copy_The_Soap_Header_Flag_And_Refuse_A_Header_Constant() {
		// Arrange
		var security = new WebServiceParameterArg("UsrSecurity", Type: "Object", Array: true, Path: "wsse:Security",
			SoapHeader: true, Items: [new WebServiceParameterArg("UsrPassword", Path: "wsse:Password", DefaultSysSetting: "UsrPass")]);
		var constant = new WebServiceParameterArg("UsrMode", Path: "tem:Mode", SoapHeader: true, DefaultValue: "mode-1");
		WebServiceNamespaceArg[] namespaces = [new("tem", "http://tempuri.org/"), new("wsse", "http://wsse/")];

		// Act
		WebServiceToolResult registered = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://x.example/svc", null, null,
			[AddMethod(security)], "soap11", namespaces));
		ServiceParameterDto saved = _saved.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single();
		Action refused = Register(AddMethod(constant), namespaces);

		// Assert
		registered.Success.Should().BeTrue(because: "a SOAP-header object array with a setting-bound field registers");
		saved.IsSoapHeader.Should().BeTrue(because: "the parameter goes in the envelope's Header");
		saved.ItemProperties.Items.Values.Single().IsSoapHeader.Should().BeTrue(because: "Studio copies the flag to items");
		refused.Should().Throw<ArgumentException>(because: "a SOAP-header constant would be a stored credential")
			.WithMessage("*constant default is refused*", because: "the refusal points at default-sys-setting");
	}

	[Test]
	[Description("A describe-web-service reply of a SOAP service sent back as setMethod and setNamespaces changes nothing the describe shows, and keeps every UId.")]
	public void Modify_Should_Round_Trip_A_Soap_Service_From_Describe() {
		// Arrange
		ServiceSchemaDto original = SoapServiceFixture.Schema();
		GivenRegistered(SoapServiceFixture.Schema());
		WebServiceView before = WebServiceSchemaView.From(original, "Custom");
		WebServiceMethodArg methodArg = JsonSerializer.Deserialize<WebServiceMethodArg>(
			JsonSerializer.Serialize(before.Methods.Single()));
		WebServiceNamespaceArg[] namespaceArgs = JsonSerializer.Deserialize<WebServiceNamespaceArg[]>(
			JsonSerializer.Serialize(before.Namespaces));

		// Act
		WebServiceToolResult result = _sut.Modify(original.Name, [
			new WebServiceOperationArg("setNamespaces", Namespaces: namespaceArgs),
			new WebServiceOperationArg("setMethod", methodArg)
		], ["UsrCallerProcess"]);

		// Assert
		result.Success.Should().BeTrue(because: $"describe output is valid modify input; error: {result.Error}");
		JsonSerializer.Serialize(WebServiceSchemaView.From(_saved, "Custom")).Should().Be(JsonSerializer.Serialize(before),
			because: "describe reads the service exactly as before");
		_saved.Methods.Items.Values.Single().Request.Parameters.Items.Keys
			.Should().BeEquivalentTo(original.Methods.Items.Values.Single().Request.Parameters.Items.Keys,
				because: "kept parameters keep their UIds, so calling processes still resolve them");
		_saved.Namespaces.Items.Keys.Should().BeEquivalentTo(original.Namespaces.Items.Keys,
			because: "a namespace keeps its UId by prefix");
		_saved.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrUser").DefValue.Value
			.Should().Be("bob", because: "a SOAP-header constant describe hid is kept when no default is sent");
	}

	[Test]
	[Description("setType switches SOAP 1.2 to 1.1 and counts as changing every method, so a process calling any method must be acknowledged.")]
	public void Modify_SetType_Should_Switch_Version_And_Count_As_Changing_Every_Method() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		GivenRegistered(schema);
		GivenSelect("SysSchema", ProcessCalling(schema.UId, "UsrOp"));

		// Act
		WebServiceToolResult refused = _sut.Modify(schema.Name, [new WebServiceOperationArg("setType", Type: "soap11")], null);
		WebServiceToolResult acknowledged = _sut.Modify(schema.Name, [new WebServiceOperationArg("setType", Type: "soap11")],
			["UsrCallerProcess"]);

		// Assert
		refused.Success.Should().BeFalse(because: "the version changes every call of the service");
		refused.Usages.Should().ContainSingle(u => u.Process == "UsrCallerProcess", because: "the refusal names the caller");
		refused.Error.Should().Contain("change every call", because: "the version or namespaces change every call the caller makes");
		refused.Error.Should().NotContain("lose their parameters",
			because: "the caller's element keeps its parameters (measured), so the refusal does not claim it breaks");
		acknowledged.Success.Should().BeTrue(because: "the user agreed");
		_saved.Type.Should().Be(ServiceSchemaCodes.ServiceTypeSoap11, because: "the service is now SOAP 1.1");
	}

	[Test]
	[Description("A call that changes one method and the SOAP version names both consequences: the caller of the changed method breaks, the other caller's calls change.")]
	public void Modify_Should_Give_Both_Reasons_For_A_Mixed_Change() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		ServiceMethodDto second = SoapServiceFixture.Method();
		second.UId = Guid.NewGuid();
		second.Name = "UsrOther";
		schema.Methods.Items[second.UId.ToString()] = second;
		GivenRegistered(schema);
		JsonObject otherCaller = ProcessCalling(schema.UId, "UsrOther");
		otherCaller["Name"] = "UsrOtherCaller";
		GivenSelect("SysSchema", ProcessCalling(schema.UId, "UsrOp"), otherCaller);
		WebServiceMethodArg methodArg = JsonSerializer.Deserialize<WebServiceMethodArg>(
			JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom").Methods.Single(m => m.Code == "UsrOp")));

		// Act
		WebServiceToolResult result = _sut.Modify(schema.Name, [
			new WebServiceOperationArg("setMethod", methodArg),
			new WebServiceOperationArg("setType", Type: "soap11")
		], null);

		// Assert
		result.Success.Should().BeFalse(because: "both callers need the user's yes");
		result.Error.Should().Contain("lose their parameters", because: "the caller of the changed method may break");
		result.Error.Should().Contain("change every call", because: "the other caller keeps its element but every call changes");
	}

	[Test]
	[Description("A caller whose metadata cannot be read is not said to break when the call only switches the SOAP version.")]
	public void Modify_SetType_Should_Not_Claim_An_Unreadable_Caller_Breaks() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		GivenRegistered(schema);
		GivenSelect("SysSchema", new JsonObject {
			["Name"] = "UsrUnreadable", ["Caption"] = "Unreadable", ["SysPackageName"] = "Custom",
			["MetaData"] = $"not json {schema.UId}"
		});

		// Act
		WebServiceToolResult result = _sut.Modify(schema.Name, [new WebServiceOperationArg("setType", Type: "soap11")], null);

		// Assert
		result.Usages.Should().ContainSingle(u => u.Process == "UsrUnreadable" && u.Method == null,
			because: "a process that names the service but cannot be read counts as calling every method");
		result.Error.Should().NotContain("lose their parameters", because: "no method is changed or removed");
	}

	[TestCase("rest", Description = "SOAP to REST")]
	[TestCase("soap13", Description = "unknown type")]
	[Description("setType refuses a SOAP service becoming REST and an unknown type.")]
	public void Modify_SetType_Should_Refuse_Leaving_Soap(string type) {
		// Arrange
		GivenRegistered(SoapServiceFixture.Schema());

		// Act
		Action act = () => _sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setType", Type: type)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "only 1.1 and 1.2 can be switched");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	private static readonly WebServiceNamespaceArg[] TemAndNs0 =
		[new("tem", "http://tempuri.org/"), new("ns0", "http://example.org/types")];

	private static WebServiceMethodArg RegionsMethod(string sequenceElement) =>
		AddMethod(new WebServiceParameterArg("UsrRegions", Array: true, Path: "tem:Regions", SequenceElement: sequenceElement));

	[TestCase("tem:Region", Description = "a prefixed item element, as Studio's wizard writes it")]
	[TestCase("ns0:string", Description = "an item element in a second declared namespace")]
	[TestCase("Region", Description = "a plain item element")]
	[Description("A request array's sequence-element may be prefix:name with a declared prefix, as Studio writes it, and is stored as given.")]
	public void Register_Should_Accept_A_Prefixed_Sequence_Element(string sequenceElement) {
		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://www.dneonline.com/calculator.asmx", null, null, [RegionsMethod(sequenceElement)], "soap11", TemAndNs0));

		// Assert
		result.Success.Should().BeTrue(because: "the platform builds the item element with its declared prefix");
		_saved.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single().SequenceElementName
			.Should().Be(sequenceElement, because: "the item element is stored as given");
	}

	[Test]
	[Description("A response array's sequence-element is read by local name only, so a prefixed name the request side would refuse is accepted there.")]
	public void Register_Should_Accept_A_Response_Sequence_Element_By_Local_Name() {
		// Arrange
		WebServiceMethodArg method = AddMethod() with {
			ResponseParameters = [new WebServiceParameterArg("UsrRegions", Array: true, Path: "Regions", SequenceElement: "tem:Re.gion")]
		};

		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://www.dneonline.com/calculator.asmx", null, null, [method], "soap11", Tem));

		// Assert
		result.Success.Should().BeTrue(because: "the platform compares only the local name Re.gion of each response item");
	}

	[TestCase("zz:Region", "prefix 'zz'", Description = "an undeclared prefix")]
	[TestCase("tem:Re.gion", "cuts the name", Description = "a name the platform would cut")]
	[TestCase("tem:Region/tem:Item", "cuts the name", Description = "a path where one element is expected")]
	[Description("A request array's sequence-element with an undeclared prefix or a name the platform would cut is refused.")]
	public void Register_Should_Refuse_A_Broken_Sequence_Element(string sequenceElement, string expected) {
		// Act
		Action act = Register(RegionsMethod(sequenceElement), TemAndNs0);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform would fail or send another element")
			.Which.Message.Should().Contain(expected, because: "the refusal says what is wrong with the item element");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("setNamespaces that drops a prefix only a request array's sequence-element uses is refused, naming the prefix.")]
	public void Modify_SetNamespaces_Should_Refuse_Dropping_A_Sequence_Element_Prefix() {
		// Arrange
		_sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://www.dneonline.com/calculator.asmx",
			null, null, [RegionsMethod("ns0:string")], "soap11", TemAndNs0));
		GivenRegistered(_saved);
		_client.ClearReceivedCalls();

		// Act
		Action act = () => _sut.Modify("UsrCalc", [new WebServiceOperationArg("setNamespaces", Namespaces: Tem)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the array items are built in the ns0 namespace")
			.WithMessage("*prefix 'ns0'*", because: "the refusal names the lost prefix");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("setNamespaces that drops a prefix a kept method still uses is refused, naming the method and the prefix.")]
	public void Modify_SetNamespaces_Should_Refuse_Dropping_A_Prefix_In_Use() {
		// Arrange
		GivenRegistered(SoapServiceFixture.Schema());

		// Act
		Action act = () => _sut.Modify("UsrSoapProbe",
			[new WebServiceOperationArg("setNamespaces", Namespaces: [new("tem", "http://example.org/UsrSoapR")])], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the WS-Security elements use the wsse prefix")
			.WithMessage("*prefix 'wsse'*", because: "the refusal names the lost prefix");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[TestCase("setNamespaces")]
	[TestCase("setType")]
	[Description("The SOAP-only operations are refused on a REST service.")]
	public void Modify_Should_Refuse_Soap_Operations_On_A_Rest_Service(string op) {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("UsrGet", "GET", "orders"), null, false, errors);
		var schema = new ServiceSchemaDto {
			UId = Guid.NewGuid(), Name = "UsrRest", PackageUId = PackageUId, Type = ServiceSchemaCodes.ServiceTypeRest,
			BaseUri = "https://erp.example/"
		};
		schema.Methods.Items[method.UId.ToString()] = method;
		GivenRegistered(schema);

		// Act
		Action act = () => _sut.Modify("UsrRest",
			[new WebServiceOperationArg(op, Namespaces: Tem, Type: "soap11")], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: $"{op} does not apply to a REST service")
			.WithMessage(op == "setType" ? "*REST service cannot become SOAP*" : "*REST service has no namespaces*",
				because: "the refusal says why");
	}

	[Test]
	[Description("A namespace row with a null uId or isDefault, or a null row, does not fail the schema read, and setNamespaces keeps the row's collection key as its UId.")]
	public void Namespaces_Should_Tolerate_Null_Members_And_Keep_The_Collection_Key() {
		// Arrange
		const string key = "e34e5bd3-4afe-47a6-9d62-3a2ee09d05f7";
		string json = SoapServiceFixture.PlatformRead
			.Replace($"{{\"uId\": \"{key}\",", "{\"uId\": null,")
			.Replace("\"prefix\": \"tem\", \"isDefault\": true", "\"prefix\": \"tem\", \"isDefault\": null");
		json.Should().Contain("\"uId\": null", because: "the arrange step must really null the namespace members");

		// Act
		ServiceSchemaDto schema = JsonSerializer.Deserialize<ServiceSchemaDto>(json);
		WebServiceView view = WebServiceSchemaView.From(schema, "Custom");
		ServiceItemCollection<ServiceNamespaceDto> rebuilt = WebServiceDefinitionBuilder.BuildNamespaces(
			[new("tem", "http://example.org/UsrSoapR"), new("wsse", "http://wsse/")], schema.Namespaces,
			new WebServiceDefinitionBuilder.Errors());

		// Assert
		view.Namespaces.Should().Contain(n => n.Prefix == "tem" && !n.Default,
			because: "a null isDefault reads as not default instead of failing the read");
		rebuilt.Items.Values.Single(n => n.Prefix == "tem").UId.Should().Be(Guid.Parse(key),
			because: "a row without a uId keeps its identity through the collection key");
	}

	[Test]
	[Description("A SOAP-header parameter with items read back from describe and sent as setMethod saves unchanged: the items carry the flag in describe and it is accepted back there.")]
	public void Modify_Should_Round_Trip_A_Soap_Header_With_Items() {
		// Arrange
		var security = new WebServiceParameterArg("UsrSecurity", Type: "Object", Array: true, Path: "wsse:Security",
			SoapHeader: true, Items: [new WebServiceParameterArg("UsrPassword", Path: "wsse:Password", DefaultSysSetting: "UsrPass")]);
		_sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://x.example/svc", null, null, [AddMethod(security)], "soap11",
			[new("tem", "http://tempuri.org/"), new("wsse", "http://wsse/")]));
		GivenRegistered(_saved);
		WebServiceView before = WebServiceSchemaView.From(_saved, "Custom");
		WebServiceMethodArg methodArg = JsonSerializer.Deserialize<WebServiceMethodArg>(
			JsonSerializer.Serialize(before.Methods.Single()));

		// Act
		WebServiceToolResult result = _sut.Modify("UsrCalc", [new WebServiceOperationArg("setMethod", methodArg)], null);

		// Assert
		before.Methods.Single().RequestParameters.Single().Items.Single().SoapHeader.Should().BeTrue(
			because: "describe reports the flag Studio stores on the items");
		result.Success.Should().BeTrue(because: $"describe output is valid setMethod input; error: {result.Error}");
		JsonSerializer.Serialize(WebServiceSchemaView.From(_saved, "Custom")).Should().Be(JsonSerializer.Serialize(before),
			because: "the header and its item are saved as they were");
	}

	[Test]
	[Description("setNamespaces that really changes the list counts as changing every method for the usage guard; the same list sent back does not.")]
	public void Modify_SetNamespaces_Should_Count_Only_A_Real_Change_For_The_Usage_Guard() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		GivenRegistered(schema);
		GivenSelect("SysSchema", ProcessCalling(schema.UId, "UsrOp"));
		WebServiceNamespaceArg[] same = JsonSerializer.Deserialize<WebServiceNamespaceArg[]>(
			JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom").Namespaces));
		WebServiceNamespaceArg[] changed = [.. same, new("ns9", "http://added/")];

		// Act
		WebServiceToolResult unchanged = _sut.Modify(schema.Name, [new WebServiceOperationArg("setNamespaces", Namespaces: same)], null);
		WebServiceToolResult refused = _sut.Modify(schema.Name, [new WebServiceOperationArg("setNamespaces", Namespaces: changed)], null);

		// Assert
		unchanged.Success.Should().BeTrue(because: "describe's own list sent back changes no call");
		refused.Success.Should().BeFalse(because: "a new namespace changes every envelope the service sends");
		refused.Usages.Should().ContainSingle(u => u.Process == "UsrCallerProcess", because: "the refusal names the caller");
		refused.Error.Should().Contain("change every call", because: "the version or namespaces change every call the caller makes");
		refused.Error.Should().NotContain("lose their parameters",
			because: "the caller's element keeps its parameters (measured), so the refusal does not claim it breaks");
	}

	[TestCase("xmlns", Description = "XML reserved prefix")]
	[TestCase("soapenv", Description = "the platform's envelope prefix")]
	[Description("A reserved namespace prefix is refused.")]
	public void Register_Should_Refuse_A_Reserved_Prefix(string prefix) {
		// Arrange
		WebServiceNamespaceArg[] namespaces = [new(prefix, "http://x/"), new("tem", "http://tempuri.org/")];

		// Act
		Action act = Register(AddMethod(), namespaces);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the envelope would declare the prefix twice")
			.WithMessage($"*prefix '{prefix}' is reserved*", because: "the refusal names the prefix");
	}

	[Test]
	[Description("A soap-action with a line break or credentials, a namespace uri with credentials, a message element with a path and a SOAP base URI placeholder are refused; none of the refused values is echoed.")]
	public void Register_Should_Refuse_Unsafe_Soap_Values() {
		// Arrange
		WebServiceMethodArg method = AddMethod() with { SoapAction = "http://u:pw-123@x/Add\r\n", MessageElement = "tem:Add/tem:X" };
		WebServiceNamespaceArg[] namespaces = [new("tem", "http://user:ns-pass@tempuri.org/")];

		// Act
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://x.example/{version}/svc", null, null,
			[method], "soap11", namespaces));

		// Assert
		string message = act.Should().Throw<ArgumentException>(because: "every value is unsafe").Which.Message;
		message.Should().Contain("soap-action must be one line", because: "a line break would fail every call");
		message.Should().Contain("soap-action must not carry credentials", because: "it is stored and shown");
		message.Should().Contain("the uri must not carry credentials", because: "a namespace uri is stored and shown");
		message.Should().Contain("is one element", because: "the platform reads only the first element of a path");
		message.Should().Contain("{placeholder}", because: "a SOAP method has no path parameter to fill it");
		message.Should().NotContainAny(["pw-123", "ns-pass"], because: "a refused credential is never echoed");
	}

	[Test]
	[Description("A setType with an unknown type reports that error alone.")]
	public void Modify_SetType_Should_Report_An_Unknown_Type_Once() {
		// Arrange
		GivenRegistered(SoapServiceFixture.Schema());

		// Act
		Action act = () => _sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setType", Type: "soap13")], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "soap13 is not a type")
			.Which.Message.Should().NotContain("REST service cannot become SOAP",
				because: "an unknown type is not a REST to SOAP switch");
	}

	private static WebServiceMethodArg DescribedOp(ServiceSchemaDto schema) =>
		JsonSerializer.Deserialize<WebServiceMethodArg>(
			JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom").Methods.Single(m => m.Code == "UsrOp")));

	[Test]
	[Description("A visible body constant is not carried into the SOAP Header when setMethod turns the parameter into a soap-header parameter without a default: a Header constant cannot be made in two calls.")]
	public void Modify_Should_Not_Carry_A_Visible_Constant_Into_The_Soap_Header() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		ServiceParameterDto plain = schema.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrA");
		plain.DefValue = new ServiceParameterValueDto { Source = ServiceSchemaCodes.ValueSourceConst, Value = "LK-123" };
		GivenRegistered(schema);
		WebServiceMethodArg methodArg = DescribedOp(schema);
		methodArg = methodArg with {
			RequestParameters = methodArg.RequestParameters
				.Select(p => p.Code == "UsrA" ? p with { SoapHeader = true, DefaultValue = null } : p).ToList()
		};

		// Act
		WebServiceToolResult result = _sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setMethod", methodArg)], null);

		// Assert
		result.Success.Should().BeTrue(because: "moving a parameter into the Header without a default is a valid change");
		_saved.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrA").DefValue.Value
			.Should().NotBe("LK-123", because: "describe showed the constant, so it was never hidden and is not carried into the Header");
	}

	[Test]
	[Description("A constant hidden because its parent travelled in the SOAP Header is refused, not erased, when setMethod takes the parent out of the Header.")]
	public void Modify_Should_Refuse_Showing_A_Constant_Hidden_By_A_Soap_Header_Parent() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		ServiceParameterDto lines = schema.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrLines");
		lines.IsSoapHeader = true;
		ServiceParameterDto item = lines.ItemProperties.Items.Values.Single();
		item.Name = "UsrLineMode";
		item.Path = "tem:Mode";
		item.DefValue = new ServiceParameterValueDto { Source = ServiceSchemaCodes.ValueSourceConst, Value = "item-mode-1" };
		GivenRegistered(schema);
		WebServiceMethodArg methodArg = DescribedOp(schema);
		methodArg = methodArg with {
			RequestParameters = methodArg.RequestParameters
				.Select(p => p.Code == "UsrLines"
					? p with { SoapHeader = null, Items = p.Items.Select(i => i with { SoapHeader = null }).ToList() }
					: p).ToList()
		};

		// Act
		Action act = () => _sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setMethod", methodArg)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "out of the Header the next describe would print the constant")
			.WithMessage("*would show it*", because: "the refusal says the constant would become visible");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[TestCase("http://svc:p@ss@host/Add", Description = "a password with @")]
	[TestCase("http://svc:p#ss@host/Add", Description = "a password with # that Uri cannot parse")]
	[TestCase("http://svc:p/ss@host/Add", Description = "a password with /")]
	[Description("A soap-action with user information is refused even when the password makes it unparseable as a URI.")]
	public void Register_Should_Refuse_A_Soap_Action_With_Any_User_Info(string soapAction) {
		// Arrange
		WebServiceMethodArg method = AddMethod() with { SoapAction = soapAction };

		// Act
		Action act = Register(method);

		// Assert
		act.Should().Throw<ArgumentException>(because: "a soap-action is stored and printed by describe")
			.Which.Message.Should().Contain("must not carry credentials", because: "the refusal names the rule")
			.And.NotContain("p#ss", because: "a refused value is never echoed");
	}

	[Test]
	[Description("describe hides user information in a namespace uri even when the password makes it unparseable as a URI.")]
	public void Describe_Should_Hide_Unparseable_User_Info_In_A_Namespace() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		schema.Namespaces.Items.Values.Single(n => n.Prefix == "tem").Name = "http://svc:p#ss@host/ns";

		// Act
		string view = JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom"));

		// Assert
		view.Should().NotContain("p#ss", because: "the password must not reach the agent");
		view.Should().Contain("[redacted]", because: "the user information is replaced, not dropped silently");
	}

	[Test]
	[Description("setNamespaces cannot drop a prefix a kept method uses only in a segment the platform still reads with that prefix (tem:Api.Key renders as tem:Api).")]
	public void Modify_SetNamespaces_Should_Count_A_Prefix_The_Platform_Reads_In_A_Cut_Segment() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		Guid ns2 = Guid.NewGuid();
		schema.Namespaces.Items[ns2.ToString()] = new ServiceNamespaceDto {
			UId = ns2, Prefix = "ns2", Name = "http://example.org/ns2", IsDefault = false
		};
		schema.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrA").Path = "ns2:Api.Key";
		GivenRegistered(schema);
		WebServiceNamespaceArg[] kept = [
			new("tem", "http://example.org/UsrSoapR", true),
			new("wsse", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")
		];

		// Act
		Action act = () => _sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setNamespaces", Namespaces: kept)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform still looks ns2 up for that element")
			.WithMessage("*prefix 'ns2'*", because: "the refusal names the lost prefix");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("A SOAP base URI keeps a fixed query, as SAP endpoints need (?sap-client=100): the platform calls it as stored and SOAP has no query parameters.")]
	public void Register_Should_Keep_A_Query_In_A_Soap_Base_Uri() {
		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://sap.example:8000/sap/bc/srt/rfc/sap/zws/100/zws/zws?sap-client=100", null, null, [AddMethod()],
			"soap11", Tem));

		// Assert
		result.Success.Should().BeTrue(because: "the query is the only place a SOAP endpoint can carry it");
		_saved.BaseUri.Should().Be("http://sap.example:8000/sap/bc/srt/rfc/sap/zws/100/zws/zws?sap-client=100",
			because: "a SOAP base URI is stored as given");
	}

	[Test]
	[Description("A credential-like key with a constant value in a SOAP base URI query is refused without echoing the value.")]
	public void Register_Should_Refuse_A_Credential_In_A_Soap_Base_Uri_Query() {
		// Act
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://x.example/svc.asmx?api_key=s3cr3t-v", null, null, [AddMethod()], "soap11", Tem));

		// Assert
		act.Should().Throw<ArgumentException>(because: "list and describe would print the key")
			.Which.Message.Should().Contain("looks like a credential", because: "the refusal names the rule")
			.And.NotContain("s3cr3t-v", because: "a refused value is never echoed");
	}

	[Test]
	[Description("An unprefixed element name with non-ASCII letters is accepted: it goes to the default namespace as written.")]
	public void Register_Should_Accept_A_Unicode_Element_Name() {
		// Arrange
		WebServiceMethodArg method = new("UsrGet", null, null, MessageElement: "ПолучитьДанные",
			RequestParameters: [new WebServiceParameterArg("UsrStart", Path: "ДатаНачала")],
			ResponseParameters: [new WebServiceParameterArg("UsrResult", Path: "Результат")]);

		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://x.example/ws/data.1cws", null, null, [method], "soap12", Tem));

		// Assert
		result.Success.Should().BeTrue(because: "XmlDocument accepts Unicode letters in an unprefixed element name");
	}

	[Test]
	[Description("An empty sequence-element on a response array survives a describe and setMethod round trip instead of becoming item.")]
	public void Modify_Should_Keep_An_Empty_Response_Sequence_Element() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		var products = new ServiceParameterDto {
			UId = Guid.NewGuid(), Type = ServiceSchemaCodes.Placements["body"], Name = "UsrProducts", Path = "Products",
			DataValueTypeName = "Text", IsArray = true, SequenceElementName = "", IsSoapHeader = false,
			DefValue = new ServiceParameterValueDto { Source = 0 },
			Caption = new Dictionary<string, string> { ["en-US"] = "UsrProducts" },
			ItemProperties = new ServiceItemCollection<ServiceParameterDto>()
		};
		schema.Methods.Items.Values.Single().Response.Parameters.Items[products.UId.ToString()] = products;
		GivenRegistered(schema);

		// Act
		WebServiceToolResult result = _sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setMethod", DescribedOp(schema))], null);

		// Assert
		result.Success.Should().BeTrue(because: "describe output is valid setMethod input");
		_saved.Methods.Items.Values.Single().Response.Parameters.Items.Values.Single(p => p.Name == "UsrProducts")
			.SequenceElementName.Should().Be("", because: "an empty item name reads every child element; item would read nothing");
	}

	[Test]
	[Description("A SOAP 1.2 service gets no empty-soap-action notice; switching it to SOAP 1.1 gives one for every method without an action.")]
	public void SoapActionNotice_Should_Follow_The_Soap_Version() {
		// Arrange
		WebServiceMethodArg method = AddMethod() with { SoapAction = null };

		// Act
		WebServiceToolResult registered = _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://x.example/svc", null, null, [method], "soap12", Tem));
		GivenRegistered(_saved);
		WebServiceToolResult switched = _sut.Modify("UsrCalc", [new WebServiceOperationArg("setType", Type: "soap11")], null);

		// Assert
		(registered.Notices ?? []).Should().NotContain(n => n.Contains("no SOAPAction header"),
			because: "the SOAPAction header is the SOAP 1.1 convention");
		switched.Notices.Should().Contain(n => n.Contains("UsrAdd") && n.Contains("no SOAPAction header"),
			because: "after the switch to SOAP 1.1 the missing action matters for a method this call did not build");
	}

	[Test]
	[Description("A stored namespace list without a default, sent back from describe unchanged, is not a change of every call.")]
	public void Modify_SetNamespaces_Should_Read_A_Stored_List_Without_Default_As_Unchanged() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		foreach (ServiceNamespaceDto ns in schema.Namespaces.Items.Values) {
			ns.IsDefault = null;
		}
		GivenRegistered(schema);
		GivenSelect("SysSchema", ProcessCalling(schema.UId, "UsrOp"));
		WebServiceNamespaceArg[] described = WebServiceSchemaView.From(schema, "Custom").Namespaces
			.Select(n => new WebServiceNamespaceArg(n.Prefix, n.Uri, n.Default ? true : null)).ToArray();

		// Act
		WebServiceToolResult result = _sut.Modify(schema.Name,
			[new WebServiceOperationArg("setNamespaces", Namespaces: described)], null);

		// Assert
		result.Success.Should().BeTrue(because: "the list the agent sent back is the one describe showed");
		(result.Usages ?? []).Should().BeEmpty(because: "nothing changes the calls, so no caller is asked about");
	}

	[TestCase("?wsdl", Description = "the WSDL address")]
	[TestCase("?singleWsdl", Description = "the single-file WSDL address")]
	[Description("A SOAP base URI that is the WSDL address is refused: every call would be posted to it.")]
	public void Register_Should_Refuse_A_Wsdl_Address_As_Base_Uri(string query) {
		// Act
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrCalc", null,
			"http://x.example/svc.asmx" + query, null, null, [AddMethod()], "soap11", Tem));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the WSDL URL is not the endpoint")
			.WithMessage("*address of the WSDL*", because: "the refusal says to give the endpoint instead");
	}

	[Test]
	[Description("A blank sequence-element on a request array is stored as item: the platform cannot create an element without a name.")]
	public void Register_Should_Store_Item_For_A_Blank_Request_Sequence_Element() {
		// Act
		_sut.Register("Custom", new WebServiceRegistration("UsrCalc", null, "http://x.example/svc", null, null,
			[AddMethod(new WebServiceParameterArg("UsrRegions", Array: true, Path: "tem:Regions", SequenceElement: " "))],
			"soap11", Tem));

		// Assert
		_saved.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single().SequenceElementName
			.Should().Be("item", because: "an empty request item name would fail every call");
	}

	[Test]
	[Description("On a SOAP service the single-Object refusal advises an element path, not a dotted path, since a dot is part of an element name.")]
	public void Register_Should_Advise_An_Element_Path_For_A_Single_Soap_Object() {
		// Arrange
		WebServiceMethodArg method = AddMethod(new WebServiceParameterArg("UsrCustomer", Type: "Object", Path: "tem:Customer",
			Items: [new WebServiceParameterArg("UsrName", Path: "tem:Name")]));

		// Act
		Action act = Register(method);

		// Assert
		act.Should().Throw<ArgumentException>(because: "a single Object is refused")
			.Which.Message.Should().Contain("tem:Customer/tem:Name", because: "SOAP nests by element path")
			.And.NotContain("customer.name", because: "a dotted path would be one element named customer.name");
	}

	[Test]
	[Description("A constant on an item of a SOAP-header parameter is hidden by describe even when the item itself lacks the flag, as the platform renders it in the Header.")]
	public void Describe_Should_Hide_A_Constant_Under_A_Soap_Header_Parent() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		ServiceParameterDto lines = schema.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrLines");
		lines.IsSoapHeader = true;
		ServiceParameterDto item = lines.ItemProperties.Items.Values.Single();
		item.IsSoapHeader = false;
		item.Name = "UsrLineMode";
		item.Path = "tem:Mode";
		item.DefValue = new ServiceParameterValueDto { Source = ServiceSchemaCodes.ValueSourceConst, Value = "item-mode-1" };

		// Act
		string view = JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom"));

		// Assert
		view.Should().NotContain("item-mode-1", because: "the item travels in the Header with its parent, whatever its name");
	}

	[Test]
	[Description("A refusal lists at most the cap of problems and says how many more there are.")]
	public void Errors_Should_Cap_The_Refusal() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		errors.AddRange(Enumerable.Range(1, WebServiceDefinitionBuilder.Errors.MaxReported + 7).Select(i => $"problem {i}."));

		// Act
		Action act = errors.ThrowIfAny;

		// Assert
		string message = act.Should().Throw<ArgumentException>(because: "there are problems").Which.Message;
		message.Should().EndWith("...and 7 more problems.", because: "the extra problems are counted, not listed");
		message.Should().NotContain($"problem {WebServiceDefinitionBuilder.Errors.MaxReported + 1}.",
			because: "a huge definition must not make the refusal huge");
	}

	[Test]
	[Description("A namespace row read with a null uId or isDefault is written back without those members, never as null.")]
	public void Namespaces_Should_Leave_Null_Members_Out_On_Write() {
		// Arrange
		ServiceSchemaDto schema = JsonSerializer.Deserialize<ServiceSchemaDto>(SoapServiceFixture.PlatformRead
			.Replace("{\"uId\": \"e34e5bd3-4afe-47a6-9d62-3a2ee09d05f7\",", "{\"uId\": null,")
			.Replace("\"prefix\": \"tem\", \"isDefault\": true", "\"prefix\": \"tem\", \"isDefault\": null"));

		// Act
		string written = JsonSerializer.Serialize(schema.Namespaces.Items.Values.Single(n => n.Prefix == "tem"));

		// Assert
		written.Should().NotContain("uId", because: "the platform's contract has a non-null Guid there");
		written.Should().NotContain("isDefault", because: "the platform's contract has a non-null boolean there");
		written.Should().Contain("\"prefix\":\"tem\"", because: "the members that were read are kept");
	}

	[TestCase("UsrLicenseType", true, Description = "licence spelt with s")]
	[TestCase("tem:Licence", true, Description = "licence spelt with c")]
	[TestCase("UsrNonceLength", true, Description = "nonce")]
	[TestCase("tem:pass", true, Description = "a pass element")]
	[TestCase("pass", true, Description = "pass alone")]
	[TestCase("UsrPass", true, Description = "a code ending in Pass")]
	[TestCase("AdminPass", true, Description = "an admin pass")]
	[TestCase("SmtpPass", true, Description = "an SMTP pass")]
	[TestCase("tem:AdminPass", true, Description = "a prefixed element ending in Pass")]
	[TestCase("UsrPasscode", true, Description = "passcode")]
	[TestCase("tem:PassPhrase", true, Description = "passphrase")]
	[TestCase("tem:Pass_Word", true, Description = "pass followed by an underscore")]
	[TestCase("tem:Pass1", true, Description = "pass followed by a digit")]
	[TestCase("tem:Bearer", true, Description = "bearer")]
	[TestCase("UsrJwt", true, Description = "jwt")]
	[TestCase("UsrSmtpPassValue", true, Description = "pass ending a camelCase word")]
	[TestCase("ApiPassId", true, Description = "pass followed by a capital")]
	[TestCase("UsrPassport", false, Description = "passport")]
	[TestCase("UsrPassenger", false, Description = "passenger")]
	[TestCase("UsrCompass", false, Description = "compass")]
	[TestCase("UsrBypass", false, Description = "bypass")]
	[Description("The credential-name pattern also catches licence, nonce, passcode, passphrase, bearer, jwt and pass not followed by a letter, REST parameters included, and leaves passport, passenger, compass and bypass alone.")]
	public void IsSecretName_Should_Match_Credential_Words_Only(string name, bool expected) {
		// Act
		bool secret = WebServiceSchemaView.IsSecretName(name);

		// Assert
		secret.Should().Be(expected, because: "a constant on a credential-like parameter is refused and hidden; other words are not");
	}

	[Test]
	[Description("A SOAP setBaseUri stores the URI as given, without adding a trailing slash.")]
	public void Modify_SetBaseUri_Should_Keep_A_Soap_Uri_As_Given() {
		// Arrange
		GivenRegistered(SoapServiceFixture.Schema());

		// Act
		_sut.Modify("UsrSoapProbe", [new WebServiceOperationArg("setBaseUri", BaseUri: "http://x.example/service.asmx")], null);

		// Assert
		_saved.BaseUri.Should().Be("http://x.example/service.asmx", because: "a SOAP call goes to the base URI as given");
	}
}
