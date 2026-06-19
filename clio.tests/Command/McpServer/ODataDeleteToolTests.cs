using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ODataDeleteToolTests {
	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, destructive, idempotent MCP tool name for odata-delete.")]
	[Ignore("ENG-90312 Phase 2: odata-delete folded into clio-run; the per-method [McpServerTool] safety flags now live on clio-run itself, so the legacy attribute reflection no longer applies.")]
	public void Delete_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataDeleteTool)
			.GetMethod(nameof(ODataDeleteTool.Delete))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ODataDeleteTool.ToolName);
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeTrue(because: "delete removes remote state");
		attribute.Idempotent.Should().BeTrue(because: "deleting an already-deleted record yields the same end state");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends a DELETE to the addressed entity key with an empty body.")]
	public void Delete_Should_Delete_Addressed_Key() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteRunArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Confirm = true
		});

		response.Success.Should().BeTrue();
		response.Id.Should().Be(Guid);
		urlBuilder.Received(1).Build($"odata/Contact({Guid})");
		client.Received(1).ExecuteDeleteRequest($"http://creatio/odata/Contact({Guid})", string.Empty, 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a destructive delete when confirm is omitted, without any remote call.")]
	public void Delete_Should_Refuse_Without_Confirm() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteRunArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("confirm");
		client.DidNotReceive().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing or non-GUID id without any remote call to guard against keyless mass deletes.")]
	public void Delete_Should_Reject_NonGuid_Id() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteRunArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = " "
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("must be a record GUID");
		client.DidNotReceive().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a validation failure without any remote call when entity is missing.")]
	public void Delete_Should_Fail_When_Entity_Missing() {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteRunArgs {
			EnvironmentName = "dev", Entity = " ", Id = Guid
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Be("entity is required.");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}
}
