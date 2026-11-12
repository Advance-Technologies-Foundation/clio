using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

/// <summary>The SOAP read side: the view, the wire round trip and the XML-aware masking.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceSoapTests {

	private static ServiceSchemaDto Rest() {
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrGet", "GET", "orders",
			RequestParameters: [new WebServiceParameterArg("UsrIds", "query", Path: "ids", Array: true)]), null, false, errors);
		var schema = new ServiceSchemaDto { Name = "UsrErp", Type = ServiceSchemaCodes.ServiceTypeRest, BaseUri = "https://erp.example/" };
		schema.Methods.Items[method.UId.ToString()] = method;
		return schema;
	}

	[TestCase(ServiceSchemaCodes.ServiceTypeRest, "REST")]
	[TestCase(ServiceSchemaCodes.ServiceTypeSoap11, "SOAP 1.1")]
	[TestCase(ServiceSchemaCodes.ServiceTypeSoap12, "SOAP 1.2")]
	[Description("The view names the service type as list-web-services does, telling SOAP 1.1 from SOAP 1.2.")]
	public void From_Should_Name_The_Service_Type(int type, string expected) {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		schema.Type = type;

		// Act
		WebServiceView view = WebServiceSchemaView.From(schema, "Custom");

		// Assert
		view.Type.Should().Be(expected, because: "the two SOAP versions build different envelopes and must be told apart");
	}

	[Test]
	[Description("A SOAP view lists the namespaces default first, and each method's message element and SOAP action instead of a uri.")]
	public void From_Should_Report_Namespaces_And_Soap_Method_Members() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();

		// Act
		WebServiceView view = WebServiceSchemaView.From(schema, "Custom");

		// Assert
		view.Namespaces.Should().HaveCount(2, because: "both declared namespaces are reported");
		view.Namespaces[0].Should().Be(new WebServiceNamespaceView("tem", "http://example.org/UsrSoapR", true),
			because: "the default namespace comes first");
		WebServiceMethodView method = view.Methods.Single();
		method.MessageElement.Should().Be("tem:UsrOp", because: "a SOAP method's request uri is its message element");
		method.SoapAction.Should().Be("http://example.org/UsrSoapR/UsrOp", because: "the SOAP action is part of the call");
		method.Uri.Should().BeNull(because: "a SOAP URL is the base URI alone, so a uri would mislead");
	}

	[Test]
	[Description("soap-header appears only on SOAP-header parameters and sequence-element only on SOAP arrays; a REST view carries no SOAP member.")]
	public void From_Should_Report_Soap_Parameter_Members_Only_Where_They_Apply() {
		// Arrange
		ServiceSchemaDto soap = SoapServiceFixture.Schema();
		ServiceSchemaDto rest = Rest();
		foreach (ServiceParameterDto parameter in rest.Methods.Items.Values.Single().Request.Parameters.Items.Values) {
			parameter.IsSoapHeader = false;
			parameter.SequenceElementName = "item";
		}

		// Act
		IReadOnlyList<WebServiceParameterView> parameters = WebServiceSchemaView.From(soap, "Custom").Methods.Single().RequestParameters;
		string restView = JsonSerializer.Serialize(WebServiceSchemaView.From(rest, "Custom"));

		// Assert
		parameters.Where(p => p.SoapHeader == true).Select(p => p.Code).Should().BeEquivalentTo(["UsrUser", "UsrPassword"],
			because: "only the WS-Security elements go to the SOAP Header");
		parameters.Single(p => p.Code == "UsrTags").SequenceElement.Should().Be("UsrTag",
			because: "the item element name of a scalar array changes the envelope");
		parameters.Single(p => p.Code == "UsrA").SequenceElement.Should().BeNull(
			because: "the platform stores 'item' on every parameter, which means nothing on a scalar");
		restView.Should().NotContainAny(["soap-header", "sequence-element", "message-element", "soap-action", "namespaces"],
			because: "a REST service reads exactly as before");
		restView.Should().Contain("\"uri\":\"orders\"", because: "a REST method keeps its address");
	}

	[Test]
	[Description("A REST schema without SOAP members is written back without them; a SOAP schema keeps every SOAP member it was read with.")]
	public void ServiceSchemaDto_Should_Write_Back_Soap_Members_Only_As_Read() {
		// Arrange
		ServiceSchemaDto rest = Rest();
		ServiceSchemaDto soap = SoapServiceFixture.Schema();

		// Act
		string restWritten = JsonSerializer.Serialize(rest);
		string soapWritten = JsonSerializer.Serialize(soap);

		// Assert
		restWritten.Should().NotContainAny(["soapAction", "isSoapHeader", "sequenceElementName"],
			because: "a save must not add members the platform did not send");
		soapWritten.Should().Contain("\"soapAction\":\"http://example.org/UsrSoapR/UsrOp\"", because: "the SOAP action is kept");
		soapWritten.Should().Contain("\"isSoapHeader\":true", because: "the SOAP-header flag is kept");
		soapWritten.Should().Contain("\"sequenceElementName\":\"UsrTag\"", because: "the sequence element name is kept");
		soapWritten.Should().Contain("\"prefix\":\"wsse\"", because: "the namespaces are kept");
	}

	[Test]
	[Description("A REST setMethod keeps the SOAP members the platform sent on the method and on the parameters it keeps, and adds none to a new parameter.")]
	public void BuildMethod_Should_Keep_Platform_Soap_Members_Of_Kept_Method_And_Parameters() {
		// Arrange
		ServiceMethodDto existing = Rest().Methods.Items.Values.Single();
		existing.SoapAction = string.Empty;
		ServiceParameterDto kept = existing.Request.Parameters.Items.Values.Single();
		kept.IsSoapHeader = false;
		kept.SequenceElementName = "item";
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		ServiceMethodDto rebuilt = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrGet", "GET", "orders",
			RequestParameters: [
				new WebServiceParameterArg("UsrIds", "query", Path: "ids", Array: true),
				new WebServiceParameterArg("UsrPage", "query", Path: "page")
			]), existing, false, errors);

		// Assert
		rebuilt.SoapAction.Should().BeEmpty(because: "a rebuilt method keeps the member as the platform sent it");
		Dictionary<string, ServiceParameterDto> byCode = rebuilt.Request.Parameters.Items.Values.ToDictionary(p => p.Name);
		byCode["UsrIds"].IsSoapHeader.Should().BeFalse(because: "a kept parameter keeps the flag it was read with");
		byCode["UsrIds"].SequenceElementName.Should().Be("item", because: "a kept parameter keeps its sequence element name");
		byCode["UsrPage"].IsSoapHeader.Should().BeNull(because: "a new parameter gets no member the platform did not send");
	}

	[Test]
	[Description("A setting-bound SOAP-header element and a setting-bound body element under a nested path are collected from the envelope.")]
	public void SentSecrets_Should_Collect_Setting_Bound_Values_From_A_Soap_Envelope() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw,
			SoapServiceFixture.SentEnvelope, false, out bool unlocated);

		// Assert
		unlocated.Should().BeFalse(because: "every top-level setting-bound element is in the envelope");
		secrets.Should().Contain(SoapServiceFixture.PasswordValue,
			because: "the WS-Security password is read from the SOAP Header, unescaped");
		secrets.Should().Contain(SoapServiceFixture.InnerValue, because: "a nested body element is found by its local name");
		secrets.Should().NotContain("bob", because: "a constant user name that is not secret-like is not collected");
	}

	[Test]
	[Description("A setting-bound field of an object array item is collected when the array was sent with items, and an empty array is not a failure.")]
	public void SentSecrets_Should_Collect_Array_Item_Fields_And_Accept_An_Empty_Array() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		string withItem = SoapServiceFixture.SentEnvelope.Replace("<tem:Lines arrayType=\"xsd:array[]\"/>",
			$"<tem:Lines arrayType=\"xsd:array[]\"><tem:item><tem:Token>{SoapServiceFixture.LineTokenValue}</tem:Token></tem:item></tem:Lines>");

		// Act
		IReadOnlyList<string> filled = WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, withItem,
			false, out bool filledUnlocated);
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, SoapServiceFixture.SentEnvelope,
			false, out bool emptyUnlocated);

		// Assert
		filled.Should().Contain(SoapServiceFixture.LineTokenValue, because: "an item field bound to a setting carries its value");
		filledUnlocated.Should().BeFalse(because: "the item field was found");
		emptyUnlocated.Should().BeFalse(because: "an array sent without items sent no field value");
	}

	[TestCase("<tem:Inner>inner-key-77</tem:Inner>", "", Description = "missing element")]
	[TestCase("<soapenv:Envelope", "plain text, not markup <soapenv:Envelope", Description = "not XML")]
	[Description("A top-level setting-bound element missing from the sent XML, or a body that is neither JSON nor XML, is unlocated, so the response is withheld.")]
	public void SentSecrets_Should_Fail_Closed_On_Xml_It_Cannot_Read(string find, string replace) {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		string body = SoapServiceFixture.SentEnvelope.Replace(find, replace);

		// Act
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body, false, out bool unlocated);

		// Assert
		unlocated.Should().BeTrue(because: "a value that went out but cannot be found cannot be masked");
	}

	[Test]
	[Description("An XML body with a DTD is not parsed, so an entity can neither expand nor reach a file, and the value counts as unlocated.")]
	public void SentSecrets_Should_Not_Parse_A_Body_With_A_Dtd() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		string body = "<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]>" + SoapServiceFixture.SentEnvelope;

		// Act
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body, false, out bool unlocated);

		// Assert
		unlocated.Should().BeTrue(because: "DTD processing is prohibited, so the body is treated as unreadable");
	}

	[Test]
	[Description("A field missing from an array item that the envelope does carry means the lookup missed it, so the value is unlocated.")]
	public void SentSecrets_Should_Fail_Closed_On_A_Field_Missing_From_A_Sent_Item() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		string body = SoapServiceFixture.SentEnvelope.Replace("<tem:Lines arrayType=\"xsd:array[]\"/>",
			"<tem:Lines arrayType=\"xsd:array[]\"><tem:item><tem:Other>x</tem:Other></tem:item></tem:Lines>");

		// Act
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body, false, out bool unlocated);

		// Assert
		unlocated.Should().BeTrue(because: "the platform renders every field of an item it renders");
	}

	[Test]
	[Description("An array item field whose array element is missing too fails closed unless the array is known to have had no value.")]
	public void SentSecrets_Should_Fail_Closed_When_The_Item_Parent_Is_Missing_Unless_It_Had_No_Value() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		string body = SoapServiceFixture.SentEnvelope.Replace("<tem:Lines arrayType=\"xsd:array[]\"/>", string.Empty);

		// Act
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body, false, null, out bool unknown);
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body, false, ["UsrLines"], out bool given);
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body, false, ["UsrA"], out bool notGiven);

		// Assert
		unknown.Should().BeTrue(because: "without knowing what the call sent, a missing parent may be a lookup miss");
		given.Should().BeTrue(because: "an array the call gave a value is rendered, so a missing element is a lookup miss");
		notGiven.Should().BeFalse(because: "an array with no value and no default is skipped by the platform, so nothing was sent");
	}

	[Test]
	[Description("An XML response that carries a DOCTYPE is still masked by its decoded values, so a character-reference form is found.")]
	public void ApplyToBody_Should_Mask_An_Xml_Response_With_A_Doctype() {
		// Arrange
		const string response = "<!DOCTYPE r><r>&#112;w-s3cret-77</r>";

		// Act
		string masked = WebServiceResponseMasker.ApplyToBody(response, ["pw-s3cret-77"], out bool changed);

		// Assert
		changed.Should().BeTrue(because: "the decoded text carries the secret");
		masked.Should().NotContain("s3cret-77", because: "the character-reference form must not survive");
	}

	[Test]
	[Description("A path segment with a dot after its prefix is matched the way the platform renders it: by the name up to the dot.")]
	public void SentSecrets_Should_Match_The_Element_Name_The_Platform_Renders() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		method.Request.Parameters.Items.Values.Single(p => p.Name == "UsrInner").Path = "tem:Outer/tem:Inner.Key";
		string body = SoapServiceFixture.SentEnvelope.Replace("<tem:A>a1</tem:A>", "<tem:A>a1</tem:A><tem:Key>other</tem:Key>");

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body,
			false, out bool unlocated);

		// Assert
		unlocated.Should().BeFalse(because: "the platform renders tem:Inner.Key as tem:Inner, which is in the envelope");
		secrets.Should().Contain(SoapServiceFixture.InnerValue, because: "the value is read from the element the platform wrote");
		secrets.Should().NotContain("other", because: "an unrelated element named like the dotted tail is not taken instead");
	}

	[Test]
	[Description("A multi-line setting value is masked when the API echoes it with CRLF line ends or line by line.")]
	public void Apply_Should_Mask_A_Multi_Line_Secret_In_Every_Line_Form() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		string body = SoapServiceFixture.SentEnvelope.Replace(SoapServiceFixture.InnerValue, "line-one-1111\r\nline-two-2222");
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw, body,
			false, out _);

		// Act
		string crlf = WebServiceResponseMasker.Apply("echo: line-one-1111\r\nline-two-2222", secrets, out _);
		string single = WebServiceResponseMasker.Apply("only line-two-2222 came back", secrets, out _);

		// Assert
		crlf.Should().Be("echo: [masked]", because: "the value as sent, with CRLF, is one form of the secret");
		single.Should().Be("only [masked] came back", because: "each line of a multi-line secret is a secret of its own");
	}

	[Test]
	[Description("An XML response is masked by its decoded values, so a secret written with numeric character references is found.")]
	public void ApplyToBody_Should_Mask_An_Xml_Body_By_Decoded_Values() {
		// Arrange
		const string fault = "<?xml version=\"1.0\"?><Fault><Reason>it&#39;s pw-s3cret&#38;&#60;9&#62;</Reason></Fault>";

		// Act
		string masked = WebServiceResponseMasker.ApplyToBody(fault, [SoapServiceFixture.PasswordValue], out bool changed);

		// Assert
		changed.Should().BeTrue(because: "the decoded text carries the password");
		masked.Should().NotContain("s3cret", because: "no form of the password may survive");
		masked.Should().Contain("[masked]", because: "the value is replaced, not dropped");
	}

	[Test]
	[Description("Parsed response values are masked by code where a parameter may carry a credential, and by value where the platform converted a secret's type.")]
	public void MaskResponseValues_Should_Mask_Credential_Parameters_And_Converted_Secrets() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrGet", "GET", "x",
			ResponseParameters: [
				new WebServiceParameterArg("UsrFromSetting", Path: "$.a", DefaultSysSetting: "UsrApiSecret"),
				new WebServiceParameterArg("UsrSession", "cookie", Path: "sid"),
				new WebServiceParameterArg("UsrAuthToken", "header", Path: "X-Auth-Token"),
				new WebServiceParameterArg("UsrRequestId", "header", Path: "x-request-id"),
				new WebServiceParameterArg("UsrId", Path: "$.id", Type: "Guid"),
				new WebServiceParameterArg("UsrAccount", Path: "$.acc", Type: "Integer")
			]), null, false, errors);
		const string values = "{\"UsrFromSetting\":\"live-secret\",\"UsrSession\":\"s-1\",\"UsrAuthToken\":\"t-1\","
			+ "\"UsrRequestId\":\"r-1\",\"UsrId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"UsrAccount\":123456}";

		// Act
		JsonNode masked = WebServiceResponseMasker.MaskResponseValues(values, method,
			["6F9619FF8B86D011B42D00C04FC964FF", "00123456"], out bool changed);

		// Assert
		changed.Should().BeTrue(because: "credential-carrying values were present");
		masked!["UsrFromSetting"]!.GetValue<string>().Should().Be("[masked]",
			because: "the platform fills a setting default when the API sends nothing, which is the setting's value");
		masked["UsrSession"]!.GetValue<string>().Should().Be("[masked]", because: "a cookie can be a session");
		masked["UsrAuthToken"]!.GetValue<string>().Should().Be("[masked]", because: "the header is named like a credential");
		masked["UsrRequestId"]!.GetValue<string>().Should().Be("r-1", because: "an ordinary header value stays readable");
		masked["UsrId"]!.GetValue<string>().Should().Be("[masked]", because: "the Guid is the sent key in another format");
		masked["UsrAccount"]!.GetValue<string>().Should().Be("[masked]", because: "the number is the sent key without leading zeros");
	}

	[Test]
	[Description("Every JSONPath body parameter of an XML response gets one notice at any depth, and a header parameter none.")]
	public void XmlResponsePathNotices_Should_Name_Every_JsonPath_Body_Parameter_Once() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		ServiceParameterDto lines = method.Request.Parameters.Items.Values.Single(p => p.Name == "UsrLines");
		lines.Path = "$.Lines";
		lines.ItemProperties.Items.Values.Single().Path = "$.Token";
		method.Response.Parameters.Items.Clear();
		method.Response.Parameters.Items[lines.UId.ToString()] = lines;
		var header = new ServiceParameterDto {
			UId = System.Guid.NewGuid(), Name = "UsrHeader", Path = "$weird", Type = ServiceSchemaCodes.Placements["header"]
		};
		method.Response.Parameters.Items[header.UId.ToString()] = header;

		// Act
		IReadOnlyList<string> notices = WebServiceSchemaView.XmlResponsePathNotices([method], soap: true);

		// Assert
		notices.Should().HaveCount(2, because: "the array and its item field each read nothing, and each is named once");
		notices.Should().Contain(n => n.Contains("'UsrLines'"), because: "the array parameter is named");
		notices.Should().Contain(n => n.Contains("'UsrLineToken'"), because: "the item field is named");
		notices.Should().NotContain(n => n.Contains("UsrHeader"), because: "a header path is a name, not a JSONPath");
	}

	[Test]
	[Description("A constant default on a SOAP-header element is hidden by describe like a header constant.")]
	public void From_Should_Hide_A_Soap_Header_Constant() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		ServiceParameterDto user = schema.Methods.Items.Values.Single().Request.Parameters.Items.Values.Single(p => p.Name == "UsrUser");
		user.DefValue = new ServiceParameterValueDto { Source = ServiceSchemaCodes.ValueSourceConst, Value = "licence-code-1" };

		// Act
		WebServiceParameterView view = WebServiceSchemaView.From(schema, "Custom").Methods.Single().RequestParameters
			.Single(p => p.Code == "UsrUser");

		// Assert
		view.DefaultValue.Should().BeNull(because: "WS-Security and licence values travel in the SOAP Header");
		view.DefaultMasked.Should().BeTrue(because: "the reader learns a constant exists");
	}

	[Test]
	[Description("Apply masks a secret in its XML-escaped forms, as a SOAP fault that echoes the request carries it.")]
	public void Apply_Should_Mask_The_Xml_Escaped_Form() {
		// Arrange
		string fault = "<faultstring>bad password pw-s3cret&amp;&lt;9&gt; and pw-s3cret&amp;&lt;9></faultstring>";

		// Act
		string masked = WebServiceResponseMasker.Apply(fault, [SoapServiceFixture.PasswordValue], out bool changed);

		// Assert
		changed.Should().BeTrue(because: "the fault carried the password");
		masked.Should().Be("<faultstring>bad password [masked] and [masked]</faultstring>",
			because: "both the fully and the minimally escaped forms are replaced");
	}
}
