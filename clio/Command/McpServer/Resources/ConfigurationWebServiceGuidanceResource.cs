using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating Creatio configuration web services.
/// </summary>
[McpServerResourceType]
public sealed class ConfigurationWebServiceGuidanceResource {
	private const string DocsScheme = "docs";
	private const string GuidePath = "mcp/guides/configuration-webservice";
	private const string GuideUri = DocsScheme + "://" + GuidePath;
	private const string ReferencePath = "mcp/references/configuration-webservice";

	internal static readonly TextResourceContents Guide = new() {
		Uri = GuideUri,
		MimeType = "text/plain",
		Text = """
		       creatio-config-webservice

		       Implement or review Creatio Configuration Web Services under package EntryPoints/WebServices folders such as packages/PkgOne/src/cs/EntryPoints/WebServices or packages/PkgOne/Files/src/cs/EntryPoints/WebServices. Use when creating endpoint classes, adding web methods, defining request/response DTO contracts, mapping custom HTTP status codes, documenting NET472 vs NETSTANDARD2_0 route differences, or wiring endpoint logic to package-level DI/application services. If the task includes tests, also use configuration-webservice-tests.

		       Non-Negotiable Rules
		       - Inherit BaseService, IReadOnlySessionState.
		       - Add [ServiceContract] and [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)].
		       - Mark request and response DTOs with [DataContract] and serialized members with [DataMember].
		       - Use a concrete return type for every web-service method. Do not return an interface or object, because Creatio will fail at runtime when serializing the response.
		       - Keep the web service thin. Put business logic in an application/service layer when possible.
		       - Never throw for expected business or validation flow. Return an error as a value when that pattern exists in the workspace.
		       - If tests are part of the task, also follow configuration-webservice-tests.

		       Required Location And Shape
		       Source path:
		       - packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/WebServices/<ServiceName>.cs

		       Namespace:
		       - <PackageNamespace>.EntryPoints.WebServices

		       Minimal class skeleton:

		       ```csharp
		       using System.ServiceModel;
		       using System.ServiceModel.Activation;
		       using System.Web.SessionState;
		       using Terrasoft.Web.Common;

		       namespace <PackageNamespace>.EntryPoints.WebServices {
		       	[ServiceContract]
		       	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
		       	public class <ServiceName> : BaseService, IReadOnlySessionState {
		       	}
		       }
		       ```

		       Framework Differences You Must Document

		       Route Prefix
		       - NET472: https://<creatio-host>/0/rest/<ServiceName>/<MethodName>
		       - NETSTANDARD2_0 and newer runtime: https://<creatio-host>/rest/<ServiceName>/<MethodName>

		       Do not document only one route if the package targets both runtimes.

		       Setting Custom Status Code

		       ```csharp
		       #if NETSTANDARD2_0
		       HttpContextAccessor.GetInstance().Response.StatusCode = 400;
		       #else
		       WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.BadRequest;
		       #endif
		       ```

		       Use a DTO return when the endpoint is a normal JSON API and only the status code changes.
		       Use void plus Response.OutputStream or return Stream only when transport-level control is the point of the endpoint.

		       References
		       Read only what you need:
		       - docs://mcp/references/configuration-webservice/dto-patterns: request/response DTO rules and concrete return-type examples
		       - docs://mcp/references/configuration-webservice/status-code-patterns: framework-specific status handling and response-style tradeoffs
		       - docs://mcp/references/configuration-webservice/composition-root-pattern: package DI registration and thin-service pattern
		       - docs://mcp/references/configuration-webservice/manual-runtime-checklist: manual endpoint verification steps after implementation

		       Response Patterns
		       Choose one pattern deliberately.

		       1. Return DTO And Let Creatio Serialize It

		       ```csharp
		       [DataContract(Name = "demo-request")]
		       public class DemoRequest {
		       	[DataMember(Name = "name")]
		       	public string Name { get; set; }
		       }

		       [DataContract(Name = "demo-response")]
		       public class DemoResponse {
		       	[DataMember(Name = "success")]
		       	public bool Success { get; set; }

		       	[DataMember(Name = "message")]
		       	public string Message { get; set; }
		       }

		       [OperationContract]
		       [WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json,
		       	ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		       public DemoResponse Execute(DemoRequest request) {
		       	DemoRequest model = request ?? new DemoRequest();
		       	if (string.IsNullOrWhiteSpace(model.Name)) {
		       		SetStatusCode(400);
		       		return new DemoResponse {
		       			Success = false,
		       			Message = "Name is required."
		       		};
		       	}
		       	return new DemoResponse {
		       		Success = true,
		       		Message = $"Hello, {model.Name}"
		       	};
		       }
		       ```

		       Notes:
		       - Creatio serializes the returned object.
		       - The return type must be a concrete DTO type, not an interface and not object.
		       - Default success status is typically 200.
		       - Custom status code is still possible. Set it before return.

		       2. Return void And Write The Body Yourself

		       ```csharp
		       [OperationContract]
		       [WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json,
		       	ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		       public void Ping() {
		       	string json = "{\"status\":\"accepted\"}";
		       	byte[] payload = Encoding.UTF8.GetBytes(json);

		       #if NETSTANDARD2_0
		       	var response = HttpContextAccessor.GetInstance().Response;
		       	response.ContentType = "application/json; charset=utf-8";
		       	response.StatusCode = 202;
		       	response.OutputStream.Write(payload, 0, payload.Length);
		       	response.OutputStream.Flush();
		       #else
		       	WebOperationContext.Current.OutgoingResponse.ContentType = "application/json; charset=utf-8";
		       	WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Accepted;
		       	HttpContextAccessor.GetInstance().Response.OutputStream.Write(payload, 0, payload.Length);
		       	HttpContextAccessor.GetInstance().Response.OutputStream.Flush();
		       #endif
		       }
		       ```

		       Use this only when you need explicit control of both body and status.

		       3. Return Stream For File/Text/Binary Responses

		       ```csharp
		       [OperationContract]
		       [WebInvoke(Method = "GET", BodyStyle = WebMessageBodyStyle.Bare, UriTemplate = "ui/build-stamp")]
		       public Stream GetBuildStamp() {
		       	byte[] content = Encoding.UTF8.GetBytes("123");
		       	var response = HttpContextAccessor.GetInstance().Response;
		       	response.ContentType = "text/plain; charset=utf-8";
		       	response.StatusCode = 200;
		       	return new MemoryStream(content);
		       }
		       ```

		       Use this for HTML, plain text, assets, or binary downloads.

		       Implementation Workflow
		       1. Inspect the package path and namespace first.
		       2. Decide whether the endpoint is DTO, custom body writer, or Stream.
		       3. Implement request validation at the entry point.
		       4. Delegate non-transport logic to the application layer when possible.
		       5. Register new application services in the package composition root, for example by updating the Init() method inside a class like PkgOneApp.
		       6. Map result to HTTP status code and response body explicitly.
		       7. If tests are required, apply configuration-webservice-tests.
		       8. Build and run tests when production code changed.

		       Custom Status Code Helper

		       ```csharp
		       private void SetStatusCode(int statusCode) {
		       #if NETSTANDARD2_0
		       	HttpContextAccessor.GetInstance().Response.StatusCode = statusCode;
		       #else
		       	WebOperationContext.Current.OutgoingResponse.StatusCode = (HttpStatusCode)statusCode;
		       #endif
		       }
		       ```

		       Typical mappings:
		       - Validation failure: 400
		       - Unauthorized: 401
		       - Forbidden: 403
		       - Not found: 404
		       - Conflict: 409
		       - Unexpected failure: 500

		       Review Checklist For Existing Services
		       1. Class inherits BaseService, IReadOnlySessionState.
		       2. Service and methods have the required WCF attributes.
		       3. Method return types are concrete DTO or Stream/void, never an interface or object.
		       4. Route documentation matches both NET472 and NETSTANDARD2_0 when relevant.
		       5. Response style is intentional: DTO vs void vs Stream.
		       6. Custom status-code handling is correct for the target frameworks.
		       7. Expected validation/business failures are mapped to response values, not exceptions.
		       8. Supporting services are registered in the package composition root when needed.
		       9. UI/file-serving code does not contain copied package names, paths, or messages from another package.

		       Build And Verify
		       For net472 targets:

		       ```powershell
		       dotnet build .\MainSolution.slnx -c dev-nf -v d
		       ```

		       For modern targets:

		       ```powershell
		       dotnet build .\MainSolution.slnx -c dev-n8 -v d
		       ```

		       If production code changed, run the relevant test command after the build. In this workspace, run build and test sequentially because parallel runs can lock package outputs under obj.

		       What To Report Back
		       - Files changed, with one-line reason per file
		       - Which response pattern was chosen and why
		       - How status codes are handled for NET472 and NETSTANDARD2_0
		       - Tests added or updated, or the reason tests were not changed
		       - Build/test commands run, or the exact blocker if not run
		       """
	};

