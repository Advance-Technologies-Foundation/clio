using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for testing Creatio configuration web services.
/// </summary>
[McpServerResourceType]
public sealed class ConfigurationWebServiceTestsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string GuidePath = "mcp/guides/configuration-webservice-tests";
	private const string GuideUri = DocsScheme + "://" + GuidePath;
	private const string ReferencePath = "mcp/references/configuration-webservice-tests";

	internal static readonly TextResourceContents Guide = new() {
		Uri = GuideUri,
		MimeType = "text/plain",
		Text = """
		       configuration-webservice-tests

		       Write, update, or review tests for Creatio Configuration Web Services under test folders such as tests/PkgOne/EntryPoints/WebServices. Use when adding endpoint tests, building workspace-specific web-service fixtures, enforcing AAA structure, requiring [Description] on every test, adding because reasons to assertions, checking status-code mapping, or validating DTO, Stream, or manual-response endpoint behavior. Pair with configuration-webservice when production endpoint code changes.

		       Non-Negotiable Rules
		       - Add or update tests for production web-service changes unless the user explicitly says not to.
		       - Structure every test method with explicit // Arrange, // Act, and // Assert sections.
		       - Decorate every test with [Description("...")].
		       - Add because: "..." to every assertion.
		       - Prefer the local workspace test base and fixture pattern over repository-agnostic examples.
		       - Assume the endpoint return type must be concrete. If production code returns an interface or object, flag it as incorrect rather than writing tests around it.
		       - Prefer mocking application-service dependencies in web-service tests. Inject the substitute through the package composition root and assert the dependency was called with the expected parameters.

		       Workflow
		       1. Identify whether the endpoint returns DTO, void, or Stream.
		       2. Build or update the local HTTP-context fixture for the package.
		       3. Inject substitutes for application-service dependencies through the package composition root when testing the web-service entry point.
		       4. Add focused success and negative-path tests.
		       5. Assert explicit HTTP status mapping whenever the endpoint sets it.
		       6. Assert the dependency was called with the mapped request values.
		       7. Build and run tests sequentially in this workspace.

		       References
		       Read only what you need:
		       - docs://mcp/references/configuration-webservice-tests/test-fixture-pattern: local fixture shape, composition-root reset, and dependency-injection mocking pattern
		       - docs://mcp/references/configuration-webservice-tests/assertion-style: AAA layout, [Description], and because assertion rules
		       - docs://mcp/references/configuration-webservice-tests/endpoint-test-patterns: what to assert for DTO, void, and Stream endpoints

		       Build And Verify
		       Typical commands:

		       ```powershell
		       dotnet build .\MainSolution.slnx -c dev-n8 -v d
		       dotnet test .\tests\<PACKAGE_NAME>\<PACKAGE_NAME>.Tests.csproj -c dev-n8 --no-build
		       ```

		       Use the matching dev-nf configuration for net472 targets.

		       Run build and test sequentially in this workspace. Parallel dotnet build and dotnet test can lock package outputs under obj.

		       What To Report Back
		       - Test files changed, with one-line reason per file
		       - Coverage intent for each added or updated test
		       - Build/test commands run, or the exact blocker if not run
		       - Any workspace-specific fixture or dependency issue discovered while wiring the tests
		       """
	};

	internal static readonly TextResourceContents TestFixturePattern = CreateReference(
		"test-fixture-pattern",
		"""
		Test Fixture Pattern

		Use the local workspace fixture pattern instead of repository-agnostic examples.

		Recommended Shape

		```csharp
		using System;
		using Microsoft.Extensions.DependencyInjection;
		using NSubstitute;
		using NUnit.Framework;
		using <PackageNamespace>;
		using Terrasoft.Web.Http.Abstractions;

		[TestFixture(Category = "PreCommit")]
		public class <ServiceName>TestFixture : BaseComposableAppTestFixture {
			private HttpApplicationState _application;
			private HttpContext _context;
			private HttpResponse _response;
			private HttpSessionState _session;
			private IHttpContextAccessor _httpContextAccessor;
			private I<Dependency> _dependency;

			protected override void SetUp() {
				base.SetUp();
				_dependency = Substitute.For<I<Dependency>>();
				<PackageNamespace>.<PackageNamespace>.InjectedServices =
					new[] { new Func<IServiceCollection, IServiceCollection>(services => {
						services.AddSingleton(_dependency);
						return services;
					}) };
				<PackageNamespace>.<PackageNamespace>.Instance.Reset();
				_application = Substitute.For<HttpApplicationState>();
				_context = Substitute.For<HttpContext>();
				_response = Substitute.For<HttpResponse>();
				_session = Substitute.For<HttpSessionState>();
				_context.Application.Returns(_application);
				_context.Response.Returns(_response);
				_context.Session.Returns(_session);
				_httpContextAccessor = CustomSetupHttpContextAccessor(_context, UserConnection);
			}

			protected override void TearDown() {
				<PackageNamespace>.<PackageNamespace>.InjectedServices = null;
				<PackageNamespace>.<PackageNamespace>.Instance.Reset();
				base.TearDown();
			}
		}
		```

		Notes
		- Do not copy [MockSettings(RequireMock.All)] unless the current test project actually supports it.
		- Reset the package composition root in SetUp() when the package uses one, such as PkgOneApp.Instance.Reset().
		- Register test doubles through InjectedServices before resetting the composition root, so the web service resolves the substitute from DI.
		- Keep the fixture focused on HTTP context setup. Test business logic separately in service-layer tests when that improves clarity.
		""");

	internal static readonly TextResourceContents AssertionStyle = CreateReference(
		"assertion-style",
		"""
		Assertion Style

		Required Style
		- Use explicit // Arrange, // Act, and // Assert comments in every test.
		- Add [Description("...")] to every test method.
		- Add because: "..." to every assertion.

		Example

		```csharp
		[Test]
		[Description("Returns HTTP 400 when division by zero is requested")]
		public void Calculate_DivideByZero_SetsBadRequestStatus() {
			// Arrange
			var sut = new CalculatorService {
				HttpContextAccessor = _httpContextAccessor
			};
			var request = new CalculatorRequest {
				Left = 10,
				Right = 0,
				Operation = "divide"
			};

			// Act
			CalculatorResponse response = sut.Calculate(request);

			// Assert
			response.Success.Should().BeFalse(
				because: "division by zero should be returned as a validation failure");
			response.Message.Should().Be("Right operand must not be zero.",
				because: "the endpoint should explain why the request was rejected");
			_response.StatusCode.Should().Be(400,
				because: "validation failures should be exposed as HTTP 400");
		}
		```
		""");

	internal static readonly TextResourceContents EndpointTestPatterns = CreateReference(
		"endpoint-test-patterns",
		"""
		Endpoint Test Patterns

		What To Assert
		Test what the endpoint actually controls:
		- DTO-returning method: assert returned object and any custom Response.StatusCode
		- void response writer: assert Response.StatusCode, ContentType, and written body from OutputStream
		- Stream response: assert Response.StatusCode, ContentType, and stream content
		- For DTO endpoints backed by an application service, assert the dependency was called with the expected mapped arguments.

		Minimum coverage per endpoint:
		- Success path
		- One negative path
		- Status code behavior
		- Response payload or stream content

		Workflow
		1. Identify whether the endpoint returns DTO, void, or Stream.
		2. Build a fixture that provides the service with a mocked HttpContextAccessor.
		3. Inject substitutes for application-service dependencies through the package composition root.
		4. Add focused success and negative-path tests.
		5. Assert the HTTP status code whenever the endpoint can set one explicitly.
		6. Assert dependency invocation arguments for entry-point mapping.
		7. Assert body or stream content for manual-writer or Stream endpoints.
		8. Keep service-layer behavior in separate unit tests when that improves clarity.
		""");

	/// <summary>
	/// Returns the canonical guidance article for testing Creatio configuration web services.
	/// </summary>
	[McpServerResource(UriTemplate = GuideUri, Name = "configuration-webservice-tests-guidance")]
	[Description("Returns canonical MCP guidance for testing Creatio configuration web services.")]
	public ResourceContents GetGuide() => Guide;

	/// <summary>
	/// Returns local fixture patterns for Creatio configuration web-service tests.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/test-fixture-pattern", Name = "configuration-webservice-tests-test-fixture-pattern-reference")]
	[Description("Returns local fixture patterns for Creatio configuration web-service tests.")]
	public ResourceContents GetTestFixturePattern() => TestFixturePattern;

	/// <summary>
	/// Returns assertion style guidance for Creatio configuration web-service tests.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/assertion-style", Name = "configuration-webservice-tests-assertion-style-reference")]
	[Description("Returns assertion style guidance for Creatio configuration web-service tests.")]
	public ResourceContents GetAssertionStyle() => AssertionStyle;

	/// <summary>
	/// Returns endpoint coverage patterns for Creatio configuration web-service tests.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferencePath + "/endpoint-test-patterns", Name = "configuration-webservice-tests-endpoint-test-patterns-reference")]
	[Description("Returns endpoint coverage patterns for Creatio configuration web-service tests.")]
	public ResourceContents GetEndpointTestPatterns() => EndpointTestPatterns;

	private static TextResourceContents CreateReference(string name, string text) =>
		new() {
			Uri = $"{DocsScheme}://{ReferencePath}/{name}",
			MimeType = "text/plain",
			Text = text
		};
}
