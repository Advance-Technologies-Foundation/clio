using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class EmailTemplateToolTests {
	private static readonly Guid EmailId = Guid.Parse("04c470db-65b6-4316-ae32-97a3e7286c94");
	private static readonly Guid LanguageId = Guid.Parse("a5420246-0a8e-e111-84a3-00155d054c03");

	[Test]
	[Category("Unit")]
	[Description("Advertises stable read-only and destructive MCP contracts for email content.")]
	public void Tools_ShouldAdvertiseSafetyAttributes_WhenReflected() {
		// Arrange
		McpServerToolAttribute getAttribute = Attribute<EmailTemplateTool>(nameof(EmailTemplateTool.Get));
		McpServerToolAttribute updateAttribute = Attribute<EmailTemplateTool>(nameof(EmailTemplateTool.Update));

		// Act and Assert
		getAttribute.Name.Should().Be(EmailTemplateTool.GetToolName,
			because: "agents need a stable read tool name");
		getAttribute.ReadOnly.Should().BeTrue(because: "reading template content does not mutate Creatio");
		getAttribute.Destructive.Should().BeFalse(because: "reading template content is safe");
		updateAttribute.Name.Should().Be(EmailTemplateTool.UpdateToolName,
			because: "agents need a stable update tool name");
		updateAttribute.Destructive.Should().BeTrue(because: "updating template content overwrites remote state");
	}

	[Test]
	[Category("Unit")]
	[Description("Publishes complete curated contracts for reading and updating email content through the lazy MCP surface.")]
	public void ToolContractGet_ShouldDescribeEmailTools_WhenExplicitlyRequested() {
		// Arrange
		ToolContractGetTool contractTool = new();

		// Act
		ToolContractGetResponse response = contractTool.GetToolContracts(new ToolContractGetArgs([
			EmailTemplateTool.GetToolName, EmailTemplateTool.UpdateToolName
		]));

		// Assert
		response.Success.Should().BeTrue(because: "both email operations have curated lazy-surface contracts");
		response.Tools.Should().HaveCount(2, because: "the read and update contracts were requested together");
		ToolContractDefinition get = response.Tools!.Single(tool => tool.Name == EmailTemplateTool.GetToolName);
		get.InputSchema.Required.Should().BeEquivalentTo(["email-id", "environment-name"],
			because: "a read needs both the host identity and the target environment");
		ToolContractDefinition update = response.Tools.Single(tool => tool.Name == EmailTemplateTool.UpdateToolName);
		update.InputSchema.Required.Should().Contain(
			["email-id", "environment-name", "format", "expected-checksum", "confirm"],
			because: "the destructive contract must advertise every write guard");
		update.PreferredFlow.Tools.Should().Equal(
			[EmailTemplateTool.GetToolName, EmailTemplateTool.UpdateToolName],
			because: "the contract must force a fresh read before an optimistic update");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns Beefree and legacy BulkEmail content with independent optimistic checksums.")]
	public void Get_ShouldReturnAllFormats_WhenBulkEmailHasBeefreeContent() {
		// Arrange
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(new {
				Id = EmailId, Name = "Invitation", TemplateSubject = "Legacy subject",
				TemplateBody = "<p>Legacy</p>", TemplateConfig = "{legacy:true}"
			}),
			var value when value.Contains("odata/EmailTemplate?") => Rows(),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(new {
				Id = Guid.Parse("bfd4d81c-e113-434b-85d0-7181ff6974b3"), EmailId,
				Language = "", TemplateLanguageId = (string)null, PageJson = "{beefree:true}",
				PageHtml = "<html>Beefree</html>", AmpHtml = "", TemplateVersion = 2
			}),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});

		// Act
		EmailTemplateContentResponse response = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the BulkEmail host and both content stores were returned");
		response.HostType.Should().Be("bulk-email", because: "the matching host is a marketing email");
		response.Variants.Should().HaveCount(2, because: "one Beefree and one legacy variant exist");
		response.Variants.Should().ContainSingle(variant => variant.Format == "beefree" && variant.Exists,
			because: "the current designer content must be returned without conversion");
		response.Variants.Should().ContainSingle(variant => variant.Format == "legacy" && variant.Exists,
			because: "legacy fallback content must remain addressable");
		response.Variants.Select(variant => variant.Checksum).Should().OnlyHaveUniqueItems(
			because: "each storage representation needs its own concurrency guard");
		client.Received(3).ExecuteGetRequest(Arg.Any<string>(), 30_000);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an absent default Beefree variant with a checksum that authorizes guarded creation.")]
	public void Get_ShouldReturnAbsentBeefreeChecksum_WhenHostHasNoBeefreeRow() {
		// Arrange
		(_, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(new {
				Id = EmailId, Name = "Target", TemplateSubject = "", TemplateBody = "", TemplateConfig = ""
			}),
			var value when value.Contains("odata/EmailTemplate?") => Rows(),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});

		// Act
		EmailTemplateContentResponse response = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		});

		// Assert
		EmailTemplateContentVariant absent = response.Variants.Should()
			.ContainSingle(variant => variant.Format == "beefree" && !variant.Exists,
				because: "the read must expose a guard value for creating the missing current-designer row")
			.Which;
		absent.Language.Should().BeEmpty(because: "the placeholder represents the default Beefree variant");
		absent.Checksum.Should().MatchRegex("^[0-9a-f]{64}$",
			because: "the creation guard uses the same SHA-256 representation as stored variants");
	}

	[Test]
	[Category("Unit")]
	[Description("Creates BfEmailTemplate content only when the absent checksum still matches.")]
	public void Update_ShouldCreateBeefreeRow_WhenAbsentChecksumMatches() {
		// Arrange
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(new {
				Id = EmailId, Name = "Target", TemplateSubject = "", TemplateBody = "", TemplateConfig = ""
			}),
			var value when value.Contains("odata/EmailTemplate?") => Rows(),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		EmailTemplateContentVariant absent = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		}).Variants.Single(variant => variant.Format == "beefree");
		client.ClearReceivedCalls();

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "beefree",
			ExpectedChecksum = absent.Checksum, Confirm = true,
			PageJson = "{new:true}", PageHtml = "<html>New</html>"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the target was unchanged after the guarded read");
		response.Created.Should().BeTrue(because: "no BfEmailTemplate row existed before the update");
		client.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("odata/BfEmailTemplate", StringComparison.Ordinal)),
			Arg.Is<string>(payload => payload.Contains("\"PageJson\":\"{new:true}\"")), 30_000);
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses stale content without making a remote write.")]
	public void Update_ShouldRefuseWrite_WhenChecksumIsStale() {
		// Arrange
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(new {
				Id = EmailId, Name = "Target", TemplateSubject = "", TemplateBody = "", TemplateConfig = ""
			}),
			var value when value.Contains("odata/EmailTemplate?") => Rows(),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "beefree",
			ExpectedChecksum = new string('0', 64), Confirm = true,
			PageJson = "{new:true}", PageHtml = "<html>New</html>"
		});

		// Assert
		response.Success.Should().BeFalse(because: "the supplied checksum does not describe current content");
		response.Error.Should().Contain("changed after it was read",
			because: "the caller needs an actionable optimistic-concurrency diagnostic");
		client.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Updates primary legacy EmailTemplate fields without replacing omitted content.")]
	public void Update_ShouldPatchLegacyMessageTemplate_WhenChecksumMatches() {
		// Arrange
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(),
			var value when value.Contains("odata/EmailTemplate?") => Rows(new {
				Id = EmailId, Name = "Message", Subject = "Before", Body = "<p>Keep</p>",
				TemplateConfig = "{legacy:true}", ConfigType = 1, IsHtmlBody = false
			}),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			var value when value.Contains("odata/EmailTemplateLang?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		EmailTemplateContentVariant legacy = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		}).Variants.Single(variant => variant.Format == "legacy" && string.IsNullOrEmpty(variant.LanguageId));
		client.ClearReceivedCalls();

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			ExpectedChecksum = legacy.Checksum, Confirm = true, Subject = "After"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the current legacy variant still matches the read checksum");
		response.Created.Should().BeFalse(because: "the primary EmailTemplate row already exists");
		client.Received(1).ExecutePatchRequest(
			Arg.Is<string>(url => url.EndsWith($"odata/EmailTemplate({EmailId:D})", StringComparison.Ordinal)),
			Arg.Is<string>(payload => payload == "{\"Subject\":\"After\"}"), 30_000);
		client.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Creates a missing EmailTemplateLang row using the language-specific absent checksum returned by get.")]
	public void Update_ShouldCreateLegacyTranslation_WhenRequestedLanguageIsAbsent() {
		// Arrange
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(),
			var value when value.Contains("odata/EmailTemplate?") => Rows(new {
				Id = EmailId, Name = "Message", Subject = "Primary", Body = "<p>Primary</p>",
				TemplateConfig = "{}", ConfigType = 1, IsHtmlBody = false
			}),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			var value when value.Contains("odata/EmailTemplateLang?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		EmailTemplateContentVariant absent = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", LanguageId = LanguageId.ToString("D")
		}).Variants.Single(variant => variant.Format == "legacy" && !variant.Exists);
		client.ClearReceivedCalls();

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			LanguageId = LanguageId.ToString("D"), ExpectedChecksum = absent.Checksum, Confirm = true,
			Subject = "Translated", Body = "<p>Translated</p>", IsHtmlBody = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "the requested translation remained absent after the guarded read");
		response.Created.Should().BeTrue(because: "EmailTemplateLang did not contain the requested language");
		client.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("odata/EmailTemplateLang", StringComparison.Ordinal)),
			Arg.Is<string>(payload => payload.Contains($"\"LanguageId\":\"{LanguageId:D}\"")), 30_000);
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an invalid language identifier before resolving or contacting an environment.")]
	public void Update_ShouldRejectLanguageId_WhenItIsNotGuid() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		EmailTemplateTool tool = new(new EmailTemplateContentService(resolver));

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			ExpectedChecksum = new string('0', 64), Confirm = true, LanguageId = "english", Subject = "Subject"
		});

		// Assert
		response.Success.Should().BeFalse(because: "EmailTemplateLang requires a concrete SysLanguage key");
		response.Error.Should().Be("language-id must be a GUID.",
			because: "the caller should be told which identity is malformed");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	private static (IApplicationClient Client, EmailTemplateTool Tool) BuildTool(Func<string, string> response) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), 30_000).Returns(call => response(call.Arg<string>()));
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000).Returns(string.Empty);
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000).Returns(string.Empty);
		return (client, new EmailTemplateTool(new EmailTemplateContentService(resolver)));
	}

	private static McpServerToolAttribute Attribute<T>(string methodName) =>
		(McpServerToolAttribute)typeof(T).GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();

	private static string Rows(params object[] values) =>
		JsonSerializer.Serialize(new Dictionary<string, object> { ["value"] = values });
}
