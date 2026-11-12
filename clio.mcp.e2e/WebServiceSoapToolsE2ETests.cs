using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage of the SOAP read side of the web service tools against a live stand:
/// describe of SOAP 1.1 and 1.2 services, a SOAP test call whose envelope carries a setting-bound SOAP-header
/// element, and the REST xml test-call message, plus registering and changing a SOAP service through the tools. The read
/// fixtures are saved through the platform's own ServiceDataContract, the call Studio makes, so they have Studio's
/// shape and not clio's; all of them are removed in the teardown.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(DescribeWebServiceTool.ToolName)]
[NonParallelizable]
public sealed class WebServiceSoapToolsE2ETests : McpContractFixtureBase {

	private const string PackageName = "Custom";
	private const string TemNamespace = "http://example.org/clio-e2e-soap";
	private const string WsseNamespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";

	private readonly List<string> _serviceCodes = [];
	private string? _environmentName;
	private string _prefix = string.Empty;

	private string Prefixed(string name) => _prefix + name;

	private string UniqueCode(string stem) => Prefixed(stem) + Guid.NewGuid().ToString("N")[..12];

	[TearDown]
	public async Task RemoveThrowawayServicesAsync() {
		try {
			if (_environmentName is null) {
				return;
			}
			using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
			McpE2ESettings settings = TestConfiguration.Load();
			foreach (string code in _serviceCodes) {
				ClioCliCommandResult rows = await ClioCliCommandRunner.RunAsync(settings, [
					"call-service", "-e", _environmentName, "--service-path", "DataService/json/SyncReply/SelectQuery",
					"-b", SelectQuery("VwWebServiceV2", "Name", code, "UId", "PackageUId")
				], cancellationToken: cts.Token);
				using JsonDocument answer = JsonDocument.Parse(rows.StandardOutput[rows.StandardOutput.IndexOf('{')..]);
				foreach (JsonElement row in answer.RootElement.GetProperty("rows").EnumerateArray()) {
					await ClioCliCommandRunner.RunAsync(settings, [
						"call-service", "-e", _environmentName, "--service-path",
						"DataService/json/SyncReply/ServiceSchemaRemoveRequest", "-b", JsonSerializer.Serialize(new {
							uId = row.GetProperty("UId").GetString(), packageUId = row.GetProperty("PackageUId").GetString()
						})
					], cancellationToken: cts.Token);
				}
			}
		} finally {
			_serviceCodes.Clear();
			_prefix = string.Empty;
		}
	}

	[Test]
	[AllureTag(DescribeWebServiceTool.ToolName)]
	[AllureTag(TestWebServiceMethodTool.ToolName)]
	[AllureName("describe and test-call SOAP 1.1 and SOAP 1.2 web services")]
	[Description("Saves a SOAP 1.1 and a SOAP 1.2 service through the platform's designer save (namespaces, a SOAP action, a SOAP-header element bound to a system setting, a body element, a scalar array with a sequence element, a response parameter with a JSONPath), expects describe-web-service to report each version and every SOAP member with no uri and a notice for the JSONPath, then test-calls the SOAP 1.1 method against the stand's own host and expects the platform's reply instead of a withheld response, with the same notice.")]
	public async Task DescribeAndTest_Should_Report_Soap_Services_And_Mask_Instead_Of_Withholding() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		string packageUId = await ResolvePackageUIdAsync(settings, cts.Token);
		string soap11 = UniqueCode("E2eSoap11");
		string soap12 = UniqueCode("E2eSoap12");
		await SaveSoapServiceAsync(settings, soap11, ServiceSchemaCodes.ServiceTypeSoap11, packageUId, cts.Token);
		await SaveSoapServiceAsync(settings, soap12, ServiceSchemaCodes.ServiceTypeSoap12, packageUId, cts.Token);

