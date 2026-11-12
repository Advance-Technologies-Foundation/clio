using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.WebServiceDesigner;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Registers a REST or SOAP web service in Studio > Web services (ENG-105475).</summary>
[McpServerToolType]
public sealed class RegisterWebServiceTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "register-web-service";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Registers a web service. A REST, SOAP 1.1 or SOAP 1.2 service is saved in a package with its base URI, methods, request and response parameters and authentication, exactly as Studio > Web services creates one, so a business process can call it with the Call web service element.

				 SOAP: type soap11 or soap12 with namespaces (prefix, uri; the first is the default); each method has message-element (e.g. tem:Add) and soap-action instead of http-method and uri, request parameters are body, header or cookie with element paths such as tem:intA (every prefix must be declared), soap-header on a top-level body parameter puts it in the envelope's Header, sequence-element names array items, and response paths are element paths such as AddResult, never JSONPath. A SOAP base URI is stored as given, without a trailing slash.

				 Credentials are never values: basic/digest auth and every header, cookie, key or token parameter reference a SYSTEM SETTING code (it must exist), and the user fills its value; a constant there (a soap-header parameter included), or user:password@ in base-uri, a namespace uri or a soap-action, is refused. The service code and every method and parameter code must carry the environment's schema-name prefix (Studio refuses to save a method without it; wire names go in uri and path), and the service code must not be used by another service (an existing service is changed with modify-web-service). The caller needs CanManageSolution. Returns the registered service with its parameter tree and the steps left to the user.
				 """)]
	public WebServiceToolResult RegisterWebService(
		[Description("Parameters: environment-name, package-name, code, base-uri, methods (required); caption, retry-count, auth, type, namespaces (optional)")]
		[Required]
		RegisterWebServiceArgs args) =>
		WebServiceToolRunner.Run(commandResolver, args?.EnvironmentName, args?.ExtensionData,
			"Valid: environment-name, package-name, code, base-uri, methods, caption, retry-count, auth, type, namespaces.",
			designer => designer.Register(args.PackageName, new WebServiceRegistration(
				args.Code, args.Caption, args.BaseUri, args.RetryCount, args.Auth, args.Methods, args.Type, args.Namespaces)));
}

/// <summary>Lists registered web services with code, caption, type, package and base URI; method trees come from describe-web-service.</summary>
[McpServerToolType]
public sealed class ListWebServicesTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "list-web-services";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Lists web services. Each comes with its code, caption, REST or SOAP, package and base URI, as Studio > Web services shows it. Use it before register-web-service to reuse an existing service, and before a Call web service element to find the service code.")]
	public WebServiceListResult ListWebServices(
		[Description("Parameters: environment-name (required)")] [Required] WebServiceEnvironmentArgs args) {
		try {
			WebServiceToolRunner.RejectUnknownArguments(args?.ExtensionData, "Valid: environment-name.");
			IWebServiceDesigner designer = WebServiceToolRunner.Resolve(commandResolver, args?.EnvironmentName);
			return new WebServiceListResult(true, Services: designer.List());
		} catch (Exception exception) {
			return new WebServiceListResult(false, WebServiceToolRunner.Describe(exception));
		}
	}
}

/// <summary>Reads one web service with its methods and parameter trees, stripped of every credential value.</summary>
[McpServerToolType]
public sealed class DescribeWebServiceTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "describe-web-service";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Reads one web service. REST, SOAP 1.1 and SOAP 1.2 services are read alike: it returns the base URI, the authentication type and the system settings it reads, and every method with its request and response parameter trees. A SOAP service also returns its namespaces (the default first), and each SOAP method its message-element and soap-action instead of a uri; SOAP parameters carry soap-header and sequence-element where they apply, and their paths are element paths (tem:Outer/tem:Inner). Credential values and constant defaults of header, cookie, key or token parameters are never returned (default-masked: true). With usages: true it also lists the processes and Call web service elements that call each method. The caller needs CanManageSolution.")]
	public WebServiceToolResult DescribeWebService(
		[Description("Parameters: environment-name, code (required); usages (optional, default false)")]
		[Required]
		DescribeWebServiceArgs args) =>
		WebServiceToolRunner.Run(commandResolver, args?.EnvironmentName, args?.ExtensionData,
			"Valid: environment-name, code, usages.",
			designer => designer.Describe(args.Code, args.Usages ?? false));
}

/// <summary>Changes a registered REST or SOAP web service, refusing a change that would break calling processes.</summary>
[McpServerToolType]
public sealed class ModifyWebServiceTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "modify-web-service";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Changes a web service. For a REST or SOAP service it applies ordered operations: addMethod, setMethod (replaces a method of the same code, keeping the UIds of the parameters it keeps), removeMethod, setBaseUri, setRetryCount, setAuth. Credentials follow the register-web-service rules. Codes the service already has are kept as they are, prefix or not (never rename one to add it); a method or parameter this call adds follows the register-web-service prefix and path rules.

				 A SOAP service also takes setNamespaces (the whole list; a prefix a kept method uses must stay) and setType (soap11 or soap12; REST and SOAP cannot be switched); both change every call, so the usage guard treats them as changing every method. Its methods follow the register-web-service SOAP rules, and describe-web-service output is valid setMethod and setNamespaces input.

				 BEFORE CALLING with setMethod, removeMethod, setNamespaces or setType: a process whose Call web service element calls that method loses its parameters on the next designer load and fails at run time, silently. The tool refuses such a change and lists the processes; show them to the user and repeat with acknowledge-usages naming them only after the user says yes. The caller needs CanManageSolution.
				 """)]
	public WebServiceToolResult ModifyWebService(
		[Description("Parameters: environment-name, code, operations (required); acknowledge-usages (optional)")]
		[Required]
		ModifyWebServiceArgs args) =>
		WebServiceToolRunner.Run(commandResolver, args?.EnvironmentName, args?.ExtensionData,
			"Valid: environment-name, code, operations, acknowledge-usages.",
			designer => designer.Modify(args.Code, args.Operations, args.AcknowledgeUsages));
}

