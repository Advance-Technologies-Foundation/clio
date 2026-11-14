using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage of test-web-service-method with values for arrays and arrays of objects against a live stand:
/// a REST call whose echo shows the body the platform built from them, the refusal of misshapen values before anything
/// is sent, and a SOAP call whose setting-bound item fields are located in the envelope at two depths. Every service is
/// registered by the test with codes built from the stand's prefix and removed in the teardown.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(TestWebServiceMethodTool.ToolName)]
[NonParallelizable]
public sealed class WebServiceNestedTestValuesE2ETests : McpContractFixtureBase {

	private const string PackageName = "Custom";
	private const string EchoBaseUri = "https://httpbin.org/anything";
	private const string TemNamespace = "http://example.org/clio-e2e-nested";

	private readonly List<string> _serviceCodes = [];
	private string? _environmentName;
	private string _prefix = string.Empty;

	private string Prefixed(string name) => _prefix + name;

	private string UniqueCode(string stem) => Prefixed(stem) + Guid.NewGuid().ToString("N")[..12];

	[TearDown]
	public async Task RemoveThrowawayServicesAsync() {
		try {
			if (_environmentName is not null && _serviceCodes.Count > 0) {
				using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
				await WebServiceStand.RemoveServicesAsync(TestConfiguration.Load(), _environmentName, _serviceCodes, cts.Token);
			}
		} finally {
			_serviceCodes.Clear();
			_prefix = string.Empty;
		}
	}

