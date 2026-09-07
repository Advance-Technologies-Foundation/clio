using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end contract tests for email-template MCP tools.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[NonParallelizable]
public sealed class EmailTemplateToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes both email-template tools through the compact lazy-surface index with correct safety flags.")]
	[AllureTag(EmailTemplateTool.GetToolName)]
	[AllureTag(EmailTemplateTool.UpdateToolName)]
	[AllureName("Email-template MCP tools are discoverable")]
	public async Task Tools_ShouldBeAdvertised_WhenMcpServerStarts() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);

		// Assert
		ToolContractIndexEntry get = index.Should()
			.ContainSingle(entry => entry.Name == EmailTemplateTool.GetToolName,
				because: "the lazy MCP surface must expose the dedicated email read workflow")
			.Which;
		get.Destructive.Should().NotBeTrue(because: "get-email-template is read-only");
		ToolContractIndexEntry update = index.Should()
			.ContainSingle(entry => entry.Name == EmailTemplateTool.UpdateToolName,
				because: "the lazy MCP surface must expose the guarded email write workflow")
			.Which;
		update.Destructive.Should().BeTrue(because: "update-email-template overwrites remote content");
	}

	[Test]
	[Description("Binds get-email-template arguments through the real MCP server and validates the host identifier before environment access.")]
	[AllureTag(EmailTemplateTool.GetToolName)]
	[AllureName("get-email-template binds and validates arguments")]
	public async Task Get_ShouldRejectInvalidEmailId_WhenCalledThroughMcp() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			EmailTemplateTool.GetToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["email-id"] = "marketing-email", ["environment-name"] = $"missing-{Guid.NewGuid():N}"
				}
			},
			arrange.CancellationTokenSource.Token);
		EmailTemplateContentResponse response =
			EntitySchemaStructuredResultParser.Extract<EmailTemplateContentResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "domain validation failures are returned in the tool's structured response envelope");
		response.Success.Should().BeFalse(because: "a non-GUID cannot identify an email host record");
		response.Error.Should().Be("email-id must be a GUID.",
			because: "the MCP caller needs a field-specific correction");
	}

	[Test]
	[Description("Binds update-email-template through the real MCP server and refuses an unconfirmed write before environment access.")]
	[AllureTag(EmailTemplateTool.UpdateToolName)]
	[AllureName("update-email-template enforces confirmation")]
	public async Task Update_ShouldRefuseWrite_WhenConfirmIsFalseThroughMcp() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			EmailTemplateTool.UpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["email-id"] = Guid.NewGuid().ToString("D"),
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["format"] = "beefree",
					["expected-checksum"] = new string('0', 64),
					["confirm"] = false,
					["page-json"] = "{}",
					["page-html"] = "<html></html>"
				}
			},
			arrange.CancellationTokenSource.Token);
		EmailTemplateUpdateResponse response =
			EntitySchemaStructuredResultParser.Extract<EmailTemplateUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a declined write is a structured safety result, not an MCP transport failure");
		response.Success.Should().BeFalse(because: "the destructive operation was not confirmed");
		response.Error.Should().Contain("confirm=true",
			because: "the caller needs to know the explicit authorization requirement");
	}
}

/// <summary>Opt-in live-stand round-trip coverage for current Beefree email content.</summary>
[TestFixture]
[Category("McpE2E.Creatio")]
[AllureNUnit]
[NonParallelizable]
public sealed class EmailTemplateToolLiveE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Reads, writes back unchanged, and re-reads an existing Beefree variant through the real MCP server.")]
	[AllureTag(EmailTemplateTool.GetToolName)]
	[AllureTag(EmailTemplateTool.UpdateToolName)]
	[AllureName("Beefree email content survives a guarded live round trip")]
	public async Task Update_ShouldPreserveBeefreeContent_WhenLiveRoundTripUsesReturnedChecksum() {
		// Arrange
		string environmentName = Environment.GetEnvironmentVariable("CLIO_EMAIL_TEMPLATE_E2E_ENVIRONMENT") ?? string.Empty;
		string emailId = Environment.GetEnvironmentVariable("CLIO_EMAIL_TEMPLATE_E2E_EMAIL_ID") ?? string.Empty;
		if (string.IsNullOrWhiteSpace(environmentName) || !Guid.TryParse(emailId, out _)) {
			Assert.Ignore("Set CLIO_EMAIL_TEMPLATE_E2E_ENVIRONMENT and CLIO_EMAIL_TEMPLATE_E2E_EMAIL_ID to run the live Beefree round trip.");
		}
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		EmailTemplateContentResponse before = await GetAsync(
			arrange.Session, environmentName, emailId, arrange.CancellationTokenSource.Token);
		before.Success.Should().BeTrue(
			because: $"the configured live email must be readable before mutation; server error: {before.Error}");
		EmailTemplateContentVariant variant = before.Variants.Should()
			.ContainSingle(item => item.Format == "beefree" && item.Exists,
				because: "the configured host must contain one current-designer variant for this acceptance test")
			.Which;

		// Act
		CallToolResult updateResult = await arrange.Session.CallToolAsync(
			EmailTemplateTool.UpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["email-id"] = emailId,
					["environment-name"] = environmentName,
					["format"] = "beefree",
					["expected-checksum"] = variant.Checksum,
					["confirm"] = true,
					["language"] = variant.Language ?? string.Empty,
					["page-json"] = variant.PageJson,
					["page-html"] = variant.PageHtml,
					["amp-html"] = variant.AmpHtml ?? string.Empty,
					["template-version"] = variant.TemplateVersion
				}
			},
			arrange.CancellationTokenSource.Token);
		EmailTemplateUpdateResponse update =
			EntitySchemaStructuredResultParser.Extract<EmailTemplateUpdateResponse>(updateResult);
		EmailTemplateContentResponse after = await GetAsync(
			arrange.Session, environmentName, emailId, arrange.CancellationTokenSource.Token);
		EmailTemplateContentVariant afterVariant = after.Variants.Single(item =>
			item.Format == "beefree" && item.Exists &&
			string.Equals(item.Language ?? string.Empty, variant.Language ?? string.Empty,
				StringComparison.OrdinalIgnoreCase));

		// Assert
		updateResult.IsError.Should().NotBeTrue(because: "the guarded write should complete as a normal MCP call");
		update.Success.Should().BeTrue(because: "the checksum came from the immediately preceding live read");
		update.Created.Should().BeFalse(because: "the configured Beefree row already exists");
		after.Success.Should().BeTrue(because: "the updated email must remain readable");
		afterVariant.PageJson.Should().Be(variant.PageJson,
			because: "the current designer source must survive the guarded round trip byte-for-byte");
		afterVariant.PageHtml.Should().Be(variant.PageHtml,
			because: "the rendered HTML must stay paired with its designer source");
		afterVariant.Checksum.Should().Be(update.Checksum,
			because: "the update receipt must describe the content subsequently read from Creatio");
	}

	private static async Task<EmailTemplateContentResponse> GetAsync(
		McpServerSession session, string environmentName, string emailId, CancellationToken cancellationToken) {
		CallToolResult result = await session.CallToolAsync(
			EmailTemplateTool.GetToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["email-id"] = emailId, ["environment-name"] = environmentName
				}
			}, cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<EmailTemplateContentResponse>(result);
	}
}