/// <summary>Sends one test request through a registered method.</summary>
[McpServerToolType]
public sealed class TestWebServiceMethodTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "test-web-service-method";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Test-calls a web service method. For a REST or SOAP method it sends one real request, as Studio's Send test request does, and returns the HTTP status, the response body and response-values, the response parameters as the platform parsed them (a value the API did not send shows the parameter's default, or null). Request and response headers are never returned whole; values sent from system settings (also inside a SOAP envelope and its Header) or in Authorization are masked, and so are response parameters read from a cookie, a credential-named header or with a setting or secret default. The call reaches the external API, so a POST, PUT, PATCH or DELETE method and every SOAP method can change data there: confirm with the user first. A REST method with content-type xml cannot be test-called; a process calls it through RequestBody. The caller needs CanManageSolution.")]
	public WebServiceTestResult TestWebServiceMethod(
		[Description("Parameters: environment-name, code, method (required); parameters (optional map of top-level parameter code to value)")]
		[Required]
		TestWebServiceMethodArgs args) {
		try {
			ArgumentNullException.ThrowIfNull(args);
			WebServiceToolRunner.RejectUnknownArguments(args.ExtensionData,
				"Valid: environment-name, code, method, parameters.", WebServiceToolRunner.TestMethodAliases);
			IWebServiceDesigner designer = WebServiceToolRunner.Resolve(commandResolver, args.EnvironmentName);
			return designer.TestMethod(args.Code, args.Method, WebServiceToolRunner.ToWireValues(args.Parameters));
		} catch (Exception exception) {
			return new WebServiceTestResult(false, WebServiceToolRunner.Describe(exception));
		}
	}
}

internal static class WebServiceToolRunner {

