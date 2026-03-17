using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class ApplicationCreateDbToolTests
{
	private IMcpHttpClientFactory _mockFactory;
	private IMcpHttpClient _mockClient;
	private ILogger _mockLogger;
	private ApplicationCreateDbTool _tool;

	[SetUp]
	public void Setup()
	{
		_mockFactory = Substitute.For<IMcpHttpClientFactory>();
		_mockClient = Substitute.For<IMcpHttpClient>();
		_mockLogger = Substitute.For<ILogger>();

		_mockFactory.CreateClient(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(_mockClient);

		_tool = new ApplicationCreateDbTool(_mockFactory, _mockLogger);
	}

	[Test]
	public void CreateApplication_WithValidArgs_CallsMcpBackend()
	{
		var args = new ApplicationCreateDbArgs
		{
			Name = "TestApp",
			Code = "UsrTestApp",
			TemplateCode = "AppFreedomUI",
			IconBackground = "#FF5733",
			EnvironmentName = "dev",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Application created successfully" }
			},
			IsError = false
		};

		_mockClient.CallToolAsync("application.create", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = _tool.CreateApplication(args);

		Assert.AreEqual(0, result.ExitCode);
		_mockClient.Received(1).CallToolAsync("application.create", Arg.Any<Dictionary<string, object>>());
	}

	[Test]
	public void CreateApplication_WithError_ReturnsErrorResult()
	{
		var args = new ApplicationCreateDbArgs
		{
			Name = "TestApp",
			Code = "UsrTestApp",
			TemplateCode = "AppFreedomUI",
			IconBackground = "#FF5733",
			Uri = "http://localhost:5001",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		var mcpResult = new McpToolCallResult
		{
			Content = new List<McpContent>
			{
				new() { Type = "text", Text = "Application creation failed" }
			},
			IsError = true
		};

		_mockClient.CallToolAsync("application.create", Arg.Any<Dictionary<string, object>>())
			.Returns(System.Threading.Tasks.Task.FromResult(mcpResult));

		var result = _tool.CreateApplication(args);

		Assert.AreEqual(1, result.ExitCode);
	}
}
