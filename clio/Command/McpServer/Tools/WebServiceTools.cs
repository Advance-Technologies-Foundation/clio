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

/// <summary>Registers a REST web service in Studio > Web services (ENG-105475).</summary>
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
				 Registers a REST web service (base URI, methods, request and response parameters, authentication) in a package, exactly as Studio > Web services creates one, so a business process can call it with the Call web service element.

				 Credentials are never values: basic/digest auth and every header, cookie, key or token parameter reference a SYSTEM SETTING code (it must exist), and the user fills its value; a constant there, or user:password@ in base-uri, is refused. The code must carry the environment's schema-name prefix and must not be used by another service (an existing service is changed with modify-web-service). The caller needs CanManageSolution. Returns the registered service with its parameter tree and the steps left to the user.
				 """)]
	public WebServiceToolResult RegisterWebService(
		[Description("Parameters: environment-name, package-name, code, base-uri, methods (required); caption, retry-count, auth (optional)")]
		[Required]
		RegisterWebServiceArgs args) =>
		WebServiceToolRunner.Run(commandResolver, args?.EnvironmentName, args?.ExtensionData,
			"Valid: environment-name, package-name, code, base-uri, methods, caption, retry-count, auth.",
			designer => designer.Register(
			args.PackageName, args.Code, args.Caption, args.BaseUri, args.RetryCount, args.Auth, args.Methods));
}

/// <summary>Lists registered web services by name only; method trees come from describe-web-service.</summary>
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
	[Description("Lists the web services registered in Studio > Web services (code, caption, REST or SOAP, package). Use it before register-web-service to reuse an existing service, and before a Call web service element to find the service code.")]
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
	[Description("Reads a registered web service: base URI, authentication type and the system settings it reads, and every method with its request and response parameter trees. Credential values and constant defaults of header, cookie, key or token parameters are never returned (default-masked: true). With usages: true it also lists the processes and Call web service elements that call each method. The caller needs CanManageSolution.")]
	public WebServiceToolResult DescribeWebService(
		[Description("Parameters: environment-name, code (required); usages (optional, default false)")]
		[Required]
		DescribeWebServiceArgs args) =>
		WebServiceToolRunner.Run(commandResolver, args?.EnvironmentName, args?.ExtensionData,
			"Valid: environment-name, code, usages.",
			designer => designer.Describe(args.Code, args.Usages ?? false));
}

/// <summary>Changes a registered REST web service, refusing a change that would break calling processes.</summary>
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
				 Changes a registered REST web service by ordered operations: addMethod, setMethod (replaces a method of the same code, keeping the UIds of the parameters it keeps), removeMethod, setBaseUri, setRetryCount, setAuth. Credentials follow the register-web-service rules.

				 BEFORE CALLING with setMethod or removeMethod: a process whose Call web service element calls that method loses its parameters on the next designer load and fails at run time, silently. The tool refuses such a change and lists the processes; show them to the user and repeat with acknowledge-usages naming them only after the user says yes. The caller needs CanManageSolution.
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

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Sends one real request through a registered web service method, as Studio's Send test request does, and returns the HTTP status and the response body (request and response headers are never returned; values sent from system settings or in Authorization are masked in the body). The call reaches the external API, so a POST, PUT, PATCH or DELETE method changes data there: confirm with the user first. The caller needs CanManageSolution.")]
	public WebServiceTestResult TestWebServiceMethod(
		[Description("Parameters: environment-name, code, method (required); parameters (optional map of top-level parameter code to value)")]
		[Required]
		TestWebServiceMethodArgs args) {
		try {
			ArgumentNullException.ThrowIfNull(args);
			WebServiceToolRunner.RejectUnknownArguments(args.ExtensionData,
				"Valid: environment-name, code, method, parameters.");
			IWebServiceDesigner designer = WebServiceToolRunner.Resolve(commandResolver, args.EnvironmentName);
			return designer.TestMethod(args.Code, args.Method, args.Parameters);
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

	// Non-resident tools get no flat-argument classification upstream, so a misspelled key would otherwise
	// be dropped at bind time and the call would succeed without it.
	public static void RejectUnknownArguments(IReadOnlyDictionary<string, JsonElement> extensionData, string validHint) {
		string error = McpToolArgumentSupport.BuildUnknownArgumentError(extensionData, validHint);
		if (error is not null) {
			throw new ArgumentException(error);
		}
	}

	public static WebServiceToolResult Run(IToolCommandResolver resolver, string environmentName,
		IReadOnlyDictionary<string, JsonElement> extensionData, string validHint,
		Func<IWebServiceDesigner, WebServiceToolResult> action) {
		try {
			RejectUnknownArguments(extensionData, validHint);
			return action(Resolve(resolver, environmentName));
		} catch (Exception exception) {
			return new WebServiceToolResult(false, Describe(exception));
		}
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
	[property: Description("Absolute http(s) URI every method address is appended to, without credentials")]
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
	WebServiceAuthArg Auth = null) {
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
	[property: Description("Code of the registered REST service")]
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
	[property: Required]
	string Code,

	[property: JsonPropertyName("method")]
	[property: Description("Method code")]
	[property: Required]
	string Method,

	[property: JsonPropertyName("parameters")]
	[property: Description("Values of top-level request parameters by code")]
	IReadOnlyDictionary<string, string> Parameters = null) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

