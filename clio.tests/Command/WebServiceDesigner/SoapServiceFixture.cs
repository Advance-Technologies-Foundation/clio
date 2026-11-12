using System.Text.Json;
using Clio.Command.WebServiceDesigner;

namespace Clio.Tests.Command.WebServiceDesigner;

/// <summary>
/// A SOAP 1.2 service in the shape <c>ServiceSchemaRequest</c> returns (measured on stand Creatio, .NET Framework,
/// November 2026): namespaces, a SOAP action, a WS-Security SOAP-header element bound to a system setting, a body
/// element under a nested path bound to another, a scalar array with its sequence element and an object array whose
/// field is bound to a third setting. <see cref="SentEnvelope"/> is what the platform reports as sent for it.
/// </summary>
internal static class SoapServiceFixture {

	public const string PasswordValue = "pw-s3cret&<9>";
	public const string InnerValue = "inner-key-77";
	public const string LineTokenValue = "line-token-55";

	public const string PlatformRead = """
		{
		 "uId": "82e5d83c-0000-4000-8000-000000000001", "realUId": "82e5d83c-0000-4000-8000-000000000001",
		 "name": "UsrSoapProbe", "caption": {"en-US": "SOAP probe"}, "description": {}, "extendParent": false,
		 "packageUId": "6d7929ef-24da-1079-a4e8-dfb3c988735c",
		 "baseUri": "http://localhost/UsrSoapProbe", "type": 3, "retryCount": 0,
		 "namespaces": {"Items": {
		  "e34e5bd3-4afe-47a6-9d62-3a2ee09d05f7": {"uId": "e34e5bd3-4afe-47a6-9d62-3a2ee09d05f7",
		   "name": "http://example.org/UsrSoapR", "prefix": "tem", "isDefault": true},
		  "1a2b3c4d-4afe-47a6-9d62-3a2ee09d05f7": {"uId": "1a2b3c4d-4afe-47a6-9d62-3a2ee09d05f7",
		   "name": "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd", "prefix": "wsse", "isDefault": false}
		 }},
		 "methods": {"Items": {"b057b88a-4897-44f4-8b32-cb935584995b": {
		  "uId": "b057b88a-4897-44f4-8b32-cb935584995b", "name": "UsrOp", "requestTimeout": 5000,
		  "request": {"uri": "tem:UsrOp", "contentType": 2, "httpMethodType": 2, "parameters": {"Items": {
		   "11111111-0000-4000-8000-000000000001": {"uId": "11111111-0000-4000-8000-000000000001", "type": 1, "name": "UsrUser",
		    "path": "wsse:Security/wsse:UsernameToken/wsse:Username", "defValue": {"value": "bob", "source": 1},
		    "dataValueTypeName": "Text", "isArray": false, "sequenceElementName": "item", "isSoapHeader": true,
		    "isRequired": false, "itemProperties": {"Items": {}}, "caption": {"en-US": "UsrUser"}},
		   "11111111-0000-4000-8000-000000000002": {"uId": "11111111-0000-4000-8000-000000000002", "type": 1, "name": "UsrPassword",
		    "path": "wsse:Security/wsse:UsernameToken/wsse:Password", "defValue": {"value": "UsrWsPass", "source": 2},
		    "dataValueTypeName": "Text", "isArray": false, "sequenceElementName": "item", "isSoapHeader": true,
		    "isRequired": false, "itemProperties": {"Items": {}}, "caption": {"en-US": "UsrPassword"}},
		   "11111111-0000-4000-8000-000000000003": {"uId": "11111111-0000-4000-8000-000000000003", "type": 1, "name": "UsrA",
		    "path": "tem:A", "defValue": {"source": 0}, "dataValueTypeName": "Text", "isArray": false,
		    "sequenceElementName": "item", "isSoapHeader": false, "isRequired": false, "itemProperties": {"Items": {}},
		    "caption": {"en-US": "UsrA"}},
		   "11111111-0000-4000-8000-000000000004": {"uId": "11111111-0000-4000-8000-000000000004", "type": 1, "name": "UsrInner",
		    "path": "/tem:Outer/tem:Inner", "defValue": {"value": "UsrInnerKey", "source": 2}, "dataValueTypeName": "Text",
		    "isArray": false, "sequenceElementName": "item", "isSoapHeader": false, "isRequired": false,
		    "itemProperties": {"Items": {}}, "caption": {"en-US": "UsrInner"}},
		   "11111111-0000-4000-8000-000000000005": {"uId": "11111111-0000-4000-8000-000000000005", "type": 1, "name": "UsrTags",
		    "path": "tem:Tags", "defValue": {"source": 0}, "dataValueTypeName": "Text", "isArray": true,
		    "sequenceElementName": "UsrTag", "isSoapHeader": false, "isRequired": false, "itemProperties": {"Items": {}},
		    "caption": {"en-US": "UsrTags"}},
		   "11111111-0000-4000-8000-000000000006": {"uId": "11111111-0000-4000-8000-000000000006", "type": 1, "name": "UsrLines",
		    "path": "tem:Lines", "defValue": {"source": 0}, "dataValueTypeName": "Object", "isArray": true,
		    "sequenceElementName": "item", "isSoapHeader": false, "isRequired": false, "caption": {"en-US": "UsrLines"},
		    "itemProperties": {"Items": {
		     "11111111-0000-4000-8000-000000000007": {"uId": "11111111-0000-4000-8000-000000000007", "type": 1,
		      "name": "UsrLineToken", "path": "tem:Token", "defValue": {"value": "UsrLineToken", "source": 2},
		      "dataValueTypeName": "Text", "isArray": false, "sequenceElementName": "item", "isSoapHeader": false,
		      "isRequired": false, "itemProperties": {"Items": {}}, "caption": {"en-US": "UsrLineToken"}}
		    }}}
		  }}},
		  "response": {"contentType": 2, "parameters": {"Items": {
		   "22222222-0000-4000-8000-000000000001": {"uId": "22222222-0000-4000-8000-000000000001", "type": 1, "name": "UsrResult",
		    "path": "UsrOpResponse/UsrOpResult", "defValue": {"source": 0}, "dataValueTypeName": "Text", "isArray": false,
		    "sequenceElementName": "item", "isSoapHeader": false, "isRequired": false, "itemProperties": {"Items": {}},
		    "caption": {"en-US": "UsrResult"}}
		  }}},
		  "caption": {"en-US": "Op"}, "useAuthInfo": false, "soapAction": "http://example.org/UsrSoapR/UsrOp"
		 }}}
		}
		""";