		// Act
		WebServiceToolResult described11 = await DescribeAsync(soap11, cts.Token);
		WebServiceToolResult described12 = await DescribeAsync(soap12, cts.Token);
		WebServiceTestResult testCall = await CallAsync<WebServiceTestResult>(TestWebServiceMethodTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = soap11, ["method"] = Prefixed("Op"),
				["parameters"] = new Dictionary<string, object?> { [Prefixed("A")] = "a1" }
			}, cts.Token);

		// Assert
		described11.Success.Should().BeTrue(because: $"describe must read a SOAP service; error: {described11.Error}");
		described12.Success.Should().BeTrue(because: $"describe must read a SOAP service; error: {described12.Error}");
		described11.Service!.Type.Should().Be("SOAP 1.1", because: "the version decides the envelope and must be reported");
		described12.Service!.Type.Should().Be("SOAP 1.2", because: "SOAP 1.2 is told apart from SOAP 1.1");
		described11.Service.Namespaces.Should().NotBeNull(because: "a SOAP service reports its namespaces");
		described11.Service.Namespaces![0].Should().Be(new WebServiceNamespaceView("tem", TemNamespace, true),
			because: "the default namespace comes first");
		described11.Service.Namespaces.Should().Contain(new WebServiceNamespaceView("wsse", WsseNamespace, false),
			because: "every declared namespace round-trips through the platform");
		WebServiceMethodView method = described11.Service.Methods.Should().ContainSingle(because: "one method was saved").Subject;
		method.MessageElement.Should().Be("tem:Op", because: "a SOAP method's request uri is its message element");
		method.SoapAction.Should().Be("http://example.org/clio-e2e-soap/Op", because: "the SOAP action round-trips");
		method.Uri.Should().BeNull(because: "a SOAP method has no address of its own");
		method.RequestParameters.Single(p => p.Code == Prefixed("Password")).SoapHeader.Should().BeTrue(
			because: "the WS-Security element is sent in the SOAP Header");
		method.RequestParameters.Single(p => p.Code == Prefixed("Password")).DefaultSysSetting.Should().Be("SchemaNamePrefix",
			because: "a setting-bound element is reported by its setting code, never its value");
		method.RequestParameters.Single(p => p.Code == Prefixed("Tags")).SequenceElement.Should().Be(Prefixed("Tag"),
			because: "the item element name of a SOAP array round-trips");
		method.RequestParameters.Single(p => p.Code == Prefixed("A")).SoapHeader.Should().BeNull(
			because: "a body element is not flagged");
		described11.Notices.Should().ContainSingle(n => n.Contains(Prefixed("JsonPathResult")),
			because: "a JSONPath on an XML response reads nothing, and describe says so");
		(testCall.Error ?? string.Empty).Should().NotContain("withheld",
			because: "the setting-bound SOAP-header element is located in the envelope, so nothing forces the response to be withheld");
		testCall.Notices.Should().ContainSingle(n => n.Contains(Prefixed("JsonPathResult")),
			because: $"the test call names the response parameter that cannot read anything; error: {testCall.Error}");
		testCall.ResponseValues.Should().NotBeNull(
			because: "the platform built and sent the envelope and reported its parsed outputs, which are returned with the body");
		if (testCall.StatusCode is not null) {
			testCall.ResponseBody.Should().NotBeNull(because: "a reply that came back is returned, masked");
		}
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureTag(ModifyWebServiceTool.ToolName)]
	[AllureName("register and modify a SOAP web service")]
	[Description("Registers a SOAP 1.1 service through register-web-service (namespaces, message element, SOAP action, a SOAP-header element bound to a system setting, an array with a sequence element, a SOAP-header object array with an item), refuses one with an undeclared prefix, sends describe's method and namespaces back through modify-web-service and expects describe unchanged, switches it to SOAP 1.2, refuses dropping a namespace a method uses, and test-calls it against the stand's own host.")]
	public async Task RegisterAndModify_Should_Manage_A_Soap_Service() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		string code = UniqueCode("E2eSoapReg");
		string refusedCode = UniqueCode("E2eSoapBad");
		_serviceCodes.Add(code);
		_serviceCodes.Add(refusedCode);
		object[] namespaces = [
			new Dictionary<string, object?> { ["prefix"] = "tem", ["uri"] = TemNamespace },
			new Dictionary<string, object?> { ["prefix"] = "wsse", ["uri"] = WsseNamespace }
		];

		// Act
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			RegisterArgs(code, namespaces, "tem:Op"), cts.Token);
		WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			RegisterArgs(refusedCode, namespaces, "zz:Op"), cts.Token);
		WebServiceToolResult described = await DescribeAsync(code, cts.Token);
		WebServiceToolResult roundTrip = await ModifyAsync(code, [
			new Dictionary<string, object?> {
				["op"] = "setNamespaces", ["namespaces"] = JsonSerializer.SerializeToElement(described.Service!.Namespaces)
			},
			new Dictionary<string, object?> {
				["op"] = "setMethod", ["method"] = JsonSerializer.SerializeToElement(described.Service.Methods.Single())
			}
		], cts.Token);
		WebServiceToolResult afterRoundTrip = await DescribeAsync(code, cts.Token);
		WebServiceToolResult switched = await ModifyAsync(code, [
			new Dictionary<string, object?> { ["op"] = "setType", ["type"] = "soap12" }
		], cts.Token);
		WebServiceToolResult dropped = await ModifyAsync(code, [
			new Dictionary<string, object?> {
				["op"] = "setNamespaces",
				["namespaces"] = new object[] { new Dictionary<string, object?> { ["prefix"] = "tem", ["uri"] = TemNamespace } }
			}
		], cts.Token);
		WebServiceTestResult testCall = await CallAsync<WebServiceTestResult>(TestWebServiceMethodTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = code, ["method"] = Prefixed("Op"),
				["parameters"] = new Dictionary<string, object?> { [Prefixed("A")] = "a1" }
			}, cts.Token);

		// Assert
		registered.Success.Should().BeTrue(because: $"a valid SOAP service registers; error: {registered.Error}");
		registered.Service!.Type.Should().Be("SOAP 1.1", because: "soap11 registers a SOAP 1.1 service");
		registered.Service.BaseUri.Should().Be($"http://localhost/{code}",
			because: "a SOAP base URI is stored as given, without a trailing slash");
		WebServiceMethodView method = registered.Service.Methods.Single();
		method.MessageElement.Should().Be("tem:Op", because: "the message element round-trips through the platform");
		method.SoapAction.Should().Be("http://example.org/clio-e2e-soap/Op", because: "the SOAP action round-trips");
		method.RequestParameters.Single(p => p.Code == Prefixed("Password")).SoapHeader.Should().BeTrue(
			because: "the WS-Security element is saved as a SOAP header");
		method.RequestParameters.Single(p => p.Code == Prefixed("Tags")).SequenceElement.Should().Be(Prefixed("Tag"),
			because: "the item element name of the array is saved");
		refused.Success.Should().BeFalse(because: "an undeclared prefix would fail every call before sending it");
		refused.Error.Should().Contain("prefix 'zz'", because: "the refusal names the undeclared prefix");
		roundTrip.Success.Should().BeTrue(because: $"describe output is valid modify input; error: {roundTrip.Error}");
		JsonSerializer.Serialize(afterRoundTrip.Service).Should().Be(JsonSerializer.Serialize(described.Service),
			because: "sending describe's own output back changes nothing describe shows");
		switched.Success.Should().BeTrue(because: $"SOAP 1.1 switches to 1.2; error: {switched.Error}");
		switched.Service!.Type.Should().Be("SOAP 1.2", because: "setType changed the version");
		dropped.Success.Should().BeFalse(because: "the method still uses the wsse prefix");
		dropped.Error.Should().Contain("wsse", because: "the refusal names the prefix in use");
		(testCall.Error ?? string.Empty).Should().NotContain("withheld",
			because: "the SOAP-header element bound to a setting is located in the envelope");
		testCall.ResponseValues.Should().NotBeNull(because: "the platform built and sent the envelope of the registered service");
	}

	private Dictionary<string, object?> RegisterArgs(string code, object[] namespaces, string messageElement) => new() {
		["environment-name"] = _environmentName,
		["package-name"] = PackageName,
		["code"] = code,
		["type"] = "soap11",
		["base-uri"] = $"http://localhost/{code}",
		["namespaces"] = namespaces,
		["methods"] = new[] {
			new Dictionary<string, object?> {
				["code"] = Prefixed("Op"),
				["message-element"] = messageElement,
				["soap-action"] = "http://example.org/clio-e2e-soap/Op",
				["request-parameters"] = new object[] {
					new Dictionary<string, object?> { ["code"] = Prefixed("A"), ["path"] = "tem:A" },
					new Dictionary<string, object?> {
						["code"] = Prefixed("Password"), ["path"] = "wsse:Security/wsse:UsernameToken/wsse:Password",
						["soap-header"] = true, ["default-sys-setting"] = "SchemaNamePrefix"
					},
					new Dictionary<string, object?> {
						["code"] = Prefixed("Tags"), ["path"] = "tem:Tags", ["array"] = true, ["sequence-element"] = Prefixed("Tag")
					},
					// A SOAP-header object array: describe reports the flag on its items too, and the round trip
					// below must accept it back there.
					new Dictionary<string, object?> {
						["code"] = Prefixed("Tokens"), ["path"] = "wsse:Tokens", ["type"] = "Object", ["array"] = true,
						["soap-header"] = true,
						["items"] = new object[] {
							new Dictionary<string, object?> { ["code"] = Prefixed("Nonce"), ["path"] = "wsse:Nonce" }
						}
					}
				},
				["response-parameters"] = new object[] {
					new Dictionary<string, object?> { ["code"] = Prefixed("Result"), ["path"] = "OpResponse/OpResult" }
				}
			}
		}
	};

	private async Task<WebServiceToolResult> ModifyAsync(string code, object[] operations, CancellationToken token) =>
		await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName, new Dictionary<string, object?> {
			["environment-name"] = _environmentName, ["code"] = code, ["operations"] = operations
		}, token);

	[Test]
	[AllureTag(TestWebServiceMethodTool.ToolName)]
	[AllureName("test-call a REST xml method")]
	[Description("Registers a REST service with an xml method, test-calls it and expects the platform's Rest:Xml refusal to come with the RequestBody route.")]
	public async Task TestWebServiceMethod_Should_Explain_A_Rest_Xml_Method() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		string code = UniqueCode("E2eXml");
		_serviceCodes.Add(code);
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["package-name"] = PackageName, ["code"] = code,
				["base-uri"] = "https://example.invalid/api",
				["methods"] = new[] {
					new Dictionary<string, object?> {
						["code"] = Prefixed("Post"), ["http-method"] = "POST", ["uri"] = "calc", ["content-type"] = "xml"
					}
				}
			}, cts.Token);
		registered.Success.Should().BeTrue(because: $"an xml method without body parameters registers; error: {registered.Error}");

		// Act
		WebServiceTestResult result = await CallAsync<WebServiceTestResult>(TestWebServiceMethodTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = code, ["method"] = Prefixed("Post")
			}, cts.Token);

		// Assert
		result.Success.Should().BeFalse(because: "the platform has no REST XML request builder");
		result.Error.Should().Contain("RequestBody", because: "the agent learns the only route that calls such a method");
	}

	private async Task<WebServiceToolResult> DescribeAsync(string code, CancellationToken token) =>
		await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName, ["code"] = code }, token);

	/// <summary>
	/// Saves a SOAP service through ServiceDataContract in the shape Studio sends. The code is recorded before the
	/// save, so the teardown removes the service even when the save's answer fails an assertion.
	/// </summary>
	private async Task SaveSoapServiceAsync(McpE2ESettings settings, string code, int type, string packageUId,
		CancellationToken token) {
		_serviceCodes.Add(code);
		string uid = Guid.NewGuid().ToString();
		string methodUId = Guid.NewGuid().ToString();
		string temUId = Guid.NewGuid().ToString();
		string wsseUId = Guid.NewGuid().ToString();
		var parameters = new Dictionary<string, object> {
			[Guid.NewGuid().ToString()] = Parameter(Prefixed("Password"), "wsse:Security/wsse:UsernameToken/wsse:Password",
				soapHeader: true, sysSetting: "SchemaNamePrefix"),
			[Guid.NewGuid().ToString()] = Parameter(Prefixed("A"), "tem:A"),
			[Guid.NewGuid().ToString()] = Parameter(Prefixed("Tags"), "tem:Tags", isArray: true, sequenceElement: Prefixed("Tag"))
		};
		string body = JsonSerializer.Serialize(new {
			uId = uid, realUId = uid, name = code, caption = new Dictionary<string, string> { ["en-US"] = code },
			description = new { }, extendParent = false, packageUId,
			baseUri = $"http://localhost/{code}", type, retryCount = 0,
			namespaces = new { Items = new Dictionary<string, object> {
				[temUId] = new { uId = temUId, prefix = "tem", name = TemNamespace, isDefault = true },
				[wsseUId] = new { uId = wsseUId, prefix = "wsse", name = WsseNamespace, isDefault = false }
			} },
			methods = new { Items = new Dictionary<string, object> {
				[methodUId] = new {
					uId = methodUId, name = Prefixed("Op"), requestTimeout = 5000,
					caption = new Dictionary<string, string> { ["en-US"] = "Op" }, useAuthInfo = false,
					soapAction = "http://example.org/clio-e2e-soap/Op",
					request = new { uri = "tem:Op", contentType = 2, httpMethodType = 2, parameters = new { Items = parameters } },
					response = new { contentType = 2, parameters = new { Items = new Dictionary<string, object> {
						[Guid.NewGuid().ToString()] = Parameter(Prefixed("Result"), "OpResponse/OpResult"),
						[Guid.NewGuid().ToString()] = Parameter(Prefixed("JsonPathResult"), "$.OpResult")
					} } }
				}
			} },
			authInfo = new { authType = 0, values = new { Items = new { } } }
		});
		ClioCliCommandResult saved = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", _environmentName!, "--service-path", "DataService/json/SyncReply/ServiceDataContract", "-b", body
		], cancellationToken: token);
		saved.ExitCode.Should().Be(0, because: $"the arrange step saves a SOAP service; output: {saved.StandardOutput} {saved.StandardError}");
		using JsonDocument answer = JsonDocument.Parse(saved.StandardOutput[saved.StandardOutput.IndexOf('{')..]);
		answer.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(
			because: $"the designer save must accept the SOAP service; output: {saved.StandardOutput}");
	}

	private static object Parameter(string code, string path, bool soapHeader = false, string? sysSetting = null,
		bool isArray = false, string sequenceElement = "item") {
		string id = Guid.NewGuid().ToString();
		return new {
			uId = id, type = 1, name = code, path,
			defValue = sysSetting is null ? new { source = 0, value = "" } : new { source = 2, value = sysSetting },
			dataValueTypeName = "Text", isArray, sequenceElementName = sequenceElement, isSoapHeader = soapHeader,
			isRequired = false, itemProperties = new { Items = new { } },
			caption = new Dictionary<string, string> { ["en-US"] = code }
		};
	}

	private async Task<string> ResolvePackageUIdAsync(McpE2ESettings settings, CancellationToken token) {
		ClioCliCommandResult queried = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", _environmentName!, "--service-path", "DataService/json/SyncReply/SelectQuery",
			"-b", SelectQuery("SysPackage", "Name", PackageName, "UId")
		], cancellationToken: token);
		queried.ExitCode.Should().Be(0, because: $"the fixture needs the package UId; output: {queried.StandardOutput} {queried.StandardError}");
		using JsonDocument answer = JsonDocument.Parse(queried.StandardOutput[queried.StandardOutput.IndexOf('{')..]);
		JsonElement rows = answer.RootElement.GetProperty("rows");
		rows.GetArrayLength().Should().BeGreaterThan(0, because: $"the stand must have package '{PackageName}'");
		return rows[0].GetProperty("UId").GetString()!;
	}

	/// <summary>Reads the stand's <c>SchemaNamePrefix</c>; every code is built from it, so any prefix (or none) works.</summary>
	private async Task ResolvePrefixAsync(McpE2ESettings settings, CancellationToken token) {
		ClioCliCommandResult queried = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", _environmentName!, "--service-path", "DataService/json/SyncReply/QuerySysSettings",
			"-b", JsonSerializer.Serialize(new { sysSettingsNameCollection = new[] { "SchemaNamePrefix" } })
		], cancellationToken: token);
		queried.ExitCode.Should().Be(0,
			because: $"the codes are built from the stand's prefix; output: {queried.StandardOutput} {queried.StandardError}");
		using JsonDocument answer = JsonDocument.Parse(queried.StandardOutput[queried.StandardOutput.IndexOf('{')..]);
		_prefix = answer.RootElement.GetProperty("values").TryGetProperty("SchemaNamePrefix", out JsonElement setting)
			&& setting.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.String
				? value.GetString()!.Trim()
				: string.Empty;
	}

	private async Task<T> CallAsync<T>(string toolName, Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		return EntitySchemaStructuredResultParser.Extract<T>(result);
	}

	private static string SelectQuery(string root, string filterColumn, string value, params string[] columns) =>
		JsonSerializer.Serialize(new {
			rootSchemaName = root,
			operationType = 0,
			columns = new {
				items = columns.ToDictionary(c => c, c => (object)new { expression = new { expressionType = 0, columnPath = c } })
			},
			filters = new {
				rootSchemaName = root, filterType = 6, logicalOperation = 0, isEnabled = true,
				items = new Dictionary<string, object> {
					[filterColumn] = new {
						filterType = 1, comparisonType = 3, isEnabled = true, dataValueType = 1,
						leftExpression = new { expressionType = 0, columnPath = filterColumn },
						rightExpression = new { expressionType = 2, parameter = new { dataValueType = 1, value } }
					}
				}
			}
		});
}
