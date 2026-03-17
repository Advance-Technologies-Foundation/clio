using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class EntityCrudDbToolsTests
{
	private IMcpHttpClientFactory _mockFactory;
	private IMcpHttpClient _mockClient;
	private ILogger _mockLogger;

	[SetUp]
	public void Setup()
	{
		_mockFactory = Substitute.For<IMcpHttpClientFactory>();
		_mockClient = Substitute.For<IMcpHttpClient>();
		_mockLogger = Substitute.For<ILogger>();

		_mockFactory.CreateClient(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(_mockClient);
	}

	[Test]
	public void CreateEntity_WithValidArgs_CallsMcpBackend()
	{
		var tool = new EntityCreateDbTool(_mockFactory, _mockLogger);
		var args = new EntityCreateDbArgs
		{
			PackageUId = "12345678-1234-1234-1234-123456789012",
			Name = "UsrTestEntity",
			Caption = "Test Entity",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Entity created successfully" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("entity.create", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.CreateEntity(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("entity.create", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void CreateLookup_WithValidArgs_CallsMcpBackend()
	{
		var tool = new EntityCreateLookupDbTool(_mockFactory, _mockLogger);
		var args = new EntityCreateLookupDbArgs
		{
			PackageUId = "12345678-1234-1234-1234-123456789012",
			Name = "UsrTestLookup",
			Caption = "Test Lookup",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Lookup created successfully" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("entity.create_lookup", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.CreateLookup(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("entity.create_lookup", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void UpdateEntity_WithValidArgs_CallsMcpBackend()
	{
		var tool = new EntityUpdateDbTool(_mockFactory, _mockLogger);
		var args = new EntityUpdateDbArgs
		{
			SchemaName = "UsrTestEntity",
			PackageUId = "12345678-1234-1234-1234-123456789012",
			ColumnsJson = "[{\"operation\":\"ADD\",\"name\":\"UsrNewColumn\"}]",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Entity updated successfully" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("entity.update", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.UpdateEntity(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("entity.update", Arg.Any<Dictionary<string, object>>());
	}
}
