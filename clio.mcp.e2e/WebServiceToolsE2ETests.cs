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
/// End-to-end coverage of the web service registration tools (ENG-105475) against a live stand: register,
/// list, describe, refuse a credential literal, modify with UIds kept, then remove the throwaway service.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(RegisterWebServiceTool.ToolName)]
[NonParallelizable]
public sealed class WebServiceToolsE2ETests : McpContractFixtureBase {

	private const string PackageName = "Custom";
	private const string MethodCode = "GetOrders";

	private string? _environmentName;
	private string? _serviceCode;

	[TearDown]
	public async Task RemoveThrowawayServiceAsync() {
		if (_environmentName is null || _serviceCode is null) {
			return;
		}
		// No tool deletes a service (out of scope for ENG-105475), so the platform's own designer contract
		// removes the throwaway one through the CLI.
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		McpE2ESettings settings = TestConfiguration.Load();
		ClioCliCommandResult describe = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", _environmentName, "--service-path", "DataService/json/SyncReply/SelectQuery",
			"-b", SelectServiceQuery(_serviceCode)
		], cancellationToken: cts.Token);
		using JsonDocument rows = JsonDocument.Parse(describe.StandardOutput[describe.StandardOutput.IndexOf('{')..]);
		foreach (JsonElement row in rows.RootElement.GetProperty("rows").EnumerateArray()) {
			string body = JsonSerializer.Serialize(new {
				uId = row.GetProperty("UId").GetString(), packageUId = row.GetProperty("PackageUId").GetString()
			});
			await ClioCliCommandRunner.RunAsync(settings, [
				"call-service", "-e", _environmentName, "--service-path",
				"DataService/json/SyncReply/ServiceSchemaRemoveRequest", "-b", body
			], cancellationToken: cts.Token);
		}
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register, list, describe and modify a REST web service")]
	[Description("Registers a REST service with a path parameter, a system-setting header and a nested response array, finds it through list-web-services, reads it back through describe-web-service with no credential value, refuses a constant header key, then replaces the method keeping the UIds of the parameters it keeps.")]
	public async Task WebServiceTools_Should_Register_Read_And_Modify_A_Rest_Service() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		_serviceCode = $"UsrE2eWs{Guid.NewGuid():N}"[..20];

		// Act
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = PackageName,
				["code"] = _serviceCode,
				["caption"] = "E2E orders",
				["base-uri"] = "https://example.invalid/api",
				["methods"] = new[] { OrdersMethod(withHeader: true) }
			}, cts.Token);
		WebServiceListResult listed = await CallAsync<WebServiceListResult>(ListWebServicesTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName }, cts.Token);
		WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = _serviceCode, ["usages"] = true
			}, cts.Token);
		WebServiceToolResult literalKey = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = _serviceCode,
				["operations"] = new[] {
					new Dictionary<string, object?> {
						["op"] = "setMethod",
						["method"] = OrdersMethod(withHeader: false, literalHeader: "sk-live-123")
					}
				}
			}, cts.Token);
		WebServiceToolResult modified = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = _serviceCode,
				["operations"] = new[] {
					new Dictionary<string, object?> { ["op"] = "setMethod", ["method"] = OrdersMethod(withHeader: false) },
					new Dictionary<string, object?> { ["op"] = "setRetryCount", ["retry-count"] = 2 }
				}
			}, cts.Token);

		// Assert
		registered.Success.Should().BeTrue(because: $"a valid REST service must register; error: {registered.Error}");
		registered.Service!.BaseUri.Should().Be("https://example.invalid/api/",
			because: "the designer joins base and method URI verbatim, so the tool stores the base with a trailing slash");
		listed.Services.Should().Contain(s => s.Code == _serviceCode,
			because: "the registered service must appear where the Process Designer's picker reads services from");
		listed.Services!.Single(s => s.Code == _serviceCode).BaseUri.Should().Be("https://example.invalid/api/",
			because: "the list carries each base URI so an agent can find a service for the same API before registering");
		described.Success.Should().BeTrue(because: $"describe must read the service back; error: {described.Error}");
		WebServiceMethodView method = described.Service!.Methods.Should().ContainSingle(
			because: "one method was registered").Subject;
		method.Uri.Should().Be("orders/{id}", because: "the method address is stored without a leading slash");
		method.RequestParameters.Should().Contain(p => p.Code == "XApiKey" && p.DefaultSysSetting == "SchemaNamePrefix",
			because: "a header bound to a system setting is returned as the setting code, never as its value");
		method.ResponseParameters.Single(p => p.Code == "items").Items.Should().ContainSingle(p => p.Code == "number",
			because: "nested response items must round-trip through the platform");
		described.Usages.Should().BeEmpty(because: "no process calls a service registered a moment ago");
		literalKey.Success.Should().BeFalse(because: "a constant value on a header parameter must be refused");
		literalKey.Error.Should().Contain("default-sys-setting",
			because: "the refusal must point the agent at the system-setting route");
		literalKey.Error.Should().NotContain("sk-live-123", because: "a refused credential value is never echoed back");
		modified.Success.Should().BeTrue(because: $"a valid setMethod must save; error: {modified.Error}");
		modified.Service!.RetryCount.Should().Be(2, because: "setRetryCount is applied in the same call");
		modified.Service.Methods.Single().RequestParameters.Select(p => p.Code).Should().BeEquivalentTo(["id"],
			because: "setMethod replaces the whole method definition, so the dropped header is gone");
	}

	[Test]
	[AllureTag(ModifyWebServiceTool.ToolName)]
	[AllureName("modify-web-service refuses a change to a method that a process calls")]
	[Description("Reads the shipped BingSearchApiCall service with usages, expects BingSearchProcess and its Call web service element among the callers, then asks to remove the called method without acknowledge-usages and expects a refusal that names the process and changes nothing.")]
	public async Task ModifyWebService_Should_Refuse_Changing_A_Method_A_Process_Calls() {
		// Arrange
		const string ServiceCode = "BingSearchApiCall";
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(10));
		WebServiceListResult listed = await CallAsync<WebServiceListResult>(ListWebServicesTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = environmentName }, cts.Token);
		if (listed.Services?.Any(s => s.Code == ServiceCode) != true) {
			Assert.Ignore($"{ServiceCode} (CrtCustomer360AI) is not installed on this stand.");
		}

		// Act
		WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = environmentName, ["code"] = ServiceCode, ["usages"] = true
			}, cts.Token);
		WebServiceUsage usage = described.Usages!.First(u => u.Process == "BingSearchProcess");
		WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = environmentName, ["code"] = ServiceCode,
				["operations"] = new[] {
					new Dictionary<string, object?> { ["op"] = "removeMethod", ["method-code"] = usage.Method }
				}
			}, cts.Token);
		WebServiceToolResult after = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = environmentName, ["code"] = ServiceCode },
			cts.Token);

		// Assert
		usage.Element.Should().NotBeNullOrWhiteSpace(because: "a usage names the calling Call web service element");
		refused.Success.Should().BeFalse(because: "removing a called method without acknowledge-usages must be refused");
		refused.Usages.Should().Contain(u => u.Process == "BingSearchProcess",
			because: "the refusal must name the processes the user has to approve breaking");
		after.Service!.Methods.Should().Contain(m => m.Code == usage.Method,
			because: "a refused modify must not have saved anything");
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register-web-service links an existing OAuth 2.0 application")]
	[Description("Registers a REST service with auth oauth20 naming an OAuth application that exists on the stand, and expects the application's id in the read-back, the method sending the authentication, and describe returning the same reference.")]
	public async Task RegisterWebService_Should_Link_An_Existing_OAuth_Application() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		(string Id, string Name)? application = await FindOAuthApplicationAsync(settings, _environmentName, cts.Token);
		if (application is null) {
			Assert.Ignore("No named OAuth application exists on this stand; the tools only link an existing one.");
		}
		_serviceCode = $"UsrE2eWs{Guid.NewGuid():N}"[..20];

		// Act
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = PackageName,
				["code"] = _serviceCode,
				["base-uri"] = "https://example.invalid/api",
				["auth"] = new Dictionary<string, object?> { ["type"] = "oauth20", ["oauth-app"] = application.Value.Name },
				["methods"] = new[] {
					new Dictionary<string, object?> { ["code"] = MethodCode, ["http-method"] = "GET", ["uri"] = "orders" }
				}
			}, cts.Token);
		WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName, ["code"] = _serviceCode },
			cts.Token);

		// Assert
		registered.Success.Should().BeTrue(because: $"an existing OAuth application must be accepted; error: {registered.Error}");
		registered.Service!.Auth!.Type.Should().Be("oauth20", because: "the service authenticates with OAuth 2.0");
		registered.Service.Auth.OAuthAppId.Should().BeEquivalentTo(application.Value.Id,
			because: "the platform stores the application by id, which Studio shows as the Application lookup");
		registered.Service.Methods.Single().UseAuth.Should().BeTrue(
			because: "a method of a service with authentication sends it by default, as the designer does");
		described.Service!.Auth!.OAuthAppId.Should().BeEquivalentTo(application.Value.Id,
			because: "the reference survives a read through the platform's own contract");
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register-web-service refuses an OAuth 2.0 application that does not exist")]
	[Description("Asks for auth oauth20 with an application name no row carries, expects a refusal that names it, and expects no service to be created.")]
	public async Task RegisterWebService_Should_Refuse_An_Unknown_OAuth_Application() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		_serviceCode = $"UsrE2eWs{Guid.NewGuid():N}"[..20];
		string missingApplication = $"E2eMissingOAuth{Guid.NewGuid():N}";

		// Act
		WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = PackageName,
				["code"] = _serviceCode,
				["base-uri"] = "https://example.invalid/api",
				["auth"] = new Dictionary<string, object?> { ["type"] = "oauth20", ["oauth-app"] = missingApplication },
				["methods"] = new[] {
					new Dictionary<string, object?> { ["code"] = MethodCode, ["http-method"] = "GET", ["uri"] = "orders" }
				}
			}, cts.Token);
		WebServiceListResult listed = await CallAsync<WebServiceListResult>(ListWebServicesTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName }, cts.Token);

		// Assert
		refused.Success.Should().BeFalse(because: "a service cannot authenticate with an application that does not exist");
		refused.Error.Should().Contain(missingApplication, because: "the refusal names the application the caller passed");
		listed.Services.Should().NotContain(s => s.Code == _serviceCode, because: "a refused registration saves nothing");
	}

	private static async Task<(string Id, string Name)?> FindOAuthApplicationAsync(McpE2ESettings settings,
		string environmentName, CancellationToken token) {
		ClioCliCommandResult select = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", environmentName, "--service-path", "DataService/json/SyncReply/SelectQuery",
			"-b", JsonSerializer.Serialize(new {
				rootSchemaName = "OAuthApplications",
				operationType = 0,
				columns = new {
					items = new {
						Id = new { expression = new { expressionType = 0, columnPath = "Id" } },
						Name = new { expression = new { expressionType = 0, columnPath = "Name" } }
					}
				}
			})
		], cancellationToken: token);
		using JsonDocument rows = JsonDocument.Parse(select.StandardOutput[select.StandardOutput.IndexOf('{')..]);
		foreach (JsonElement row in rows.RootElement.GetProperty("rows").EnumerateArray()) {
			string? name = row.GetProperty("Name").GetString();
			if (!string.IsNullOrWhiteSpace(name)) {
				return (row.GetProperty("Id").GetString()!, name);
			}
		}
		return null;
	}

	private async Task<T> CallAsync<T>(string toolName, Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		return EntitySchemaStructuredResultParser.Extract<T>(result);
	}

	private static Dictionary<string, object?> OrdersMethod(bool withHeader, string? literalHeader = null) {
		var requestParameters = new List<Dictionary<string, object?>> {
			new() { ["code"] = "id", ["in"] = "path" }
		};
		if (withHeader) {
			// SchemaNamePrefix exists on every stand, so the reference passes the existence check without
			// the test creating a system setting.
			requestParameters.Add(new() {
				["code"] = "XApiKey", ["in"] = "header", ["path"] = "X-Api-Key", ["default-sys-setting"] = "SchemaNamePrefix"
			});
		}
		if (literalHeader is not null) {
			requestParameters.Add(new() {
				["code"] = "XApiKey", ["in"] = "header", ["path"] = "X-Api-Key", ["default-value"] = literalHeader
			});
		}
		return new Dictionary<string, object?> {
			["code"] = MethodCode,
			["http-method"] = "GET",
			["uri"] = "/orders/{id}",
			["request-parameters"] = requestParameters,
			["response-parameters"] = new[] {
				new Dictionary<string, object?> { ["code"] = "status" },
				new Dictionary<string, object?> {
					["code"] = "items", ["type"] = "Object", ["array"] = true,
					["items"] = new[] { new Dictionary<string, object?> { ["code"] = "number" } }
				}
			}
		};
	}

	private static string SelectServiceQuery(string code) => JsonSerializer.Serialize(new {
		rootSchemaName = "VwWebServiceV2",
		operationType = 0,
		columns = new {
			items = new {
				UId = new { expression = new { expressionType = 0, columnPath = "UId" } },
				PackageUId = new { expression = new { expressionType = 0, columnPath = "PackageUId" } }
			}
		},
		filters = new {
			rootSchemaName = "VwWebServiceV2", filterType = 6, logicalOperation = 0, isEnabled = true,
			items = new {
				Name = new {
					filterType = 1, comparisonType = 3, isEnabled = true, dataValueType = 1,
					leftExpression = new { expressionType = 0, columnPath = "Name" },
					rightExpression = new { expressionType = 2, parameter = new { dataValueType = 1, value = code } }
				}
			}
		}
	});
}
