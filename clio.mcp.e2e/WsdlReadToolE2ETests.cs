using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>read-wsdl through the real clio mcp-server, with no environment.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ReadWsdlTool.ToolName)]
[Parallelizable(ParallelScope.Self)]
public sealed class WsdlReadToolContractE2ETests : McpContractFixtureBase {

	[Test]
	[AllureName("read-wsdl is discoverable and refuses a guessed argument before touching any environment")]
	[Description("read-wsdl is reachable on the lazy surface, and a guessed source argument gets a rename hint instead of being dropped at bind time.")]
	public async Task ReadWsdl_Should_Be_Discoverable_And_Refuse_A_Guessed_Argument() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> names =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		CallToolResult call = await context.Session.CallToolAsync(ReadWsdlTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["url"] = "http://www.dneonline.com/calculator.asmx?WSDL"
				}
			}, context.CancellationTokenSource.Token);
		WsdlReadResult result = EntitySchemaStructuredResultParser.Extract<WsdlReadResult>(call);

		// Assert
		names.Should().Contain(ReadWsdlTool.ToolName, because: "non-resident tools are discovered through the compact index");
		result.Success.Should().BeFalse(because: "url is not an argument of read-wsdl");
		result.Error.Should().Contain("'url' -> 'wsdl-url'", because: "the refusal names the argument to use");
	}
}

/// <summary>
/// read-wsdl against a live stand (slice C): a WSDL shipped with the tests is read in wsdl-files mode, its Studio pick is
/// registered with register-web-service under a throwaway code and read back with describe-web-service, and removed in the
/// teardown. Codes come from the stand's own schema-name prefix.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ReadWsdlTool.ToolName)]
[NonParallelizable]
public sealed class WsdlReadToolE2ETests : McpContractFixtureBase {

	private const string PackageName = "Custom";
	private readonly List<string> _serviceCodes = [];
	private string? _environmentName;
	private string _prefix = string.Empty;

