using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class PageDbToolsTests
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
	public void GetPage_WithValidArgs_CallsMcpBackend()
	{
		var tool = new PageGetDbTool(_mockFactory, _mockLogger);
		var args = new PageGetDbArgs
		{
			PageName = "AccountPageV2",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "{\"name\":\"AccountPageV2\",\"schema\":{}}" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("page.get", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.GetPage(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("page.get", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void UpdatePage_WithValidArgs_CallsMcpBackend()
	{
		var tool = new PageUpdateDbTool(_mockFactory, _mockLogger);
		var args = new PageUpdateDbArgs
		{
			PageName = "AccountPageV2",
			PackageUId = "12345678-1234-1234-1234-123456789012",
			SchemaJson = "{\"views\":[]}",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Page updated successfully" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("page.update", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.UpdatePage(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("page.update", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void ListPages_WithValidArgs_CallsMcpBackend()
	{
		var tool = new PageListDbTool(_mockFactory, _mockLogger);
		var args = new PageListDbArgs
		{
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "[{\"name\":\"AccountPageV2\"},{\"name\":\"ContactPageV2\"}]" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("page.list", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.ListPages(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("page.list", Arg.Any<Dictionary<string, object>>());
	}
}
