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
	private static readonly Guid BeefreeRecordId = Guid.Parse("5e1f0c58-9d1f-4a4b-9a2c-0b3f6d5e7a10");
	private static readonly Guid TranslationRecordId = Guid.Parse("c7a1d3b2-4e56-4f78-9a0b-1c2d3e4f5a6b");

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
		// Arrange - the stub persists the POST, so the receipt is checked against a real round trip.
		bool rowExists = false;
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(new {
				Id = EmailId, Name = "Target", TemplateSubject = "", TemplateBody = "", TemplateConfig = ""
			}),
			var value when value.Contains("odata/EmailTemplate?") => Rows(),
			var value when value.Contains("odata/BfEmailTemplate?") => rowExists
				? Rows(new {
					Id = BeefreeRecordId, EmailId, Language = "", PageJson = "{new:true}",
					PageHtml = "<html>New</html>", AmpHtml = "", TemplateVersion = 0, IsDefault = true
				})
				: Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000)
			.Returns(_ => { rowExists = true; return string.Empty; });
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
		response.Checksum.Should().Be(
			tool.Get(new EmailTemplateGetArgs { EmailId = EmailId.ToString("D"), EnvironmentName = "dev" })
				.Variants.Single(variant => variant.Format == "beefree").Checksum,
			because: "the create receipt must describe the row Creatio stored, not the request values");
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
		// Arrange - the stub persists the POST and answers with Creatio's own defaults for the columns the
		// caller never supplied: non-nullable text as "" and non-nullable boolean as false, not null.
		bool rowExists = false;
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(),
			var value when value.Contains("odata/EmailTemplate?") => Rows(new {
				Id = EmailId, Name = "Message", Subject = "Primary", Body = "<p>Primary</p>",
				TemplateConfig = "{}", ConfigType = 1, IsHtmlBody = false
			}),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			var value when value.Contains("odata/EmailTemplateLang?") => rowExists
				? Rows(new {
					Id = TranslationRecordId, EmailTemplateId = EmailId, LanguageId,
					Subject = "Translated", Body = "<p>Translated</p>", TemplateConfig = "",
					IsHtmlBody = true
				})
				: Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000)
			.Returns(_ => { rowExists = true; return string.Empty; });
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

	[Test]
	[Category("Unit")]
	[Description("Returns a checksum a re-read reproduces after a primary legacy write, so the next guarded update is not rejected as a concurrent change.")]
	public void Update_ShouldReturnChecksumMatchingAReRead_WhenLegacyPrimaryVariantIsWritten() {
		// Arrange — the host answers with the values the write applies, which is what a re-read would see.
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(),
			var value when value.Contains("odata/EmailTemplate?") => Rows(new {
				Id = EmailId, Name = "Message", Subject = "Updated", Body = "<p>Primary</p>",
				TemplateConfig = "{}", ConfigType = 1, IsHtmlBody = false
			}),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			var value when value.Contains("odata/EmailTemplateLang?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		EmailTemplateContentVariant before = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		}).Variants.Single(variant => variant.Format == "legacy");

		// Act
		EmailTemplateUpdateResponse update = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			ExpectedChecksum = before.Checksum, Confirm = true, Subject = "Updated"
		});
		EmailTemplateContentVariant after = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		}).Variants.Single(variant => variant.Format == "legacy");

		// Assert
		update.Success.Should().BeTrue(because: "the guarded write applies when the expected checksum still holds");
		update.Checksum.Should().Be(after.Checksum,
			because: "the receipt must describe what Creatio hands back - an omitted language-id and the read's null one are the same primary variant, so both sides must hash the same normalized slot");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a checksum a re-read reproduces after a legacy translation is created, so the chained guarded update is accepted instead of refused as a concurrent change.")]
	public void Update_ShouldReturnChecksumMatchingAReRead_WhenLegacyTranslationIsCreated() {
		// Arrange - only the body is supplied. Creatio stores the columns the caller omitted at their
		// platform defaults ("" for non-nullable text, false for non-nullable boolean), so a receipt hashed
		// from the request values would carry null in those slots and no read could reproduce it.
		bool rowExists = false;
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(),
			var value when value.Contains("odata/EmailTemplate?") => Rows(new {
				Id = EmailId, Name = "Message", Subject = "Primary", Body = "<p>Primary</p>",
				TemplateConfig = "{}", ConfigType = 1, IsHtmlBody = false
			}),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			var value when value.Contains("odata/EmailTemplateLang?") => rowExists
				? Rows(new {
					Id = TranslationRecordId, EmailTemplateId = EmailId, LanguageId, Subject = "",
					Body = "<p>new</p>", TemplateConfig = "", IsHtmlBody = false
				})
				: Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000)
			.Returns(_ => { rowExists = true; return string.Empty; });
		EmailTemplateContentVariant absent = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", LanguageId = LanguageId.ToString("D")
		}).Variants.Single(variant => variant.Format == "legacy" && !variant.Exists);

		// Act
		EmailTemplateUpdateResponse create = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			LanguageId = LanguageId.ToString("D"), ExpectedChecksum = absent.Checksum, Confirm = true,
			Body = "<p>new</p>"
		});
		EmailTemplateContentVariant after = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", LanguageId = LanguageId.ToString("D")
		}).Variants.Single(variant => variant.Format == "legacy" && variant.Exists
			&& !string.IsNullOrEmpty(variant.LanguageId));
		EmailTemplateUpdateResponse chained = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			LanguageId = LanguageId.ToString("D"), ExpectedChecksum = create.Checksum, Confirm = true,
			Body = "<p>newer</p>"
		});

		// Assert
		create.Success.Should().BeTrue(because: "the requested translation was still absent when the guard ran");
		create.Created.Should().BeTrue(because: "no EmailTemplateLang row carried the requested language");
		create.Checksum.Should().Be(after.Checksum,
			because: "the create receipt must describe the content subsequently read from Creatio, or the very next guarded update is refused");
		chained.Success.Should().BeTrue(
			because: "get-source then update-target on a host with no translation yet must be able to keep editing the row it just created");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a legacy write that carries only fields a bulk-email host cannot store, instead of dropping them and reporting success.")]
	public void Update_ShouldRejectLegacyWrite_WhenBulkEmailHostCannotCarryTheRequestedFields() {
		// Arrange
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(new {
				Id = EmailId, Name = "Bulk", TemplateSubject = "Subject", TemplateBody = "<p>Body</p>",
				TemplateConfig = "{}"
			}),
			var value when value.Contains("odata/EmailTemplate?") => Rows(),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		EmailTemplateContentVariant before = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		}).Variants.Single(variant => variant.Format == "legacy");
		client.ClearReceivedCalls();

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "legacy",
			ExpectedChecksum = before.Checksum, Confirm = true, ConfigType = 1, IsHtmlBody = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "a BulkEmail row carries neither ConfigType nor IsHtmlBody");
		response.Error.Should().Be(
			"config-type and is-html-body are supported only for EmailTemplate message-template hosts.",
			because: "silently discarding the requested fields and returning a checksum computed from them reports a change that never happened");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves an omitted language to the existing default beefree row and edits it in place, instead of reporting it absent and creating a second default row.")]
	public void Update_ShouldEditTheDefaultBeefreeRow_WhenNoLanguageIsRequested() {
		// Arrange — the only row is the default one and its Language is not empty.
		(IApplicationClient client, EmailTemplateTool tool) = BuildTool(url => url switch {
			var value when value.Contains("odata/BulkEmail?") => Rows(),
			var value when value.Contains("odata/EmailTemplate?") => Rows(new {
				Id = EmailId, Name = "Message", Subject = "Primary", Body = "<p>Primary</p>",
				TemplateConfig = "{}", ConfigType = 1, IsHtmlBody = false
			}),
			var value when value.Contains("odata/BfEmailTemplate?") => Rows(new {
				Id = BeefreeRecordId, EmailId, Language = "en-US", PageJson = "{a:1}", PageHtml = "<h1/>",
				AmpHtml = "", TemplateVersion = 3, IsDefault = true
			}),
			var value when value.Contains("odata/EmailTemplateLang?") => Rows(),
			_ => throw new InvalidOperationException($"Unexpected URL: {url}")
		});
		EmailTemplateContentVariant beefree = tool.Get(new EmailTemplateGetArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev"
		}).Variants.Single(variant => variant.Format == "beefree");
		client.ClearReceivedCalls();

		// Act
		EmailTemplateUpdateResponse response = tool.Update(new EmailTemplateUpdateArgs {
			EmailId = EmailId.ToString("D"), EnvironmentName = "dev", Format = "beefree",
			ExpectedChecksum = beefree.Checksum, Confirm = true, PageJson = "{a:2}", PageHtml = "<h2/>"
		});

		// Assert
		beefree.Exists.Should().BeTrue(
			because: "an omitted language means the row the email sends by default, which is the IsDefault row rather than the one whose Language is empty");
		beefree.IsDefault.Should().BeTrue(because: "the read must expose the flag it would otherwise overwrite unseen");
		response.Success.Should().BeTrue(
			because: "the resolved default row still matches the checksum the read returned");
		response.Created.Should().BeFalse(because: "the default row already exists and must be edited in place");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default);
		// because: posting here would leave the email with two default beefree rows
		client.Received(1).ExecutePatchRequest(
			Arg.Any<string>(),
			Arg.Is<string>(payload => !payload.Contains("IsDefault", StringComparison.Ordinal)
				&& payload.Contains("\"Language\":\"en-US\"", StringComparison.Ordinal)),
			30_000);
		// because: IsDefault belongs to the create only - rewriting it on every update changes a column the caller never named
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