	private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", "Wsdl", name);

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
	[AllureTag(ReadWsdlTool.ToolName)]
	[AllureName("read-wsdl warns about WCF ports the platform cannot call and names the SOAP 1.1 port")]
	[Description("Reads a WSDL written by a WCF host (wsdl-files) with a SOAP 1.1, a SOAP 1.2, a SOAP 1.2 WS-Addressing and a wsHttpBinding port, and expects all four ports back with a warning on each of the last three that names the SOAP 1.1 port to register instead; nothing is saved.")]
	public async Task ReadWsdl_Should_Warn_About_Wcf_Ports_The_Platform_Cannot_Call() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the read-wsdl E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));

		// Act
		WsdlReadResult read = await CallAsync<WsdlReadResult>(ReadWsdlTool.ToolName, new Dictionary<string, object?> {
			["environment-name"] = _environmentName, ["wsdl-files"] = new[] { Asset("wcf-bindings.wsdl") }
		}, cts.Token);

		// Assert
		read.Success.Should().BeTrue(because: $"the shipped WCF WSDL is readable; error: {read.Error}");
		read.Services!.Select(s => s.Port).Should().Equal(
			["BasicHttpBinding_ICalc", "CustomBinding_ICalc", "CustomBinding_ICalc1", "WSHttpBinding_ICalc"],
			because: "every SOAP port is still returned");
		read.Warnings.Should().Contain(w => w.Contains("Port 'CustomBinding_ICalc' is SOAP 1.2")
				&& w.Contains("Register port 'BasicHttpBinding_ICalc'"),
			because: "WCF refuses the platform's SOAP 1.2 call, which carries the action only in a SOAPAction header");
		read.Warnings.Should().Contain(w => w.Contains("Port 'CustomBinding_ICalc1' requires WS-Addressing")
				&& w.Contains("Register port 'BasicHttpBinding_ICalc'"),
			because: "the platform writes no wsa:Action or wsa:To");
		read.Warnings.Should().Contain(w => w.Contains("Port 'WSHttpBinding_ICalc' requires WS-Addressing")
				&& w.Contains("Register port 'BasicHttpBinding_ICalc'"),
			because: "a wsHttpBinding requires WS-Addressing");
	}

	[Test]
	[AllureTag(ReadWsdlTool.ToolName)]
	[AllureTag(RegisterWebServiceTool.ToolName)]
	[AllureName("read-wsdl output registers a SOAP service that describe reads back the same")]
	[Description("Reads a WSDL shipped with the tests (wsdl-files) with the new service's code given as code, expects both SOAP ports with the SOAP 1.2 one as Studio's pick, that code in the arguments and the other codes built from the stand's prefix, registers that port's arguments trimmed to three operations (a SOAP-header array, an object array, a complex header with a body) under a throwaway code, and expects describe-web-service to report the same namespaces, message elements, SOAP actions, paths, SOAP-header flags and item elements; a second read with service-code returns the same codes as existing methods.")]
	public async Task ReadWsdl_Output_Should_Register_And_Describe_The_Same_Service() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		_environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the read-wsdl E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		_prefix = await WebServiceStand.ReadPrefixAsync(settings, _environmentName, cts.Token);
		string[] operations = ["TaggedCall", "GetByRoles", "SecureCall"];
		string code = _prefix + "E2eWsdl" + Guid.NewGuid().ToString("N")[..12];

		// Act
		WsdlReadResult read = await CallAsync<WsdlReadResult>(ReadWsdlTool.ToolName, new Dictionary<string, object?> {
			["environment-name"] = _environmentName, ["wsdl-files"] = new[] { Asset("wizard-features.wsdl") },
			["operations"] = operations, ["code"] = code
		}, cts.Token);
		read.Success.Should().BeTrue(because: $"the shipped WSDL is readable; error: {read.Error}");
		WsdlServiceView pick = read.Services!.Single(s => s.StudioPick);
		JsonObject arguments = JsonNode.Parse(pick.Arguments!.Value.GetRawText())!.AsObject();
		arguments["code"]!.GetValue<string>().Should().Be(code,
			because: "the code the user named for the new service is already in the arguments, which are registered as they are");
		arguments["package-name"] = PackageName;
		_serviceCodes.Add(code);
		WebServiceToolResult registered = await CallAsync<WebServiceToolResult>(RegisterWebServiceTool.ToolName,
			arguments.Deserialize<Dictionary<string, object?>>()!, cts.Token);
		WebServiceToolResult described = await CallAsync<WebServiceToolResult>(DescribeWebServiceTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = _environmentName, ["code"] = code }, cts.Token);
		WsdlReadResult reread = await CallAsync<WsdlReadResult>(ReadWsdlTool.ToolName, new Dictionary<string, object?> {
			["environment-name"] = _environmentName, ["wsdl-files"] = new[] { Asset("wizard-features.wsdl") },
			["operations"] = operations, ["port"] = pick.Port, ["service-code"] = code
		}, cts.Token);

		// Assert
		read.Services!.Select(s => s.Port).Should().Equal(["ClioWizardFeaturesSoap", "ClioWizardFeaturesSoap12"],
			because: "every SOAP port is returned and the HTTP port is dropped");
		pick.Port.Should().Be("ClioWizardFeaturesSoap12", because: "Studio's quick setup picks the SOAP 1.2 port");
		read.Warnings.Should().Contain(w => w.Contains("2 SOAP headers"), because: "SecureCall's second header is not read");
		read.Warnings.Should().Contain(w => w.Contains("Port 'ClioWizardFeaturesSoap12' is SOAP 1.2") && w.Contains("Keep this port"),
			because: "outside a WCF WSDL the SOAP 1.2 pick stays, with the SOAP 1.1 port only as the way out of a failed call");
		registered.Success.Should().BeTrue(because: $"read-wsdl output is valid register input; error: {registered.Error}");
		described.Success.Should().BeTrue(because: $"the registered service is readable; error: {described.Error}");
		WebServiceView service = described.Service!;
		service.Type.Should().Be("SOAP 1.2", because: "the pick is the SOAP 1.2 port");
		service.Namespaces!.Select(n => (n.Prefix, n.Uri, n.Default)).Should().Equal(
			JsonNode.Parse(pick.Arguments!.Value.GetRawText())!["namespaces"]!.AsArray()
				.Select((n, i) => (n!["prefix"]!.GetValue<string>(), n["uri"]!.GetValue<string>(), i == 0)),
			because: "the namespaces are saved as read, the first being the default");
		WebServiceMethodView tagged = service.Methods.Single(m => m.Code == _prefix + "TaggedCall");
		tagged.MessageElement.Should().Be("tem:TaggedCall", because: "the message element is stored with its prefix");
		tagged.SoapAction.Should().Be("http://example.org/clio/wizard/TaggedCall", because: "the SOAP action comes from the binding");
		WebServiceParameterView tags = tagged.RequestParameters.Single();
		(tags.Path, tags.SoapHeader, tags.Array, tags.SequenceElement).Should().Be(("tem:TagsHeader", true, true, "tem:Tag"),
			because: "a SOAP-header array round-trips with its item element");
		WebServiceParameterView roles = service.Methods.Single(m => m.Code == _prefix + "GetByRoles").RequestParameters
			.Single(p => p.Array);
		(roles.Type, roles.SequenceElement, roles.Items.Count).Should().Be(("Object", "ns0:RoleObject", 3),
			because: "an object array keeps its item element and its fields");
		WebServiceMethodView secure = service.Methods.Single(m => m.Code == _prefix + "SecureCall");
		secure.RequestParameters.Where(p => p.SoapHeader == true).Select(p => p.Path).Should().BeEquivalentTo(
			["tem:AuthHeader/tem:UserName", "tem:AuthHeader/tem:Token", "tem:AuthHeader/tem:Scope/tem:Tenant"],
			because: "the complex header is flattened, without the wizard's copy of Tenant one level too high");
		reread.Success.Should().BeTrue(because: $"the WSDL read again with service-code gives a result; error: {reread.Error}");
		reread.Services!.Single().ExistingMethods.Should().BeEquivalentTo(service.Methods.Select(m => m.Code),
			because: "a second read gives the same codes (D11), so every method is matched for setMethod");
	}

	private async Task<T> CallAsync<T>(string toolName, Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		return EntitySchemaStructuredResultParser.Extract<T>(result);
	}
}