	[Test]
	[AllureName("test-call a REST method with arrays and arrays of objects")]
	[Description("Registers a REST service on an echo API with an array of texts and an array of objects whose items hold an array of texts and an inner array of objects, test-calls it with values at every depth and expects the echo to show the JSON body the platform built from them. Ignored when the echo API answers with an error or the stand cannot reach it, since the sent body cannot be read back then; a call the platform fails to build fails the test.")]
	public async Task TestWebServiceMethod_Should_Send_Arrays_And_Arrays_Of_Objects() {
		// Arrange
		await ArrangeAsync();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(cts.Token);
		string code = UniqueCode("E2eNestRest");
		_serviceCodes.Add(code);
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["package-name"] = PackageName, ["code"] = code,
				["base-uri"] = EchoBaseUri,
				["methods"] = new[] {
					new Dictionary<string, object?> {
						["code"] = Prefixed("Post"), ["http-method"] = "POST", ["uri"] = "nested", ["content-type"] = "json",
						["request-parameters"] = new object[] {
							Field("Tags", "tags", array: true),
							Field("Lines", "lines", "Object", true,
								Field("Sku", "sku"), Field("Qty", "qty", "Integer"), Field("Notes", "notes", array: true),
								Field("Subs", "subs", "Object", true, Field("Note", "note")))
						}
					}
				}
			}, cts.Token);
		registered.Success.Should().BeTrue(because: $"the arrange step registers the service; error: {registered.Error}");

		// Act
		WebServiceTestResult result = await TestAsync(code, Prefixed("Post"), new Dictionary<string, object?> {
			[Prefixed("Tags")] = new object[] { "red", "green" },
			[Prefixed("Lines")] = new object[] {
				new Dictionary<string, object?> {
					[Prefixed("Sku")] = "S1", [Prefixed("Qty")] = 2, [Prefixed("Notes")] = new object[] { "n1", "n2" },
					[Prefixed("Subs")] = new object[] { new Dictionary<string, object?> { [Prefixed("Note")] = "deep" } }
				},
				new Dictionary<string, object?> { [Prefixed("Sku")] = "S2" }
			}
		}, cts.Token);
		// An echo API that answers with an error, or a stand that cannot reach it, cannot show the sent body. A call the
		// platform failed to build reports no status and its own error, and must fail the test instead.
		if (result.StatusCode > 200 || (result.StatusCode is null or 0 && IsConnectionFailure(result.Error))) {
			Assert.Ignore($"'{_environmentName}' got no answer from {EchoBaseUri} (status {result.StatusCode}, "
				+ $"error {result.Error}), so the sent body cannot be read back.");
		}

		// Assert
		result.Success.Should().BeTrue(because: $"the echo API answers the call; error: {result.Error}");
		using JsonDocument echo = JsonDocument.Parse(result.ResponseBody!);
		JsonElement sent = echo.RootElement.GetProperty("json");
		sent.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal(["red", "green"],
			because: "an array of texts goes out as a JSON array, not one item per character");
		JsonElement[] lines = sent.GetProperty("lines").EnumerateArray().ToArray();
		lines.Should().HaveCount(2, because: "each object of the array becomes one item");
		lines[0].GetProperty("sku").GetString().Should().Be("S1", because: "an item field goes out under its path");
		lines[0].GetProperty("qty").GetInt32().Should().Be(2, because: "the platform converts the text to the field's type");
		lines[0].GetProperty("notes").EnumerateArray().Select(n => n.GetString()).Should().Equal(["n1", "n2"],
			because: "an array of texts inside an item goes out as a JSON array");
		lines[0].GetProperty("subs")[0].GetProperty("note").GetString().Should().Be("deep",
			because: "an array of objects inside an item goes out with its own items");
		lines[1].GetProperty("sku").GetString().Should().Be("S2", because: "the second item is sent too");
	}

	[Test]
	[AllureName("refuse misshapen test-call values")]
	[Description("Registers a REST service with a query array, a body array and an array of objects, test-calls it with a JSON array for the query value, a text for the body array and an unknown item field, and expects one refusal that names each by its location, before anything is sent; needs no internet.")]
	public async Task TestWebServiceMethod_Should_Refuse_Misshapen_Values_Before_Sending() {
		// Arrange
		await ArrangeAsync();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(cts.Token);
		string code = UniqueCode("E2eNestBad");
		_serviceCodes.Add(code);
		Dictionary<string, object?> ids = Field("Ids", "ids", array: true);
		ids["in"] = "query";
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["package-name"] = PackageName, ["code"] = code,
				["base-uri"] = "https://example.invalid/api",
				["methods"] = new[] {
					new Dictionary<string, object?> {
						["code"] = Prefixed("Post"), ["http-method"] = "POST", ["uri"] = "nested", ["content-type"] = "json",
						["request-parameters"] = new object[] {
							ids, Field("Tags", "tags", array: true), Field("Lines", "lines", "Object", true, Field("Sku", "sku"))
						}
					}
				}
			}, cts.Token);
		registered.Success.Should().BeTrue(because: $"the arrange step registers the service; error: {registered.Error}");

		// Act
		WebServiceTestResult refused = await TestAsync(code, Prefixed("Post"), new Dictionary<string, object?> {
			[Prefixed("Ids")] = new object[] { "1", "2" },
			[Prefixed("Tags")] = "red",
			[Prefixed("Lines")] = new object[] { new Dictionary<string, object?> { [Prefixed("Sku")] = "S1", [Prefixed("Price")] = 5 } }
		}, cts.Token);

		// Assert
		refused.Success.Should().BeFalse(because: "misshapen values are refused before the call");
		refused.StatusCode.Should().BeNull(because: "nothing was sent, so there is no HTTP status");
		refused.Error.Should().Contain($"parameters.{Prefixed("Ids")}: ",
				because: "the platform would send a JSON array in a query value as System.Object[]")
			.And.Contain($"parameters.{Prefixed("Tags")}: ", because: "a text for a body array would go out one item per character")
			.And.Contain($"parameters.{Prefixed("Lines")}[0]: '{Prefixed("Price")}' is not a field of",
				because: "an unknown item field is named with its location, since the platform would drop it silently");
	}

	[Test]
	[AllureName("test-call a SOAP method with arrays of objects")]
	[Description("Registers a SOAP 1.1 service on the stand's own host whose array of objects has an item field and an inner array of objects with a field, both bound to a system setting; expects a test call without the user's confirmation to be refused with nothing sent, then test-calls it confirmed once leaving the inner array out and once filling it, and expects both responses to come back instead of being withheld, because every setting value that went out is located in the envelope and an inner array left out sends nothing.")]
	public async Task TestWebServiceMethod_Should_Locate_Setting_Values_In_Nested_Soap_Items() {
		// Arrange
		await ArrangeAsync();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(cts.Token);
		string code = UniqueCode("E2eNestSoap");
		_serviceCodes.Add(code);
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["package-name"] = PackageName, ["code"] = code,
				["type"] = "soap11", ["base-uri"] = $"http://localhost/{code}",
				["namespaces"] = new object[] { new Dictionary<string, object?> { ["prefix"] = "tem", ["uri"] = TemNamespace } },
				["methods"] = new[] {
					new Dictionary<string, object?> {
						["code"] = Prefixed("Op"), ["message-element"] = "tem:Op",
						["soap-action"] = "http://example.org/clio-e2e-nested/Op",
						["request-parameters"] = new object[] {
							Field("Lines", "tem:Lines", "Object", true,
								Field("Sku", "tem:Sku"), SettingField("Key", "tem:Key", "SchemaNamePrefix"),
								Field("Subs", "tem:Subs", "Object", true,
									Field("Note", "tem:Note"), SettingField("SubKey", "tem:SubKey", "SchemaNamePrefix")))
						},
						["response-parameters"] = new object[] {
							new Dictionary<string, object?> { ["code"] = Prefixed("Result"), ["path"] = "OpResponse/OpResult" }
						}
					}
				}
			}, cts.Token);
		registered.Success.Should().BeTrue(because: $"the arrange step registers the service; error: {registered.Error}");

		// Act
		WebServiceTestResult unconfirmed = await TestAsync(code, Prefixed("Op"), new Dictionary<string, object?> {
			[Prefixed("Lines")] = new object[] { new Dictionary<string, object?> { [Prefixed("Sku")] = "S1" } }
		}, cts.Token, confirmed: false);
		WebServiceTestResult innerLeftOut = await TestAsync(code, Prefixed("Op"), new Dictionary<string, object?> {
			[Prefixed("Lines")] = new object[] { new Dictionary<string, object?> { [Prefixed("Sku")] = "S1" } }
		}, cts.Token);
		WebServiceTestResult innerFilled = await TestAsync(code, Prefixed("Op"), new Dictionary<string, object?> {
			[Prefixed("Lines")] = new object[] {
				new Dictionary<string, object?> {
					[Prefixed("Sku")] = "S1",
					[Prefixed("Subs")] = new object[] { new Dictionary<string, object?> { [Prefixed("Note")] = "n1" } }
				}
			}
		}, cts.Token);

		// Assert
		unconfirmed.Success.Should().BeFalse(because: "a request to test is not a confirmation for a SOAP call");
		unconfirmed.ConfirmationRequired.Should().BeTrue(because: "the result tells the agent to ask the user first");
		unconfirmed.StatusCode.Should().BeNull(because: "nothing was sent before the user's yes");
		(innerLeftOut.Error ?? string.Empty).Should().NotContain("withheld",
			because: "the item's setting field is located, and an inner array left out of every item sent no field");
		innerLeftOut.ResponseValues.Should().NotBeNull(because: "the platform built and sent the envelope");
		(innerFilled.Error ?? string.Empty).Should().NotContain("withheld",
			because: "the setting field of the inner array's item is located two arrays deep");
		innerFilled.ResponseValues.Should().NotBeNull(because: "the platform built and sent the envelope");
	}

	private static bool IsConnectionFailure(string? error) =>
		error is not null && new[] { "resolve", "connect", "timed out", "timeout", "ssl", "tls", "unreachable", "network" }
			.Any(word => error.Contains(word, StringComparison.OrdinalIgnoreCase));

	private async Task ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
	}

	private Dictionary<string, object?> Field(string stem, string path, string? type = null, bool array = false,
		params Dictionary<string, object?>[] items) {
		var field = new Dictionary<string, object?> { ["code"] = Prefixed(stem), ["path"] = path };
		if (type is not null) {
			field["type"] = type;
		}
		if (array) {
			field["array"] = true;
		}
		if (items.Length > 0) {
			field["items"] = items;
		}
		return field;
	}

	private Dictionary<string, object?> SettingField(string stem, string path, string setting) {
		Dictionary<string, object?> field = Field(stem, path);
		field["default-sys-setting"] = setting;
		return field;
	}

	/// <summary>
	/// Test-calls a method. The services here are POST and SOAP ones, which test-web-service-method calls only with the
	/// user's confirmation; the test stands in for the user's yes unless <paramref name="confirmed"/> is false.
	/// </summary>
	private async Task<WebServiceTestResult> TestAsync(string code, string method, Dictionary<string, object?> parameters,
		CancellationToken token, bool confirmed = true) =>
		await CallAsync<WebServiceTestResult>(TestWebServiceMethodTool.ToolName, new Dictionary<string, object?> {
			["environment-name"] = _environmentName, ["code"] = code, ["method"] = method, ["parameters"] = parameters,
			["user-confirmed"] = confirmed ? true : null
		}, token);

	/// <summary>Reads the stand's <c>SchemaNamePrefix</c>; every code is built from it, so any prefix (or none) works.</summary>
	private async Task ResolvePrefixAsync(CancellationToken token) =>
		_prefix = await WebServiceStand.ReadPrefixAsync(TestConfiguration.Load(), _environmentName!, token);

	private async Task<T> CallAsync<T>(string toolName, Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		return EntitySchemaStructuredResultParser.Extract<T>(result);
	}
}