	public static IWebServiceDesigner Resolve(IToolCommandResolver resolver, string environmentName) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("environment-name is required.");
		}
		return resolver.Resolve<IWebServiceDesigner>(new EnvironmentOptions { Environment = environmentName });
	}

	/// <summary>
	/// Spellings agents use for the service code, answered with a rename hint (never bound): agents that skip the
	/// contract send service-code or method-code, measured in isolated runs. Built on the shared environment-name
	/// aliases and case-insensitive like them, because the binder matches names case-insensitively. Only spellings that
	/// can mean nothing but the code are listed: a bare <c>service</c> may wrap a whole object and a <c>service-name</c>
	/// may be a caption, so those stay unknown keys.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> ServiceCodeAliases = WithAliases(
		McpToolArgumentSupport.EnvironmentNameAliases,
		("service-code", "code"), ("service_code", "code"), ("serviceCode", "code"));

	/// <summary><see cref="ServiceCodeAliases"/> plus the method-code spellings of test-web-service-method.</summary>
	public static readonly IReadOnlyDictionary<string, string> TestMethodAliases = WithAliases(ServiceCodeAliases,
		("method-code", "method"), ("method_code", "method"), ("methodCode", "method"));

	private static IReadOnlyDictionary<string, string> WithAliases(IReadOnlyDictionary<string, string> source,
		params (string Alias, string Canonical)[] extra) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach ((string alias, string canonical) in source) {
			map[alias] = canonical;
		}
		foreach ((string alias, string canonical) in extra) {
			map[alias] = canonical;
		}
		return map;
	}

	// Non-resident tools get no flat-argument classification upstream, so a misspelled key would otherwise
	// be dropped at bind time and the call would succeed without it.
	public static void RejectUnknownArguments(IReadOnlyDictionary<string, JsonElement> extensionData, string validHint,
		IReadOnlyDictionary<string, string> aliases = null) {
		string error = McpToolArgumentSupport.BuildLegacyAliasError(extensionData,
			aliases ?? McpToolArgumentSupport.EnvironmentNameAliases, ".", validHint);
		if (error is not null) {
			throw new ArgumentException(error);
		}
	}

	public static WebServiceToolResult Run(IToolCommandResolver resolver, string environmentName,
		IReadOnlyDictionary<string, JsonElement> extensionData, string validHint,
		Func<IWebServiceDesigner, WebServiceToolResult> action) {
		try {
			RejectUnknownArguments(extensionData, validHint, ServiceCodeAliases);
			return action(Resolve(resolver, environmentName));
		} catch (Exception exception) {
			return new WebServiceToolResult(false, Describe(exception));
		}
	}

	/// <summary>
	/// Test-call values as the platform's test call takes them: text. A JSON number or boolean is written as its JSON
	/// text (1.5, true), so an agent that sends an amount as a number is not refused; null leaves the parameter out.
	/// </summary>
	public static IReadOnlyDictionary<string, string> ToWireValues(IReadOnlyDictionary<string, JsonElement> values) {
		if (values is null) {
			return null;
		}
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string code, JsonElement value) in values) {
			switch (value.ValueKind) {
				case JsonValueKind.Null or JsonValueKind.Undefined:
					break;
				case JsonValueKind.String:
					result[code] = value.GetString();
					break;
				case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
					result[code] = value.GetRawText();
					break;
				default:
					throw new ArgumentException($"parameters: '{code}' is an object or an array; the test call takes "
						+ "one text, number or boolean value per top-level parameter.");
			}
		}
		return result;
	}

	public static string Describe(Exception exception) =>
		SensitiveErrorTextRedactor.Redact(exception.Message);
}

public sealed record WebServiceEnvironmentArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}


public sealed record RegisterWebServiceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Existing package that receives the service, e.g. Custom")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("code")]
	[property: Description("Service code with the environment's schema-name prefix, e.g. UsrErpOrders")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("base-uri")]
	[property: Description("Absolute http(s) URI without credentials: REST appends every method address to it; SOAP calls it as given, e.g. http://www.dneonline.com/calculator.asmx")]
	[property: Required]
	string BaseUri,

	[property: JsonPropertyName("methods")]
	[property: Description("At least one method")]
	[property: Required]
	IReadOnlyList<WebServiceMethodArg> Methods,

	[property: JsonPropertyName("caption")]
	string Caption = null,

	[property: JsonPropertyName("retry-count")]
	[property: Description("Retries on call failure; default 0")]
	int? RetryCount = null,

	[property: JsonPropertyName("auth")]
	[property: Description("Optional authentication; credentials as system-setting codes only")]
	WebServiceAuthArg Auth = null,

	[property: JsonPropertyName("type")]
	[property: Description("rest (default), soap11 or soap12")]
	string Type = null,

	[property: JsonPropertyName("namespaces")]
	[property: Description("SOAP only, at least one: the namespaces the envelope declares, e.g. [{\"prefix\": \"tem\", \"uri\": \"http://tempuri.org/\"}]; the first is the default unless one is marked default")]
	IReadOnlyList<WebServiceNamespaceArg> Namespaces = null) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}


public sealed record DescribeWebServiceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("code")]
	[property: Description("Service code, e.g. UsrErpOrders")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("usages")]
	[property: Description("Also list the processes that call the service (scans process metadata; slower)")]
	bool? Usages = null) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}


public sealed record ModifyWebServiceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("code")]
	[property: Description("Code of the registered REST or SOAP service")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("operations")]
	[property: Description("Operations applied in order, all or nothing")]
	[property: Required]
	IReadOnlyList<WebServiceOperationArg> Operations,

	[property: JsonPropertyName("acknowledge-usages")]
	[property: Description("Process codes the user agreed may break, exactly as the refusal listed them")]
	IReadOnlyList<string> AcknowledgeUsages = null) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}


public sealed record TestWebServiceMethodArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("code")]
	[property: Description("Web service code (its schema name), e.g. UsrErpOrders; the argument is code, not service-code")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("method")]
	[property: Description("Method code, e.g. UsrGetOrders; the argument is method, not method-code")]
	[property: Required]
	string Method,

	[property: JsonPropertyName("parameters")]
	[property: Description("Values of top-level request parameters by code; text, numbers and booleans are accepted")]
	IReadOnlyDictionary<string, JsonElement> Parameters = null) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

