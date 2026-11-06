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
