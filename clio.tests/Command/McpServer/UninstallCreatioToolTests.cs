using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class UninstallCreatioToolTests : BaseClioModuleTests {
	private ICreatioUninstaller _uninstaller;
	private ILogger _logger;
	private List<LogMessage> _messages;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_logger = Substitute.For<ILogger>();
		_messages = [];
		_logger.LogMessages.Returns(_messages);
		_logger.When(item => item.WriteWarning(Arg.Any<string>()))
			.Do(call => _messages.Add(new WarningMessage(call.Arg<string>())));
		_logger.When(item => item.WriteInfo(Arg.Any<string>()))
			.Do(call => _messages.Add(new InfoMessage(call.Arg<string>())));
		IValidator<UninstallCreatioCommandOptions> validator =
			Substitute.For<IValidator<UninstallCreatioCommandOptions>>();
		validator.Validate(Arg.Any<UninstallCreatioCommandOptions>()).Returns(new ValidationResult());
		_uninstaller = Substitute.For<ICreatioUninstaller>();
		IStageEventProgressForwarder progressForwarder = Substitute.For<IStageEventProgressForwarder>();
		ModelContextProtocol.Server.McpServer server = Substitute.For<ModelContextProtocol.Server.McpServer>();
		services.AddSingleton(_logger);
		services.AddSingleton(validator);
		services.AddSingleton(_uninstaller);
		services.AddSingleton(progressForwarder);
		services.AddSingleton(server);
		services.AddTransient<UninstallCreatioTool>();
	}

	[Test]
	[Description("Advertises a stable destructive MCP contract that explains successful profile-cleanup warnings.")]
	public void UninstallCreatio_ShouldExposeStableWarningAwareContract_WhenToolIsDiscovered() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(UninstallCreatioTool)
			.GetMethod(nameof(UninstallCreatioTool.UninstallCreatio), [typeof(RequestContext<CallToolRequestParams>), typeof(UninstallCreatioArgs)])!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		System.ComponentModel.DescriptionAttribute description = (System.ComponentModel.DescriptionAttribute)typeof(UninstallCreatioTool)
			.GetMethod(nameof(UninstallCreatioTool.UninstallCreatio), [typeof(RequestContext<CallToolRequestParams>), typeof(UninstallCreatioArgs)])!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Single();

		// Act
		string toolName = UninstallCreatioTool.UninstallCreatioToolName;

		// Assert
		toolName.Should().Be("uninstall-creatio",
			because: "Ring and E2E callers require a stable discovery name");
		attribute.Destructive.Should().BeTrue(
			because: "uninstall removes infrastructure and must retain destructive classification");
		description.Description.Should().Contain("warning with successful tool completion",
			because: "agents must not retry an already successful destructive uninstall after a profile warning");
	}

	[Test]
	[Description("Maps the MCP environment name and preserves warning plus informational output with exit code zero.")]
	public void UninstallCreatio_ShouldPreserveSuccessfulWarningOutput_WhenProfileCleanupWarns() {
		// Arrange
		_uninstaller.When(item => item.UninstallByEnvironmentName("sandbox"))
			.Do(_ => _logger.WriteWarning("Profile remains; remove it manually."));
		UninstallCreatioTool tool = Container.GetRequiredService<UninstallCreatioTool>();

		// Act
		CommandExecutionResult result = tool.UninstallCreatio((ProgressToken?)null, new UninstallCreatioArgs("sandbox"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a best-effort profile warning must keep the destructive uninstall successful");
		_uninstaller.Received(1).UninstallByEnvironmentName("sandbox");
		result.Output.Should().Contain(message => message is WarningMessage,
			because: "MCP callers must receive the typed profile cleanup warning");
		result.Output.Should().Contain(message => message is InfoMessage,
			because: "the normal successful command completion message must be preserved");
	}
}
