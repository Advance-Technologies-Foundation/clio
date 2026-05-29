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
public sealed class ODataUpdateToolTests {
	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, destructive, idempotent MCP tool name for odata-update.")]
	public void Update_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataUpdateTool)
			.GetMethod(nameof(ODataUpdateTool.Update))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ODataUpdateTool.ToolName);
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeTrue(because: "an update mutates existing remote state");
		attribute.Idempotent.Should().BeTrue(because: "re-applying the same field values is idempotent");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends a PATCH to the addressed entity key with the JSON body via the shared application client.")]
	public void Update_Should_Patch_Addressed_Key_With_Body() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Id = Guid,
			Data = Obj("{\"Name\":\"New\"}"),
			Confirm = true
		});

		response.Success.Should().BeTrue();
		response.Id.Should().Be(Guid);
		urlBuilder.Received(1).Build($"odata/Contact({Guid})");
		client.Received(1).ExecutePatchRequest($"http://creatio/odata/Contact({Guid})", "{\"Name\":\"New\"}", 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing or non-GUID id without any remote call to guard against keyless mass updates.")]
	public void Update_Should_Reject_NonGuid_Id() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = "all", Data = Obj("{\"Name\":\"x\"}"), Confirm = true
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("must be a record GUID");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a destructive update when confirm is omitted, without any remote call.")]
	public void Update_Should_Refuse_Without_Confirm() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("confirm");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects empty data without any remote call.")]
	public void Update_Should_Reject_Empty_Data() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("data is required");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