	internal static readonly TextResourceContents DtoPatterns = CreateReference(
		"dto-patterns",
		"""
		DTO Patterns

		Contract Rules
		- Mark request and response DTOs with [DataContract].
		- Mark serialized members with [DataMember].
		- Return a concrete DTO type from web-service methods.
		- Do not return an interface.
		- Do not return object.

		Concrete DTO Example

		```csharp
		[DataContract(Name = "calculator-request")]
		public class CalculatorRequest {
			[DataMember(Name = "left")]
			public double Left { get; set; }

			[DataMember(Name = "right")]
			public double Right { get; set; }

			[DataMember(Name = "operation")]
			public string Operation { get; set; }
		}

		[DataContract(Name = "calculator-response")]
		public class CalculatorResponse {
			[DataMember(Name = "success")]
			public bool Success { get; set; }

			[DataMember(Name = "result", EmitDefaultValue = false)]
			public double Result { get; set; }

			[DataMember(Name = "message")]
			public string Message { get; set; }

			[DataMember(Name = "operation")]
			public string Operation { get; set; }
		}
		```

		DTO Return Pattern

		```csharp
		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public CalculatorResponse Calculate(CalculatorRequest request) {
			CalculatorRequest model = request ?? new CalculatorRequest();
			if (string.IsNullOrWhiteSpace(model.Operation)) {
				SetStatusCode(400);
				return new CalculatorResponse {
					Success = false,
					Message = "Operation is required.",
					Operation = string.Empty
				};
			}

			return new CalculatorResponse {
				Success = true,
				Result = 42,
				Message = "Calculation completed.",
				Operation = model.Operation
			};
		}
		```

		Forbidden Patterns
		- public ICalculatorResponse Calculate(...)
		- public object Calculate(...)
		- DTOs with object payload fields when a concrete payload type is known
		""");