/// <summary>
/// read-wsdl in URL mode against a live stand: the stand downloads the public calculator WSDL through its own WSDL
/// endpoint. It needs the stand to reach the internet, so it is run by hand ([Explicit], McpE2E.Manual) and ignored when
/// the download fails; an automatic lane would count that ignore against its Skipped == 0 gate.
/// </summary>
[TestFixture]
[Explicit("Needs a stand that reaches http://www.dneonline.com; run by hand.")]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[AllureNUnit]
[AllureFeature(ReadWsdlTool.ToolName)]
[NonParallelizable]
public sealed class WsdlReadUrlToolE2ETests : McpContractFixtureBase {

	private const string CalculatorWsdl = "http://www.dneonline.com/calculator.asmx?WSDL";

	[Test]
	[AllureTag(ReadWsdlTool.ToolName)]
	[AllureName("read-wsdl reads a WSDL the stand downloads")]
	[Description("The stand downloads the public calculator WSDL through Studio's WSDL endpoint; read-wsdl returns its two SOAP ports with Add as the stable code prefix+Add and the message element tem:Add. Ignored when the stand cannot reach the internet.")]
	public async Task ReadWsdl_Should_Read_A_Url_The_Stand_Downloads() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(settings,
			"Configure McpE2E:Sandbox:EnvironmentName to run the read-wsdl E2E tests.");
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		string prefix = await WebServiceStand.ReadPrefixAsync(settings, environmentName, cts.Token);

		// Act
		CallToolResult call = await Session.CallToolAsync(ReadWsdlTool.ToolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> { ["environment-name"] = environmentName, ["wsdl-url"] = CalculatorWsdl }
		}, cts.Token);
		WsdlReadResult result = EntitySchemaStructuredResultParser.Extract<WsdlReadResult>(call);
		if (!result.Success && (result.Error ?? string.Empty).Contains("download the WSDL", StringComparison.Ordinal)) {
			Assert.Ignore($"The stand could not download {CalculatorWsdl}: {result.Error}");
		}

		// Assert
		result.Success.Should().BeTrue(because: $"the stand downloaded the WSDL; error: {result.Error}");
		result.Services!.Select(s => (s.Port, s.StudioPick)).Should().Equal(
			[("CalculatorSoap", false), ("CalculatorSoap12", true)], because: "both SOAP ports are returned, the SOAP 1.2 one picked");
		JsonNode add = JsonNode.Parse(result.Services![0].Arguments!.Value.GetRawText())!["methods"]!.AsArray()
			.Single(m => m!["caption"]!.GetValue<string>() == "Add")!;
		add["code"]!.GetValue<string>().Should().Be(prefix + "Add", because: "the code is the stand's prefix and the operation name");
		add["message-element"]!.GetValue<string>().Should().Be("tem:Add", because: "the root element is qualified");
	}
}
