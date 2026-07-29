using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins the MCP trust boundary of the printable-* tools: a caught transport exception must never
/// reach the client transcript verbatim. Every sibling OData tool redacts, and a printable tool
/// that regressed to a raw <c>ex.Message</c> would leak the target URI or credential values into
/// whatever host or third-party LLM consumes the tool result.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PrintableToolErrorRedactionTests {

	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string EntitySchemaId = "1b9dc2f8-8e2c-4d67-8f4c-2a1d0f3e7b55";

	/// <summary>A transport message shaped like the ones Creatio's HTTP layer actually throws.</summary>
	private const string LeakyExceptionMessage =
		"Connection refused to https://secret-tenant.creatio.com/0/odata/SysModuleReport; password=hunter2";

	private const string RedactedUri = "[redacted-uri]";

	private IApplicationClient _client;
	private IServiceUrlBuilder _urlBuilder;
	private IToolCommandResolver _resolver;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(_client);
		_resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(_urlBuilder);
		_urlBuilder.Build(Arg.Any<string>()).Returns(call => $"https://secret-tenant.creatio.com/{call.Arg<string>()}");
	}

	[Test]
	[Description("Redacts the URI and credential value out of a transport failure surfaced by get-printable.")]
	public void GetPrintable_Should_Redact_Transport_Error() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>())
			.Throws(new InvalidOperationException(LeakyExceptionMessage));
		PrintableGetTool tool = new(_resolver);

		// Act
		ODataReadResponse response = tool.Get(new PrintableGetArgs { EnvironmentName = "dev", Id = RecordId });

		// Assert
		response.Success.Should().BeFalse(
			because: "a throwing transport call must surface as a structured failure, not an MCP protocol error");
		AssertRedacted(response.Error);
	}

	[Test]
	[Description("Redacts the URI and credential value out of a transport failure surfaced by create-printable.")]
	public void CreatePrintable_Should_Redact_Transport_Error() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Throws(new InvalidOperationException(LeakyExceptionMessage));
		PrintableCreateTool tool = new(_resolver);

		// Act
		ODataWriteResponse response = tool.Create(new PrintableCreateArgs {
			EnvironmentName = "dev", Caption = "Contact card", EntitySchemaId = EntitySchemaId
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a throwing transport call must surface as a structured failure, not an MCP protocol error");
		AssertRedacted(response.Error);
	}

	[Test]
	[Description("Redacts the URI and credential value out of a transport failure surfaced by update-printable.")]
	public void UpdatePrintable_Should_Redact_Transport_Error() {
		// Arrange
		_client
			.When(client => client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()))
			.Do(_ => throw new InvalidOperationException(LeakyExceptionMessage));
		PrintableUpdateTool tool = new(_resolver);

		// Act
		ODataWriteResponse response = tool.Update(new PrintableUpdateArgs {
			EnvironmentName = "dev", Id = RecordId, Caption = "Renamed", Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a throwing transport call must surface as a structured failure, not an MCP protocol error");
		AssertRedacted(response.Error);
	}

	[Test]
	[Description("Redacts the URI and credential value out of a transport failure surfaced by delete-printable.")]
	public void DeletePrintable_Should_Redact_Transport_Error() {
		// Arrange
		_client
			.When(client => client.ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()))
			.Do(_ => throw new InvalidOperationException(LeakyExceptionMessage));
		PrintableDeleteTool tool = new(_resolver);

		// Act
		ODataWriteResponse response = tool.Delete(new PrintableDeleteArgs {
			EnvironmentName = "dev", Id = RecordId, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a throwing transport call must surface as a structured failure, not an MCP protocol error");
		AssertRedacted(response.Error);
	}

	[Test]
	[Description("Redacts the URI and credential value out of a transport failure surfaced by upload-report-template.")]
	public void UploadReportTemplate_Should_Redact_Transport_Error() {
		// Arrange
		string templatePath = Path.Combine(Path.GetTempPath(), $"clio-printable-{Guid.NewGuid():N}.docx");
		File.WriteAllText(templatePath, "not a real docx, only the transport path is under test");
		try {
			_client.UploadAlmFileByChunk(Arg.Any<string>(), Arg.Any<string>())
				.Throws(new InvalidOperationException(LeakyExceptionMessage));
			PrintableTemplateUploadTool tool = new(_resolver);

			// Act
			PrintableTemplateUploadResponse response = tool.Upload(new PrintableTemplateUploadArgs {
				EnvironmentName = "dev", Id = RecordId, FilePath = templatePath, Confirm = true
			});

			// Assert
			response.Success.Should().BeFalse(
				because: "a throwing transport call must surface as a structured failure, not an MCP protocol error");
			AssertRedacted(response.Error);
		} finally {
			File.Delete(templatePath);
		}
	}

	[Test]
	[Description("Keeps the actionable environment-not-found reason readable after redaction so an agent can self-correct.")]
	public void GetPrintable_Should_Preserve_Environment_Name_In_Resolution_Failure() {
		// Arrange
		const string missingEnvironment = "missing-printable-env";
		IEnumerable<string> noRegisteredEnvironments = [];
		IToolCommandResolver failingResolver = Substitute.For<IToolCommandResolver>();
		failingResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>())
			.Throws(new InvalidOperationException(EnvironmentNotFoundError.Build(missingEnvironment, noRegisteredEnvironments)));
		PrintableGetTool tool = new(failingResolver);

		// Act
		ODataReadResponse response = tool.Get(new PrintableGetArgs { EnvironmentName = missingEnvironment, Id = RecordId });

		// Assert
		response.Success.Should().BeFalse(
			because: "an unresolvable environment is a caller error the tool must report, not throw");
		response.Error.Should().Contain(missingEnvironment,
			because: "redaction is surgical - the agent still needs to see which environment key failed to self-correct");
	}

	private static void AssertRedacted(string error) {
		error.Should().NotBeNullOrWhiteSpace(
			because: "the failure must carry a reason the agent can act on");
		error.Should().NotContain("secret-tenant.creatio.com",
			because: "the target host must never cross the MCP boundary into the client transcript");
		error.Should().NotContain("hunter2",
			because: "a credential value must never cross the MCP boundary into the client transcript");
		error.Should().Contain(RedactedUri,
			because: "SensitiveErrorTextRedactor replaces the URI with a stable placeholder rather than dropping the message");
	}
}