	internal static readonly TextResourceContents StatusCodePatterns = CreateReference(
		"status-code-patterns",
		"""
		Status Code Patterns

		Route Prefixes
		- NET472: /0/rest/<ServiceName>/<MethodName>
		- NETSTANDARD2_0 and newer runtime: /rest/<ServiceName>/<MethodName>

		Document both routes when the package targets both runtimes.

		Set Status Code

		```csharp
		private void SetStatusCode(int statusCode) {
		#if NETSTANDARD2_0
			HttpContextAccessor.GetInstance().Response.StatusCode = statusCode;
		#else
			WebOperationContext.Current.OutgoingResponse.StatusCode = (HttpStatusCode)statusCode;
		#endif
		}
		```

		Choose The Right Response Style
		Use DTO return when:
		- the endpoint is a normal JSON API
		- Creatio serialization is acceptable
		- you only need to set status code before returning

		Use void and manual body writing when:
		- you need explicit control over body and status
		- the transport contract is the main concern

		Use Stream when:
		- returning HTML, text, assets, or binary content

		Manual Body Example

		```csharp
		[OperationContract]
		[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public void Ping() {
			string json = "{\"status\":\"accepted\"}";
			byte[] payload = Encoding.UTF8.GetBytes(json);

		#if NETSTANDARD2_0
			var response = HttpContextAccessor.GetInstance().Response;
			response.ContentType = "application/json; charset=utf-8";
			response.StatusCode = 202;
			response.OutputStream.Write(payload, 0, payload.Length);
			response.OutputStream.Flush();
		#else
			WebOperationContext.Current.OutgoingResponse.ContentType = "application/json; charset=utf-8";
			WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Accepted;
			HttpContextAccessor.GetInstance().Response.OutputStream.Write(payload, 0, payload.Length);
			HttpContextAccessor.GetInstance().Response.OutputStream.Flush();
		#endif
		}
		```

		Forbidden Pattern
		- Do not return 204 NoContent and also write a response body.
		""");

