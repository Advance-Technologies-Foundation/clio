using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class BindingDbToolsTests
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
	public void CreateBinding_WithValidArgs_CallsMcpBackend()
	{
		var tool = new BindingCreateDbTool(_mockFactory, _mockLogger);
		var args = new BindingCreateDbArgs
		{
			SchemaName = "UsrTestEntity",
			PackageUId = "12345678-1234-1234-1234-123456789012",
			RowsJson = "[{\"UsrName\":\"Test\"}]",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Binding created successfully" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("binding.create", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.CreateBinding(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("binding.create", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void GetColumns_WithValidArgs_CallsMcpBackend()
	{
		var tool = new BindingGetColumnsDbTool(_mockFactory, _mockLogger);
		var args = new BindingGetColumnsDbArgs
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
				new() { Type = "text", Text = "[\"Id\",\"Name\",\"CreatedOn\"]" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("binding.get_columns", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.GetColumns(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("binding.get_columns", Arg.Any<Dictionary<string, object>>());
	}
}
