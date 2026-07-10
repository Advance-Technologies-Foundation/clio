using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>Guidance for authoring portable Creatio integration tests.</summary>
[McpServerResourceType]
public sealed class IntegrationTestingGuidanceResource {
	private const string ResourceUri = "docs://mcp/guides/integration-testing";

	/// <summary>Canonical integration-testing guidance.</summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       Creatio integration testing

		       Start with new-integration-test-project. The scaffold is scenario-neutral: NUnit,
		       FluentAssertions, ATF.Repository, Allure, settings, and a base fixture only.

		       Configuration
		       - NUnit parameters override process environment variables.
		       - Required: CREATIO_URL and CREATIO_IS_NETCORE=true|false.
		       - Configure exactly one authentication mode: CREATIO_ACCESS_TOKEN, or both
		         CREATIO_USERNAME and CREATIO_PASSWORD.
		       - Store credentials in CI secrets. Never commit or attach them to Allure results.

		       Test design
		       - Use NUnit, FluentAssertions, [AllureNUnit], human-readable Allure names/descriptions,
		         and explicit Arrange, Act, Assert, Cleanup steps.
		       - Generate only the models needed by the scenario with add-item model and
		         generate-process-model. Models are not part of the base scaffold.
		       - For object-signal processes, Act is the triggering insert/update. Poll the observable
		         business result and VwSysProcessLog; include process status and error text on failure.
		       - Cover positive flow, trigger/filter negatives, isolation, and idempotency where relevant.
		       - Wait for background work to finish before deleting parent data.

		       Browser scenarios
		       - Add Playwright only when browser behavior is part of the acceptance criteria.
		       - Choose C# or TypeScript according to the repository; the base C# project intentionally
		         does not force a browser dependency.
		       """
	};

	/// <summary>Returns integration-testing guidance.</summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "integration-testing-guidance")]
	[Description("Returns guidance for portable Creatio integration tests, process scenarios, CI authentication, Allure, and optional browser testing.")]
	public ResourceContents GetGuide() => Guide;
}
