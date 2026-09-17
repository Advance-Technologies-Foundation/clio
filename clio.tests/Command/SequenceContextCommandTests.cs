using System;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class SequenceContextCommandTests : BaseCommandTests<SequenceContextOptions> {
	private IApplicationClient _client;
	private IRemoteEntitySchemaColumnManager _schemas;
	private SequenceContextCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		_schemas = Substitute.For<IRemoteEntitySchemaColumnManager>();
		services.AddSingleton(_client);
		services.AddSingleton(_schemas);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SequenceContextCommand>();
	}

	public override void TearDown() {
		_client.ClearReceivedCalls();
		_schemas.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("An empty successful schema catalog proves absence without probing absent schema metadata.")]
	public void MissingSchema_IsAbsent() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1)
			.Returns("{\"success\":true,\"rows\":[]}");
		// Act
		var result = _command.Read(new());
		// Assert
		result.Availability.Should().Be("absent", because: "the catalog read succeeded with no matching schema");
		result.Success.Should().BeFalse(because: "absent sequences cannot supply usable context");
		_schemas.ReceivedCalls().Should().BeEmpty(because: "discovery stops before reading absent metadata");
	}

	[TestCase("{\"success\":false,\"errorInfo\":{\"message\":\"password=secret\"}}")]
	[TestCase("<html>login</html>")]
	[Description("A failed catalog or non-JSON response is unknown rather than absent.")]
	public void FailedCatalog_IsUnknown(string response) {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1).Returns(response);
		// Act
		var result = _command.Read(new());
		// Assert
		result.Availability.Should().Be("unknown", because: "a failed read cannot establish absence");
		JsonSerializer.Serialize(result).Should().NotContain("password=secret", because: "raw server errors are not returned");
	}

	[Test]
	[Description("Empty sequence IDs fail validation before any remote operation.")]
	public void EmptyId_IsRejectedBeforeRead() {
		// Arrange
		SequenceContextOptions options = new() { SequenceId = Guid.Empty };
		// Act
		Action act = () => _command.Read(options);
		// Assert
		act.Should().Throw<ArgumentException>(because: "an empty ID is not an existing definition");
		_client.ReceivedCalls().Should().BeEmpty(because: "invalid inputs must not reach Creatio");
	}

	[Test]
	[Description("Oversized DataService responses fail with a bounded result instead of exposing the body.")]
	public void OversizedCatalog_IsBounded() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1)
			.Returns(new string('x', SequenceContextReader.ResponseByteLimit + 1));
		// Act
		var result = _command.Read(new());
		// Assert
		result.Success.Should().BeFalse(because: "an oversized response is unusable");
		JsonSerializer.Serialize(result).Length.Should().BeLessThan(2000, because: "failure output must stay bounded");
	}
	[Test]
	[Description("Happy-path discovery returns live lookup IDs and merged metadata without reading operational contacts.")]
	public void Discovery_ReturnsLiveMetadataAndIds() {
		// Arrange
		Guid liveId = Guid.NewGuid();
		_schemas.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(call =>
			JsonSerializer.Deserialize<EntitySchemaPropertiesInfo>(
				"{\"name\":\"" + call.Arg<GetEntitySchemaPropertiesOptions>().SchemaName + "\",\"columns\":[{\"name\":\"Id\",\"type\":\"Guid\",\"source\":\"inherited\"},{\"name\":\"UsrCustom\",\"type\":\"Text\",\"source\":\"own\"}]}"));
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1).Returns(call => {
			using var query = JsonDocument.Parse(call.ArgAt<string>(1));
			string schema = query.RootElement.GetProperty("rootSchemaName").GetString();
			return schema == "SysSchema"
				? "{\"success\":true,\"rows\":[{\"Id\":\"" + liveId + "\"}]}"
				: "{\"success\":true,\"rows\":[{\"Id\":\"" + liveId + "\",\"Name\":\"Live choice\"}]}";
		});
		// Act
		var result = _command.Read(new());
		// Assert
		result.Success.Should().BeTrue(because: "all requested read sections completed");
		JsonSerializer.Serialize(result.Sections["choices:SequenceStatus"].Data).Should().Contain(liveId.ToString(),
			because: "lookup identifiers must come from this environment");
		_schemas.ReceivedCalls().Select(call => (GetEntitySchemaPropertiesOptions)call.GetArguments()[0])
			.Should().OnlyContain(options => string.IsNullOrEmpty(options.Package) && options.RuntimeReadTimeoutMilliseconds == 10000,
				because: "discovery must include all package layers with a finite metadata request timeout");
		_client.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "ExecutePostRequest")
			.Select(call => (string)call.GetArguments()[1]).Should().NotContain(json => json.Contains("\"rootSchemaName\":\"Contact\""),
				because: "context discovery must not enumerate operational contacts");
	}

	[Test]
	[Description("A successful response with omitted requested columns is refused as incompatible.")]
	public void OmittedFields_AreNotSuccessfulEmptyContext() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1)
			.Returns("{\"success\":true,\"rows\":[{}]}");
		// Act
		var result = _command.Read(new());
		// Assert
		result.Availability.Should().Be("unknown", because: "missing requested fields make the catalog untrustworthy");
		result.Success.Should().BeFalse(because: "an incompatible schema response cannot establish usable context");
	}
	[Test]
	[Description("A large transport exception cannot bypass the aggregate response bound or the untrusted-text fence.")]
	public void LargeException_IsBoundedAndFenced() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1)
			.Returns(_ => throw new InvalidOperationException(new string('x', 300000)));
		// Act
		var result = _command.Read(new());
		// Assert
		JsonSerializer.Serialize(result).Length.Should().BeLessThan(2000, because: "errors must obey output bounds too");
		result.Sections["schema-presence"].Error.Should().Contain("untrusted-source-text", because: "server exception prose is untrusted");
	}

	[Test]
	[Description("An existing sequence with a hidden referenced ruleset cannot report complete prerequisites.")]
	public void HiddenRuleset_IsMissing() {
		// Arrange
		Guid reference = Guid.NewGuid();
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1).Returns(call => {
			using var query = JsonDocument.Parse(call.ArgAt<string>(1));
			return query.RootElement.GetProperty("rootSchemaName").GetString() switch {
				"SysSchema" => "{\"success\":true,\"rows\":[{\"Id\":\"" + reference + "\"}]}",
				"Sequence" => "{\"success\":true,\"rows\":[{\"Id\":\"" + reference + "\",\"Name\":\"Lab\",\"Status\":null,\"Ruleset\":{\"value\":\"" + reference + "\"},\"DeliverySchedule\":null}]}",
				_ => "{\"success\":true,\"rows\":[]}"
			};
		});
		// Act
		var result = _command.Read(new() { SequenceId = reference });
		// Assert
		result.Sections["selected-ruleset"].State.Should().Be("missing", because: "an invisible referenced ruleset cannot be inspected");
		result.Success.Should().BeFalse(because: "prerequisite inspection is incomplete");
		result.Sections["schema:Sequence"].State.Should().Be("failed", because: "missing effective metadata cannot become an empty success");
	}

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(true, true)]
	[Description("Definition inspection follows scalar or expanded lookup IDs and distinguishes an invisible schedule from usable configuration.")]
	public void DefinitionInspection_ResolvesReferences(bool expandedLookup, bool hiddenSchedule) {
		// Arrange
		Guid id = Guid.NewGuid();
		_schemas.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			JsonSerializer.Deserialize<EntitySchemaPropertiesInfo>("{\"columns\":[{\"name\":\"Id\",\"type\":\"Guid\"}]}"));
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1).Returns(call => {
			using var query = JsonDocument.Parse(call.ArgAt<string>(1));
			string schema = query.RootElement.GetProperty("rootSchemaName").GetString();
			if (hiddenSchedule && schema == "DeliverySchedule") {
				return "{\"success\":true,\"rows\":[]}";
			}
			var row = query.RootElement.GetProperty("columns").GetProperty("items").EnumerateObject()
				.ToDictionary(column => column.Name, column => (object)id.ToString());
			if (schema == "Sequence" && expandedLookup) {
				row["Ruleset"] = new { value = id };
				row["DeliverySchedule"] = new { value = id };
			}
			return JsonSerializer.Serialize(new { success = true, rows = new[] { row } });
		});
		// Act
		var result = _command.Read(new() { SequenceId = id });
		// Assert
		result.Success.Should().Be(!hiddenSchedule, because: "success requires visible prerequisites");
		result.Sections["selected-schedule"].State.Should().Be(hiddenSchedule ? "missing" : "complete",
			because: "both supported lookup shapes must lead to an explicit schedule read");
		var queries = _client.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "ExecutePostRequest")
			.Select(call => (string)call.GetArguments()[1]).ToArray();
		queries.Single(json => json.Contains("\"rootSchemaName\":\"SequenceStep\""))
			.Should().Contain("\"orderDirection\":1", because: "steps must be requested in sequence order");
	}
}