	internal static readonly TextResourceContents CompositionRootPattern = CreateReference(
		"composition-root-pattern",
		"""
		Composition Root Pattern

		Keep the service thin and push non-transport logic into an application service.

		Service Layer Example

		```csharp
		public interface ICalculatorEngine {
			ErrorOr<double> Calculate(double left, double right, string operation);
		}

		internal sealed class CalculatorEngine : ICalculatorEngine {
			public ErrorOr<double> Calculate(double left, double right, string operation) {
				// Core logic lives here, not in the web service.
			}
		}
		```

		Package Registration Example

		Register new services in the package composition root.

		```csharp
		private static ServiceProvider Init() {
			ServiceCollection serviceCollection = new ServiceCollection();
			// UserConnection is owned by the Creatio platform (the per-request connection).
			// Never register it as a container-managed (scoped/transient) service: the DI
			// container disposes any IDisposable it resolves from a factory when the scope
			// closes, which would tear down the platform connection's DB executors and clear
			// UserConnection.Current mid-request. Expose it through a Func accessor instead so
			// the container never owns its lifetime; the platform disposes it at request end.
			serviceCollection.AddTransient<Func<UserConnection>>(sp => () => UserConnection);
			serviceCollection.AddSingleton<ICalculatorEngine, CalculatorEngine>();
			return serviceCollection.BuildServiceProvider();
		}
		```

		A service that genuinely needs the connection takes the accessor and resolves it per call
		(never store the resolved connection in a field, and never dispose it):

		```csharp
		internal sealed class ContactRepository : IContactRepository {
			private readonly Func<UserConnection> _userConnectionAccessor;

			public ContactRepository(Func<UserConnection> userConnectionAccessor) {
				_userConnectionAccessor = userConnectionAccessor;
			}

			public int CountContacts() {
				UserConnection userConnection = _userConnectionAccessor();
				// Use userConnection for the current request (ESQ/Select/etc.).
				// Do not cache it across requests and do not dispose it.
				return new EntitySchemaQuery(userConnection.EntitySchemaManager, "Contact")
					.GetEntityCollection(userConnection).Count;
			}
		}
		```

		Web Service Usage Example

		```csharp
		public CalculatorResponse Calculate(CalculatorRequest request) {
			CalculatorRequest model = request ?? new CalculatorRequest();
			using (IServiceScope scope = PkgOneApp.Instance.CreateScope()) {
				ICalculatorEngine calculator = scope.ServiceProvider.GetRequiredService<ICalculatorEngine>();
				ErrorOr<double> result = calculator.Calculate(model.Left, model.Right, model.Operation);
				if (result.IsError) {
					SetStatusCode(400);
					return new CalculatorResponse {
						Success = false,
						Message = result.FirstError.Description,
						Operation = model.Operation ?? string.Empty
					};
				}
				return new CalculatorResponse {
					Success = true,
					Result = result.Value,
					Message = "Calculation completed.",
					Operation = model.Operation ?? string.Empty
				};
			}
		}
		```

		UserConnection Lifetime Rule
		- The platform owns the per-request UserConnection and disposes it at request end.
		- Never register UserConnection as a scoped or transient service, and never dispose it
		  from package code. Inject Func<UserConnection> and call it where the connection is needed.
		- Service scopes are still fine for genuinely package-owned scoped services; just keep
		  UserConnection out of the container's lifetime tracking.
		""");

	internal static readonly TextResourceContents ManualRuntimeChecklist = CreateReference(
		"manual-runtime-checklist",
		"""
		Manual Runtime Checklist

		Use this after implementation when you need a concrete runtime verification plan.

		Endpoint Checklist
		1. Authenticate in Creatio and keep the required session or auth headers.
		2. Call the endpoint with the correct route for the target runtime:
		   - NET472: /0/rest/<ServiceName>/<MethodName>
		   - NETSTANDARD2_0: /rest/<ServiceName>/<MethodName>
		3. Send a representative success request.
		4. Send at least one representative failure request.
		5. Verify:
		   - HTTP status code
		   - response body shape
		   - DTO field names
		   - error message or code for failure path
		6. Record one success response and one failure response in task notes.

		What To Watch For
		- route mismatches between NET472 and NETSTANDARD2_0
		- DTO fields not serialized as expected
		- methods returning interface or object types
		- custom status codes not being applied on one of the target frameworks
		""");

	/// <summary>
	/// Returns the canonical guidance article for Creatio configuration web services.
	/// </summary>
	[McpServerResource(UriTemplate = GuideUri, Name = "configuration-webservice-guidance")]
	[Description("Returns canonical MCP guidance for implementing Creatio configuration web services.")]
	public ResourceContents GetGuide() => Guide;

	/// <summary>
	/// Returns request and response DTO patterns for Creatio configuration web services.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/dto-patterns", Name = "configuration-webservice-dto-patterns-reference")]
	[Description("Returns request and response DTO patterns for Creatio configuration web services.")]
	public ResourceContents GetDtoPatterns() => DtoPatterns;

	/// <summary>
	/// Returns route and HTTP status-code patterns for Creatio configuration web services.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/status-code-patterns", Name = "configuration-webservice-status-code-patterns-reference")]
	[Description("Returns route and HTTP status-code patterns for Creatio configuration web services.")]
	public ResourceContents GetStatusCodePatterns() => StatusCodePatterns;

	/// <summary>
	/// Returns composition-root and service-layer patterns for Creatio configuration web services.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/composition-root-pattern", Name = "configuration-webservice-composition-root-pattern-reference")]
	[Description("Returns composition-root and service-layer patterns for Creatio configuration web services.")]
	public ResourceContents GetCompositionRootPattern() => CompositionRootPattern;

	/// <summary>
	/// Returns manual runtime verification guidance for Creatio configuration web services.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/manual-runtime-checklist", Name = "configuration-webservice-manual-runtime-checklist-reference")]
	[Description("Returns manual runtime verification guidance for Creatio configuration web services.")]
	public ResourceContents GetManualRuntimeChecklist() => ManualRuntimeChecklist;

	private static TextResourceContents CreateReference(string name, string text) =>
		new() {
			Uri = $"{DocsScheme}://{ReferencePath}/{name}",
			MimeType = "text/plain",
			Text = text
		};
}
