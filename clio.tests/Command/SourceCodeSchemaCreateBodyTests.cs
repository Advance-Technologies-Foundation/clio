using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class SourceCodeSchemaCreateBodyTests : BaseCommandTests<SourceCodeSchemaCreateOptions> {
	private IApplicationClient _client;
	private SourceCodeSchemaCreateCommand _command;
	private IToolCommandResolver _resolver;
	private SchemaCreateTool _tool;
	private string _savedBody;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		_resolver = Substitute.For<IToolCommandResolver>();
		services.AddSingleton(_client);
		services.AddSingleton(_resolver);
		services.AddSingleton<ILogger>(ConsoleLogger.Instance);
		services.AddSingleton(Substitute.For<ICaptionCultureResolver>());
		services.AddTransient<SchemaCreateTool>();
	}

	[SetUp]
	public void ArrangeCommand() {
		_command = Container.GetRequiredService<SourceCodeSchemaCreateCommand>();
		_tool = Container.GetRequiredService<SchemaCreateTool>();
		_resolver.Resolve<SourceCodeSchemaCreateCommand>(Arg.Any<SourceCodeSchemaCreateOptions>()).Returns(_command);
		_savedBody = null;
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			string url = call.ArgAt<string>(0);
			JObject payload = JObject.Parse(call.ArgAt<string>(1));
			if (url.EndsWith("SelectQuery", System.StringComparison.Ordinal)) {
				return payload["rootSchemaName"]?.ToString() == "SysPackage"
					? """{"success":true,"rows":[{"UId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}]}"""
					: """{"success":true,"rows":[]}""";
			}
			if (url.EndsWith("CreateNewSchema", System.StringComparison.Ordinal)) {
				return """{"success":true,"schema":{"uId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","body":" ","caption":[],"description":[]}}""";
			}
			_savedBody = payload["body"]?.ToString();
			return """{"success":true}""";
		});
	}

	[TearDown]
	public void ClearCalls() {
		_client.ClearReceivedCalls();
		_resolver.ClearReceivedCalls();
		ConsoleLogger.Instance.ClearMessages();
	}

	[TestCase(null, " ")]
	[TestCase("// Grüße\r\nnamespace Example { }\n", "// Grüße\r\nnamespace Example { }\n")]
	[Description("Saves the supplied source verbatim while preserving the platform template when body inputs are omitted.")]
	public void TryCreate_ShouldSaveExpectedBody_WhenOptionalBodyIsProvided(string body, string expected) {
		// Arrange
		SourceCodeSchemaCreateOptions options = new() { SchemaName = "UsrBody", PackageName = "Custom", Body = body };
		// Act
		bool success = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);
		// Assert
		success.Should().BeTrue(because: $"valid creation should succeed: {response.Error}");
		_savedBody.Should().Be(expected, because: "the save request must carry the exact source or unchanged template");
	}

	[TestCase("")]
	[TestCase(" \r\n\t")]
	[Description("Rejects explicitly empty source before any remote request, avoiding a partially created schema.")]
	public void TryCreate_ShouldRejectBeforeRemoteCalls_WhenBodyIsEmpty(string body) {
		// Arrange
		SourceCodeSchemaCreateOptions options = new() { SchemaName = "UsrBody", PackageName = "Custom", Body = body };
		// Act
		bool success = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);
		// Assert
		success.Should().BeFalse(because: "an explicitly supplied empty source is invalid");
		response.Error.Should().Contain("must not be empty", because: "the error must explain the invalid body");
		_client.ReceivedCalls().Should().BeEmpty(because: "invalid content must fail before any remote side effects");
	}

	[TestCase(null, null)]
	[TestCase("// Body", null)]
	[TestCase(null, "source.cs")]
	[TestCase("// Body", "source.cs")]
	[Property("Module", "McpServer")]
	[Description("Maps optional body and body-file inputs into the per-environment command options without altering them.")]
	public void CreateSchema_ShouldMapBodyInputs_WhenResolvingCommand(string body, string bodyFile) {
		// Arrange
		SourceCodeSchemaCreateOptions mapped = null;
		_resolver.Resolve<SourceCodeSchemaCreateCommand>(Arg.Do<SourceCodeSchemaCreateOptions>(value => mapped = value))
			.Returns(_ => throw new System.InvalidOperationException("Stop after mapping"));
		// Act
		_tool.CreateSchema(new SchemaCreateArgs("UsrBody", "Custom") { Body = body, BodyFile = bodyFile, EnvironmentName = "lab" });
		// Assert
		mapped.Should().NotBeNull(because: "the tool must use environment-aware resolution");
		mapped.Body.Should().Be(body, because: "inline source must reach the command unchanged");
		mapped.BodyFile.Should().Be(bodyFile, because: "the command owns source-file loading");
		mapped.Environment.Should().Be("lab", because: "the requested environment owns the schema");
	}
}
