using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.WebServiceDesigner;
using Clio.Common;
using Microsoft.AspNetCore.Http;
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

				 SOAP from a WSDL (a URL or files the user gives): call read-wsdl first and register one of the ports it returns, adding package-name; never hand-build what read-wsdl returned or infer a soap-action from another operation (describe by hand only what it says it left out). SOAP: type soap11 or soap12 with namespaces (prefix, uri; the first is the default); each method has message-element (e.g. tem:Add) and an optional soap-action (from the WSDL binding or the user, never made up) instead of http-method and uri, request parameters are body, header or cookie with element paths such as tem:intA (every prefix must be declared), soap-header on a top-level body parameter puts it in the envelope's Header, sequence-element names array items, and response paths are element paths such as AddResult, never JSONPath. A SOAP base URI is stored as given, without a trailing slash.

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
				 Changes a web service. For a REST or SOAP service it applies ordered operations: addMethod, setMethod (replaces a method of the same code, keeping the UIds of the parameters it keeps), removeMethod, setBaseUri, setRetryCount, setAuth. Credentials follow the register-web-service rules; a method with use-auth false sends no authentication, so after a change of the authentication a notice names such methods. Codes the service already has are kept as they are, prefix or not (never rename one to add it); a method or parameter this call adds follows the register-web-service prefix and path rules.

				 A SOAP service also takes setNamespaces (the whole list; a prefix a kept method uses must stay) and setType (soap11 or soap12; REST and SOAP cannot be switched); both change every call, as does setBaseUri, so the usage guard treats them as changing every method; setAuth changes the calls of the methods with use-auth true. Its methods follow the register-web-service SOAP rules, and describe-web-service output is valid setMethod and setNamespaces input.

				 BEFORE CALLING with setMethod or removeMethod: a process whose Call web service element calls that method loses its parameters on the next designer load and fails at run time, silently. setBaseUri, setAuth, setNamespaces and setType keep those parameters but change the calls the processes make. The tool refuses these changes (those four only when the service differs after the whole call; setAuth only for methods with use-auth true; setRetryCount never) while a process calls the service and lists the processes; show them to the user and repeat with acknowledge-usages naming them only after the user says yes. The caller needs CanManageSolution.
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
	[Description("Test-calls a web service method. A request to test is not a confirmation: for any method but a REST GET (POST, PUT, PATCH, DELETE, every SOAP method), first tell the user the call sends a real request that can change data in that API, and call only after their explicit yes, with user-confirmed: true (without it such a call is refused and nothing is sent). For a REST or SOAP method it sends one real request, as Studio's Send test request does, and returns the HTTP status, the response body and response-values, the response parameters as the platform parsed them (a value the API did not send shows the parameter's default, or null). Request and response headers are never returned whole; values sent from system settings (also inside a SOAP envelope and its Header) or in Authorization are masked, and so are response parameters read from a cookie, a credential-named header or with a setting or secret default. A REST method with content-type xml cannot be test-called; a process calls it through RequestBody. The caller needs CanManageSolution.")]
	public WebServiceTestResult TestWebServiceMethod(
		[Description("Parameters: environment-name, code, method (required); parameters (optional map of top-level parameter code to value; arrays and arrays of objects take JSON arrays), user-confirmed (optional; true only after the user's explicit yes to a call of any method but a REST GET)")]
		[Required]
		TestWebServiceMethodArgs args) {
		try {
			ArgumentNullException.ThrowIfNull(args);
			WebServiceToolRunner.RejectUnknownArguments(args.ExtensionData,
				"Valid: environment-name, code, method, parameters, user-confirmed.", WebServiceToolRunner.TestMethodAliases);
			IWebServiceDesigner designer = WebServiceToolRunner.Resolve(commandResolver, args.EnvironmentName);
			return designer.TestMethod(args.Code, args.Method, args.Parameters, args.UserConfirmed == true);
		} catch (Exception exception) {
			return new WebServiceTestResult(false, WebServiceToolRunner.Describe(exception));
		}
	}
}

