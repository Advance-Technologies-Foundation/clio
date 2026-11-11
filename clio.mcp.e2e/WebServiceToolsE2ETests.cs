using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
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

	/// <summary>The first CrtProcessBuilder that builds a Call web service element (ENG-92710).</summary>
	private const string CallWebServiceElementPackageVersion = "1.6.6.150";

	/// <summary>The Call web service element of the usage-guard process.</summary>
	private const string CallerElement = "CallOrders";

	private string? _environmentName;
	private string? _serviceCode;
	private string? _oauthApplicationId;

	/// <summary>
	/// The stand's <c>SchemaNamePrefix</c>, read by <see cref="ResolvePrefixAsync"/>. Every service, method,
	/// parameter and process code is built from it, so the tests run on a stand with any prefix, or none.
	/// </summary>
	private string _prefix = string.Empty;

	/// <summary>A code carrying the stand's prefix.</summary>
	private string Prefixed(string name) => _prefix + name;

	private string MethodCode => Prefixed("GetOrders");

	/// <summary>A unique throwaway code: the prefix, a stem and 12 hex digits.</summary>
	private string UniqueCode(string stem) => Prefixed(stem) + Guid.NewGuid().ToString("N")[..12];

	[TearDown]
	public async Task RemoveThrowawayFixturesAsync() {
		try {
			await RemoveThrowawayServiceAsync();
		} finally {
			// The service goes first: it references the application by id.
			await RemoveThrowawayOAuthApplicationAsync();
			_serviceCode = null;
			_oauthApplicationId = null;
			_prefix = string.Empty;
		}
	}

	private async Task RemoveThrowawayOAuthApplicationAsync() {
		if (_environmentName is null || _oauthApplicationId is null) {
			return;
		}
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		McpE2ESettings settings = TestConfiguration.Load();
		ClioCliCommandResult deleted = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", _environmentName, "--service-path", "DataService/json/SyncReply/DeleteQuery",
			"-b", JsonSerializer.Serialize(new {
				rootSchemaName = "OAuthApplications",
				operationType = 3,
				filters = IdFilter("OAuthApplications", _oauthApplicationId)
			})
		], cancellationToken: cts.Token);
		if (deleted.ExitCode != 0) {
			await TestContext.Error.WriteLineAsync(
				$"Could not delete OAuth application {_oauthApplicationId}: {deleted.StandardOutput} {deleted.StandardError}");
		}
	}

	private async Task RemoveThrowawayServiceAsync() {
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
		await ResolvePrefixAsync(settings, cts.Token);
		_serviceCode = UniqueCode("E2eWs");

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
		method.RequestParameters.Should().Contain(p => p.Code == Prefixed("ApiKey") && p.DefaultSysSetting == "SchemaNamePrefix",
			because: "a header bound to a system setting is returned as the setting code, never as its value");
		method.ResponseParameters.Single(p => p.Code == Prefixed("Items")).Items.Should().ContainSingle(p => p.Code == Prefixed("Number"),
			because: "nested response items must round-trip through the platform");
		described.Usages.Should().BeEmpty(because: "no process calls a service registered a moment ago");
		literalKey.Success.Should().BeFalse(because: "a constant value on a header parameter must be refused");
		literalKey.Error.Should().Contain("default-sys-setting",
			because: "the refusal must point the agent at the system-setting route");
		literalKey.Error.Should().NotContain("sk-live-123", because: "a refused credential value is never echoed back");
		modified.Success.Should().BeTrue(because: $"a valid setMethod must save; error: {modified.Error}");
		modified.Service!.RetryCount.Should().Be(2, because: "setRetryCount is applied in the same call");
		modified.Service.Methods.Single().RequestParameters.Select(p => p.Code).Should().BeEquivalentTo([Prefixed("Id")],
			because: "setMethod replaces the whole method definition, so the dropped header is gone");
	}

	[Test]
	[AllureTag(ModifyWebServiceTool.ToolName)]
	[AllureName("modify-web-service refuses a change to a method that a process calls")]
	[Description("Registers a throwaway service, builds a process whose Call web service element calls its method, expects describe-web-service usages to name that process and element, then asks to remove the called method without acknowledge-usages and expects a refusal that names the process and changes nothing.")]
	public async Task ModifyWebService_Should_Refuse_Changing_A_Method_A_Process_Calls() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		await using ProcessDesignerArrangeContext context = await ProcessDesignerE2EArrange.StartAsync(
			"Web service usage guard", CallWebServiceElementPackageVersion, TimeSpan.FromMinutes(10));
		_environmentName = context.EnvironmentName;
		await ResolvePrefixAsync(settings, context.CancellationTokenSource.Token);
		_serviceCode = UniqueCode("E2eWs");
		string processName = UniqueCode("E2eWsCaller");
		bool processCreated = false;
		try {
			WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = _environmentName,
					["package-name"] = PackageName,
					["code"] = _serviceCode,
					["base-uri"] = "https://example.invalid/api",
					["methods"] = new[] { OrdersMethod(withHeader: false) }
				}, context.CancellationTokenSource.Token);
			registered.Success.Should().BeTrue(because: $"the arrange step needs a service; error: {registered.Error}");
			CallToolResult built = await ProcessDesignerE2EArrange.CallToolAsync(context,
				CreateBusinessProcessTool.CreateBusinessProcessToolName, new Dictionary<string, object?> {
					["environment-name"] = _environmentName,
					["package-name"] = PackageName,
					["descriptor"] = CallerDescriptor(processName, _serviceCode)
				});
			ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(
				ProcessDesignerE2ESupport.ToolText(built), CallWebServiceElementPackageVersion);
			processCreated = true;
			ProcessDesignerE2ESupport.ShouldHaveBuilt(built,
				"the arrange step needs a process whose Call web service element calls the method");

			// Act
			WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = _environmentName, ["code"] = _serviceCode, ["usages"] = true
				}, context.CancellationTokenSource.Token);
			WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = _environmentName, ["code"] = _serviceCode,
					["operations"] = new[] {
						new Dictionary<string, object?> { ["op"] = "removeMethod", ["method-code"] = MethodCode }
					}
				}, context.CancellationTokenSource.Token);
			WebServiceToolResult after = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
				new Dictionary<string, object?> { ["environment-name"] = _environmentName, ["code"] = _serviceCode },
				context.CancellationTokenSource.Token);

			// Assert
			described.Usages.Should().ContainSingle(because: "exactly one process calls the throwaway service")
				.Which.Should().Match<WebServiceUsage>(u => u.Process == processName && u.Element == CallerElement
					&& u.Method == MethodCode, because: "a usage names the calling process, its element and the method");
			refused.Success.Should().BeFalse(because: "removing a called method without acknowledge-usages must be refused");
			refused.Usages.Should().Contain(u => u.Process == processName,
				because: "the refusal must name the processes the user has to approve breaking");
			after.Service!.Methods.Should().Contain(m => m.Code == MethodCode,
				because: "a refused modify must not have saved anything");
		} finally {
			if (processCreated) {
				// The process goes first: the service teardown must not leave a caller of a removed service behind.
				await ProcessDesignerE2ESupport.DeleteProcessAsync(_environmentName, processName);
			}
		}
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register-web-service links an existing OAuth 2.0 application")]
	[Description("Creates a throwaway OAuth application, registers a REST service with auth oauth20 naming it, and expects the application's id in the read-back, the method sending the authentication, describe returning the same reference, and a setAuth rebuilt from that describe reply linking the same application.")]
	public async Task RegisterWebService_Should_Link_An_Existing_OAuth_Application() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		// The test creates its own application: the tools only link an existing one, and a stand need not have any.
		string applicationName = $"E2eWsOAuth{Guid.NewGuid():N}"[..24];
		await CreateOAuthApplicationAsync(settings, applicationName, cts.Token);
		_serviceCode = UniqueCode("E2eWs");

		// Act
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = PackageName,
				["code"] = _serviceCode,
				["base-uri"] = "https://example.invalid/api",
				["auth"] = new Dictionary<string, object?> { ["type"] = "oauth20", ["oauth-app"] = applicationName },
				["methods"] = new[] {
					new Dictionary<string, object?> { ["code"] = MethodCode, ["http-method"] = "GET", ["uri"] = "orders" }
				}
			}, cts.Token);
		WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName, ["code"] = _serviceCode },
			cts.Token);
		WebServiceToolResult restored = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = _serviceCode,
				["operations"] = new[] {
					new Dictionary<string, object?> {
						["op"] = "setAuth",
						// The auth block exactly as describe returned it: oauth-app-id and the read-only flag included.
						["auth"] = described.Service?.Auth
					}
				}
			}, cts.Token);

		// Assert
		registered.Success.Should().BeTrue(because: $"an existing OAuth application must be accepted; error: {registered.Error}");
		registered.Service!.Auth!.Type.Should().Be("oauth20", because: "the service authenticates with OAuth 2.0");
		registered.Service.Auth.OAuthAppId.Should().BeEquivalentTo(_oauthApplicationId,
			because: "the platform stores the application by id, which Studio shows as the Application lookup");
		registered.Service.Methods.Single().UseAuth.Should().BeTrue(
			because: "a method of a service with authentication sends it by default, as the designer does");
		described.Service!.Auth!.OAuthAppId.Should().BeEquivalentTo(_oauthApplicationId,
			because: "the reference survives a read through the platform's own contract");
		restored.Success.Should().BeTrue(because: $"setAuth accepts the oauth-app-id describe reports; error: {restored.Error}");
		restored.Service!.Auth!.OAuthAppId.Should().BeEquivalentTo(_oauthApplicationId,
			because: "authentication rebuilt from a describe reply links the same application");
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
		await ResolvePrefixAsync(settings, cts.Token);
		_serviceCode = UniqueCode("E2eWs");
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

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register-web-service refuses method and parameter codes without the schema-name prefix")]
	[Description("Asks to register a method and a parameter whose codes lack the environment's schema-name prefix, which Studio refuses to save, and expects a refusal naming both codes and no service to be created.")]
	public async Task RegisterWebService_Should_Refuse_Unprefixed_Method_And_Parameter_Codes() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		if (_prefix.Length == 0) {
			Assert.Ignore($"'{_environmentName}' has no SchemaNamePrefix, so there is no prefix rule to refuse against.");
		}
		_serviceCode = UniqueCode("E2eWs");
		string unprefixedMethod = Unprefixed("GetOrders");
		string unprefixedParameter = Unprefixed("page");

		// Act
		WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = PackageName,
				["code"] = _serviceCode,
				["base-uri"] = "https://example.invalid/api",
				["methods"] = new[] {
					new Dictionary<string, object?> {
						["code"] = unprefixedMethod, ["http-method"] = "GET", ["uri"] = "orders",
						["request-parameters"] = new[] {
							new Dictionary<string, object?> { ["code"] = unprefixedParameter, ["in"] = "query", ["path"] = "page" }
						}
					}
				}
			}, cts.Token);
		WebServiceListResult listed = await CallAsync<WebServiceListResult>(ListWebServicesTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName }, cts.Token);

		// Assert
		refused.Success.Should().BeFalse(because: "Studio refuses to save a method whose codes lack the prefix, so the tool must not create one");
		refused.Error.Should().Contain($"'{unprefixedMethod}'", because: "the refusal names the unprefixed method code");
		refused.Error.Should().Contain($"'{unprefixedParameter}'",
			because: "the refusal names the unprefixed parameter code in the same answer");
		listed.Services.Should().NotContain(s => s.Code == _serviceCode, because: "a refused registration saves nothing");
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register-web-service refuses a single Object parameter and accepts its fields with dotted paths")]
	[Description("Asks to register a body parameter of type Object that is not an array, which Studio refuses and a Call web service element cannot bind, and expects a refusal with the dotted-path alternative and no service; then registers the same object as two fields with dotted paths and expects them stored as given.")]
	public async Task RegisterWebService_Should_Refuse_A_Single_Object_And_Accept_Dotted_Paths() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		_serviceCode = UniqueCode("E2eWs");
		Dictionary<string, object?> Register(params Dictionary<string, object?>[] requestParameters) => new() {
			["environment-name"] = _environmentName,
			["package-name"] = PackageName,
			["code"] = _serviceCode,
			["base-uri"] = "https://example.invalid/api",
			["methods"] = new[] {
				new Dictionary<string, object?> {
					["code"] = Prefixed("CreateOrder"), ["http-method"] = "POST", ["uri"] = "orders",
					["request-parameters"] = requestParameters
				}
			}
		};

		// Act
		WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			Register(new Dictionary<string, object?> {
				["code"] = Prefixed("Customer"), ["path"] = "customer", ["type"] = "Object",
				["items"] = new[] { new Dictionary<string, object?> { ["code"] = Prefixed("Name"), ["path"] = "name" } }
			}), cts.Token);
		WebServiceListResult afterRefusal = await CallAsync<WebServiceListResult>(ListWebServicesTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName }, cts.Token);
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			Register(
				new Dictionary<string, object?> { ["code"] = Prefixed("CustomerName"), ["path"] = "customer.name" },
				new Dictionary<string, object?> { ["code"] = Prefixed("CustomerEmail"), ["path"] = "customer.email" }),
			cts.Token);

		// Assert
		refused.Success.Should().BeFalse(because: "a single Object cannot be saved in Studio nor bound by a process element");
		refused.Error.Should().Contain("must be an array", because: "the refusal names the rule");
		refused.Error.Should().Contain("customer.name", because: "the refusal names the dotted-path alternative");
		afterRefusal.Services.Should().NotContain(s => s.Code == _serviceCode, because: "a refused registration saves nothing");
		registered.Success.Should().BeTrue(because: $"fields with dotted paths are the supported shape; error: {registered.Error}");
		registered.Service!.Methods.Single().RequestParameters.Select(p => p.Path).Should().BeEquivalentTo(
			["customer.name", "customer.email"], because: "the platform renders each dotted path into the nested body object");
	}

	[Test]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("register-web-service keeps header and cookie response parameters and refuses bodies Studio refuses")]
	[Description("Registers a method whose response parameters are read from a header, a cookie and the body, expects describe to report each placement, then expects a setMethod rebuilt from describe to keep them; finally expects an xml method with a body parameter and a GET with a body parameter to be refused in one answer.")]
	public async Task RegisterWebService_Should_Keep_Response_Placements_And_Refuse_Invalid_Bodies() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the web service tool E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await ResolvePrefixAsync(settings, cts.Token);
		_serviceCode = UniqueCode("E2eWs");
		string method = Prefixed("GetStatus");
		Dictionary<string, object?> Parameter(string code, string place, string path) =>
			new() { ["code"] = Prefixed(code), ["in"] = place, ["path"] = path };

		// Act
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["package-name"] = PackageName, ["code"] = _serviceCode,
				["base-uri"] = "https://example.invalid/api",
				["methods"] = new[] {
					new Dictionary<string, object?> {
						["code"] = method, ["http-method"] = "GET", ["uri"] = "status",
						["response-parameters"] = new[] {
							Parameter("RequestId", "header", "x-request-id"), Parameter("Session", "cookie", "sid"),
							Parameter("Status", "body", "$.status")
						}
					}
				}
			}, cts.Token);
		registered.Success.Should().BeTrue(because: $"header, cookie and body response parameters are valid; error: {registered.Error}");
		WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName, ["code"] = _serviceCode }, cts.Token);
		described.Success.Should().BeTrue(because: $"the round trip starts from describe; error: {described.Error}");
		WebServiceMethodView read = described.Service!.Methods.Single();
		WebServiceToolResult roundTrip = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = _serviceCode,
				["operations"] = new[] {
					new Dictionary<string, object?> {
						["op"] = "setMethod",
						["method"] = new Dictionary<string, object?> {
							["code"] = read.Code, ["http-method"] = read.HttpMethod, ["uri"] = read.Uri,
							// The parameters exactly as describe returned them, read-only fields included.
							["response-parameters"] = read.ResponseParameters
						}
					}
				}
			}, cts.Token);
		WebServiceToolResult refused = await CallAsync<WebServiceToolResult>(ModifyWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["code"] = _serviceCode,
				["operations"] = new[] {
					new Dictionary<string, object?> { ["op"] = "addMethod", ["method"] = new Dictionary<string, object?> {
						["code"] = Prefixed("PostXml"), ["http-method"] = "POST", ["uri"] = "xml", ["content-type"] = "xml",
						["request-parameters"] = new[] { Parameter("Amount", "body", "amount") } } },
					new Dictionary<string, object?> { ["op"] = "addMethod", ["method"] = new Dictionary<string, object?> {
						["code"] = Prefixed("GetWithBody"), ["http-method"] = "GET", ["uri"] = "get",
						["request-parameters"] = new[] { Parameter("Filter", "body", "filter") } } }
				}
			}, cts.Token);

		// Assert
		read.ResponseParameters.Select(p => p.In).Should().BeEquivalentTo(["header", "cookie", "body"],
			because: "describe reports where each response parameter is read from");
		roundTrip.Success.Should().BeTrue(because: $"a setMethod rebuilt from describe is valid; error: {roundTrip.Error}");
		roundTrip.Service!.Methods.Single().ResponseParameters.Select(p => p.In).Should().BeEquivalentTo(
			["header", "cookie", "body"], because: "the round trip must not turn a header or cookie value into a body value");
		refused.Success.Should().BeFalse(because: "Studio refuses an xml method with a body parameter and a GET with one");
		refused.Error.Should().Contain("xml method", because: "the xml refusal is reported");
		refused.Error.Should().Contain("GET method", because: "the GET refusal is reported in the same answer");
	}

	/// <summary>
	/// Inserts an OAuth application with placeholder endpoints and a placeholder client id; it carries no secret.
	/// Only ClientId is required by the entity. The id is recorded BEFORE the insert, so the teardown deletes the
	/// row even when the insert's answer fails an assertion.
	/// </summary>
	private async Task CreateOAuthApplicationAsync(McpE2ESettings settings, string name, CancellationToken token) {
		string id = Guid.NewGuid().ToString();
		_oauthApplicationId = id;
		ClioCliCommandResult inserted = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", _environmentName!, "--service-path", "DataService/json/SyncReply/InsertQuery",
			"-b", JsonSerializer.Serialize(new {
				rootSchemaName = "OAuthApplications",
				operationType = 1,
				columnValues = new {
					items = new {
						Id = new { expressionType = 2, parameter = new { dataValueType = 0, value = id } },
						Name = new { expressionType = 2, parameter = new { dataValueType = 1, value = name } },
						ClientId = new { expressionType = 2, parameter = new { dataValueType = 1, value = "e2e-placeholder-client" } },
						TokenUrl = new { expressionType = 2, parameter = new { dataValueType = 1, value = "https://example.invalid/oauth/token" } }
					}
				}
			})
		], cancellationToken: token);
		inserted.ExitCode.Should().Be(0,
			because: $"the arrange step needs an OAuth application; output: {inserted.StandardOutput} {inserted.StandardError}");
		using JsonDocument answer = JsonDocument.Parse(inserted.StandardOutput[inserted.StandardOutput.IndexOf('{')..]);
		answer.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(
			because: $"DataService must accept the insert; output: {inserted.StandardOutput}");
	}

	/// <summary>
	/// Reads the stand's <c>SchemaNamePrefix</c> through the platform's own system-setting query. An empty value is a
	/// valid answer: Studio and the tools then apply no prefix rule.
	/// </summary>
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

	/// <summary>A code that does not start with the stand's prefix, even when the plain name happens to.</summary>
	private string Unprefixed(string name) =>
		name.StartsWith(_prefix, StringComparison.Ordinal) ? "Zq" + name : name;

	private static object IdFilter(string rootSchemaName, string id) => new {
		rootSchemaName, filterType = 6, logicalOperation = 0, isEnabled = true,
		items = new {
			Id = new {
				filterType = 1, comparisonType = 3, isEnabled = true, dataValueType = 0,
				leftExpression = new { expressionType = 0, columnPath = "Id" },
				rightExpression = new { expressionType = 2, parameter = new { dataValueType = 0, value = id } }
			}
		}
	};

	private async Task<T> CallAsync<T>(string toolName, Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		return EntitySchemaStructuredResultParser.Extract<T>(result);
	}

	/// <summary>A start - Call web service - end process whose element calls <see cref="MethodCode"/>.</summary>
	private string CallerDescriptor(string processName, string serviceCode) => JsonSerializer.Serialize(new {
		name = processName,
		caption = processName,
		elements = new object[] {
			new { name = "Start1", type = "startEvent" },
			new { name = CallerElement, type = "webService", webService = new { service = serviceCode, method = MethodCode } },
			new { name = "End1", type = "endEvent" }
		},
		flows = new[] {
			new { source = "Start1", target = CallerElement },
			new { source = CallerElement, target = "End1" }
		},
		mappings = new[] { new { elementName = CallerElement, elementParameter = Prefixed("Id") + "_In", value = "1" } }
	});

	private Dictionary<string, object?> OrdersMethod(bool withHeader, string? literalHeader = null) {
		var requestParameters = new List<Dictionary<string, object?>> {
			new() { ["code"] = Prefixed("Id"), ["in"] = "path", ["path"] = "id" }
		};
		if (withHeader) {
			// SchemaNamePrefix exists on every stand, so the reference passes the existence check without
			// the test creating a system setting.
			requestParameters.Add(new() {
				["code"] = Prefixed("ApiKey"), ["in"] = "header", ["path"] = "X-Api-Key", ["default-sys-setting"] = "SchemaNamePrefix"
			});
		}
		if (literalHeader is not null) {
			requestParameters.Add(new() {
				["code"] = Prefixed("ApiKey"), ["in"] = "header", ["path"] = "X-Api-Key", ["default-value"] = literalHeader
			});
		}
		return new Dictionary<string, object?> {
			["code"] = MethodCode,
			["http-method"] = "GET",
			["uri"] = "/orders/{id}",
			["request-parameters"] = requestParameters,
			["response-parameters"] = new[] {
				new Dictionary<string, object?> { ["code"] = Prefixed("Status"), ["path"] = "$.status" },
				new Dictionary<string, object?> {
					["code"] = Prefixed("Items"), ["path"] = "$.items", ["type"] = "Object", ["array"] = true,
					["items"] = new[] { new Dictionary<string, object?> { ["code"] = Prefixed("Number"), ["path"] = "number" } }
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
