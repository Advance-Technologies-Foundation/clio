using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class EntityHelperDbToolsTests
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
	public void CheckName_WithValidArgs_CallsMcpBackend()
	{
		var tool = new EntityCheckNameDbTool(_mockFactory, _mockLogger);
		var args = new EntityCheckNameDbArgs
		{
			Name = "UsrTestEntity",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "{\"isTaken\":false}" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("entity.check_name_taken", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.CheckName(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("entity.check_name_taken", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void ListPackages_WithValidArgs_CallsMcpBackend()
	{
		var tool = new EntityListPackagesDbTool(_mockFactory, _mockLogger);
		var args = new EntityListPackagesDbArgs
		{
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "[{\"id\":\"123\",\"name\":\"CustomPackage\"}]" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("entity.list_packages", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.ListPackages(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("entity.list_packages", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void GetSchema_WithValidArgs_CallsMcpBackend()
	{
		var tool = new EntityGetSchemaDbTool(_mockFactory, _mockLogger);
		var args = new EntityGetSchemaDbArgs
		{
			SchemaName = "Contact",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "{\"name\":\"Contact\",\"columns\":[]}" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("entity.get_schema", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.GetSchema(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("entity.get_schema", Arg.Any<Dictionary<string, object>>());
	}
}