/// <summary>Turns a SOAP WSDL into register-web-service arguments, as Studio's SOAP quick setup does; saves nothing.</summary>
[McpServerToolType]
public sealed class ReadWsdlTool(IToolCommandResolver commandResolver, IHttpContextAccessor httpContextAccessor = null) {

	internal const string ToolName = "read-wsdl";

	private const string WsdlUrl = "wsdl-url";
	private const string WsdlFiles = "wsdl-files";
	private const string ValidHint = "Valid: environment-name, wsdl-url, wsdl-files, port, code, service-code, operations.";

	/// <summary>Spellings agents use for the WSDL source, answered with a rename hint (never bound).</summary>
	internal static readonly IReadOnlyDictionary<string, string> Aliases = WebServiceToolRunner.WithAliases(
		McpToolArgumentSupport.EnvironmentNameAliases,
		("url", WsdlUrl), ("wsdl", WsdlUrl), ("wsdl_url", WsdlUrl), ("wsdlUrl", WsdlUrl),
		("files", WsdlFiles), ("wsdl-path", WsdlFiles), ("wsdl-file", WsdlFiles), ("wsdl_files", WsdlFiles),
		("wsdlFiles", WsdlFiles));

	/// <summary>Reads a WSDL into register-web-service arguments on the environment the call names.</summary>
	/// <param name="args">The arguments.</param>
	/// <returns>The arguments per SOAP port with warnings and notices, or the refusal as an error.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
	// In process, not Worker: wsdl-files is refused over HTTP by reading the host's HttpContext, which a relayed worker
	// call would not have; and service-code reads through the schema designer, which stays host-resident (ADR D9).
	[McpToolExecution(
		Location = McpToolExecutionLocation.InProcess,
		Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Reads a SOAP WSDL. It turns a SOAP WSDL 1.1 document into ready register-web-service arguments the way Studio's SOAP quick setup builds a service, and saves nothing.

				 Returns one argument object per SOAP 1.1 and SOAP 1.2 port (studio-pick marks the port Studio would create: the SOAP 1.2 one when both exist; it is not a recommendation, see warnings; HTTP ports are dropped): type, base URI, namespaces, and methods with message-element, soap-action and parameters with element paths, soap-header and sequence-element. Codes are the environment's schema-name prefix plus the full WSDL name in PascalCase (GetCustomerOrders -> UsrGetCustomerOrders; a nested field joins its whole path, wrappers included, customer/name -> UsrCustomerName), the same on every read. warnings list what was not read or needs a test call, and a port the platform may not be able to call as described (SOAP 1.2 dispatched by the Content-Type action, as WCF does; required WS-Addressing; WS-Security message security), naming the SOAP 1.1 port to register instead when there is one (for a plain SOAP 1.2 port only when WCF wrote the WSDL; otherwise as the way out of a failed test call).

				 wsdl-url is downloaded by the stand (Studio's WSDL download, imported schemas included; it turns non-ASCII text into ?), never by clio. wsdl-files reads local absolute paths: the WSDL and every schema it imports. Show the operations to the user, keep the ones they want, add package-name and call register-web-service with one port's arguments. When the user named the new service's code, pass it as code. With service-code (an existing SOAP service), operations that match its methods keep their codes (existing-methods: replace them with modify-web-service setMethod, add the rest with addMethod, the namespaces with setNamespaces). A WSDL too large to return whole comes back as operation names: read it again with operations. The caller needs CanManageSolution.
				 """)]
	public WsdlReadResult ReadWsdl(
		[Description("Parameters: environment-name, and wsdl-url or wsdl-files (required); port, code (new service) or service-code (existing service), operations (optional)")]
		[Required]
		ReadWsdlArgs args) {
		try {
			ArgumentNullException.ThrowIfNull(args);
			WebServiceToolRunner.RejectUnknownArguments(args.ExtensionData, ValidHint, Aliases);
			if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
				throw new ArgumentException("environment-name is required.");
			}
			// Over HTTP the caller is not on the machine whose files wsdl-files would read.
			if (args.WsdlFiles is { Count: > 0 } && httpContextAccessor?.HttpContext is not null) {
				throw new ArgumentException("wsdl-files reads files on the machine running clio, which an HTTP client cannot "
					+ "point at; give wsdl-url instead.");
			}
			IWsdlReader reader = commandResolver.Resolve<IWsdlReader>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			return reader.Read(new WsdlReadRequest(args.EnvironmentName, args.WsdlUrl, args.WsdlFiles, args.Port,
				args.ServiceCode, args.Operations, args.Code));
		} catch (Exception exception) {
			return new WsdlReadResult(false, WebServiceToolRunner.Describe(exception));
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

	/// <summary>The test-web-service-method argument that carries the user's explicit yes.</summary>
	internal const string UserConfirmed = "user-confirmed";

	/// <summary>
	/// <see cref="ServiceCodeAliases"/> plus the method-code and confirmation spellings of test-web-service-method; a
	/// confirmation under another name is answered with a rename hint, never taken as the user's yes.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> TestMethodAliases = WithAliases(ServiceCodeAliases,
		("method-code", "method"), ("method_code", "method"), ("methodCode", "method"),
		("confirmed", UserConfirmed), ("confirm", UserConfirmed), ("user_confirmed", UserConfirmed),
		("userConfirmed", UserConfirmed), ("confirmed-by-user", UserConfirmed));

	internal static IReadOnlyDictionary<string, string> WithAliases(IReadOnlyDictionary<string, string> source,
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

/// <summary>
/// The arguments of read-wsdl. Unknown keys land in <see cref="ExtensionData"/> and are refused with the valid names,
/// so a misspelled argument is not dropped silently.
/// </summary>
public sealed record ReadWsdlArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("wsdl-url")]
	[property: Description("WSDL URL the stand downloads, e.g. http://www.dneonline.com/calculator.asmx?WSDL; or give wsdl-files")]
	string WsdlUrl = null,

	[property: JsonPropertyName("wsdl-files")]
	[property: Description("Absolute local paths of the WSDL and every schema it imports (an import is matched by its schemaLocation or file name); or give wsdl-url")]
	IReadOnlyList<string> WsdlFiles = null,

	[property: JsonPropertyName("port")]
	[property: Description("One port name to return; every SOAP port when omitted")]
	string Port = null,

	[property: JsonPropertyName("service-code")]
	[property: Description("Only an EXISTING SOAP service the operations go into: methods that match keep their codes. For a new service use code")]
	string ServiceCode = null,

	[property: JsonPropertyName("operations")]
	[property: Description("Operation names to return, e.g. [\"Add\"]; all when omitted")]
	IReadOnlyList<string> Operations = null,

	[property: JsonPropertyName("code")]
	[property: Description("The code the user named for a NEW service, with the schema-name prefix, e.g. UsrErpOrders; the returned arguments carry it instead of the code built from the WSDL. Not with service-code")]
	string Code = null) {
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
	[property: Description("Values by request parameter code: text, a number or a boolean for a scalar; for a body array a JSON array of them, e.g. [\"a\", \"b\"]; for an array of objects a JSON array of objects by field code, e.g. [{\"UsrSku\": \"A1\", \"UsrQty\": 2}], with arrays inside items given the same way. A path, query, header or cookie value is one text even when marked as an array (a list as \"1,2\"). Every code, nested ones included, must be the method's; null leaves a value out")]
	IReadOnlyDictionary<string, JsonElement> Parameters = null,

	[property: JsonPropertyName(WebServiceToolRunner.UserConfirmed)]
	[property: Description("True only after the user explicitly said yes to this call; a request to test is not a confirmation. Any method but a REST GET (POST, PUT, PATCH, DELETE, every SOAP method) is refused without it and nothing is sent")]
	bool? UserConfirmed = null) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