	/// <summary>The envelope the platform reports as sent: the password XML-escaped, no item in the object array.</summary>
	public const string SentEnvelope = """
		<soapenv:Envelope xmlns:tem="http://example.org/UsrSoapR" xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd" xmlns:soapenv="http://www.w3.org/2003/05/soap-envelope">
			<soapenv:Header>
				<wsse:Security>
					<wsse:UsernameToken>
						<wsse:Username>bob</wsse:Username>
						<wsse:Password>pw-s3cret&amp;&lt;9&gt;</wsse:Password>
					</wsse:UsernameToken>
				</wsse:Security>
			</soapenv:Header>
			<soapenv:Body>
				<tem:UsrOp>
					<tem:A>a1</tem:A>
					<tem:Outer>
						<tem:Inner>inner-key-77</tem:Inner>
					</tem:Outer>
					<tem:Tags arrayType="xsd:array[]"><tem:UsrTag>t1</tem:UsrTag></tem:Tags>
					<tem:Lines arrayType="xsd:array[]"/>
				</tem:UsrOp>
			</soapenv:Body>
		</soapenv:Envelope>
		""";

	public const string SentRaw = "POST http://localhost/UsrSoapProbe HTTP/1.1\r\n"
		+ "Content-Type: application/soap+xml;charset=UTF-8\r\nSOAPAction: http://example.org/UsrSoapR/UsrOp\r\n"
		+ "Host: localhost";

	public static ServiceSchemaDto Schema() => JsonSerializer.Deserialize<ServiceSchemaDto>(PlatformRead);

	public static ServiceMethodDto Method() {
		foreach (ServiceMethodDto method in Schema().Methods.Items.Values) {
			return method;
		}
		return null;
	}
}
