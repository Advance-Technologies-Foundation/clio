using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class ApplicationGetDbToolsTests
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
	public void GetApplicationInfo_WithValidArgs_CallsMcpBackend()
	{
		var tool = new ApplicationGetInfoDbTool(_mockFactory, _mockLogger);
		var args = new ApplicationGetInfoDbArgs
		{
			AppCode = "TestApp",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "{\"id\":\"123\",\"name\":\"TestApp\"}" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("application.get_info", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.GetApplicationInfo(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("application.get_info", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void GetApplicationList_WithValidArgs_CallsMcpBackend()
	{
		var tool = new ApplicationGetListDbTool(_mockFactory, _mockLogger);
		var args = new ApplicationGetListDbArgs
		{
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "[{\"id\":\"123\",\"name\":\"App1\"}]" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("application.get_list", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = tool.GetApplicationList(args);

		result.ExitCode.Should().Be(0);
		_mockClient.Received(1).CallToolAsync("application.get_list", Arg.Any<Dictionary<string, object>>());
	}
}
