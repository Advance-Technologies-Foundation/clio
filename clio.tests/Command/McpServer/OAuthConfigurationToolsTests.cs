using System;
using System.Linq;
using Clio.Command.IdentityServiceDeployment;
using Clio.Command.McpServer.Tools;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class OAuthConfigurationToolsTests
{
	private static string GetToolName<TTool>(string methodName) =>
		((McpServerToolAttribute)typeof(TTool)
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single())
		.Name;

	[Test]
	[Category("Unit")]
	[Description("get-identity-service-config advertises its stable MCP tool name and a success envelope from the resolved command.")]
	public void GetIdentityServiceConfig_ShouldAdvertiseNameAndReturnSuccess_WhenCommandResolves() {
		// Arrange
		GetToolName<GetIdentityServiceConfigTool>(nameof(GetIdentityServiceConfigTool.GetIdentityServiceConfig))
			.Should().Be(GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName,
				because: "the MCP tool name must stay centralized on the production tool type");
		GetIdentityServiceConfigResult expected =
			new("https://crm-is.creatio.com", "setting", "cid", "https://crm-is.creatio.com/connect/token",
				"https://crm-is.creatio.com/.well-known/openid-configuration", true);
		GetIdentityServiceConfigCommand command = Substitute.For<GetIdentityServiceConfigCommand>(
			Substitute.For<ISysSettingsManager>(), Substitute.For<IIdentityServerUrlResolver>(),
			Substitute.For<IIdentityServerProbe>(), new EnvironmentSettings { Uri = "http://localhost" },
			Substitute.For<ILogger>());
		command.GetConfig(Arg.Any<GetIdentityServiceConfigOptions>()).Returns(expected);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<GetIdentityServiceConfigCommand>(Arg.Any<GetIdentityServiceConfigOptions>()).Returns(command);
		GetIdentityServiceConfigTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		GetIdentityServiceConfigResponse response = tool.GetIdentityServiceConfig(new GetIdentityServiceConfigArgs("dev"));

		// Assert
		response.Success.Should().BeTrue(because: "a resolved config is a success envelope");
		response.Config.Should().Be(expected, because: "the tool surfaces the command's structured config");
	}

	[Test]
	[Category("Unit")]
	[Description("get-identity-service-config wraps a resolution failure in a structured error envelope.")]
	public void GetIdentityServiceConfig_ShouldReturnErrorEnvelope_WhenResolutionFails() {
		// Arrange
		GetIdentityServiceConfigCommand command = Substitute.For<GetIdentityServiceConfigCommand>(
			Substitute.For<ISysSettingsManager>(), Substitute.For<IIdentityServerUrlResolver>(),
			Substitute.For<IIdentityServerProbe>(), new EnvironmentSettings { Uri = "http://localhost" },
			Substitute.For<ILogger>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<GetIdentityServiceConfigCommand>(Arg.Any<GetIdentityServiceConfigOptions>())
			.Returns(_ => throw new InvalidOperationException("env missing"));
		GetIdentityServiceConfigTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		GetIdentityServiceConfigResponse response = tool.GetIdentityServiceConfig(new GetIdentityServiceConfigArgs("missing"));

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must be a structured error, not an exception");
		response.Error.Should().Contain("env missing", because: "the error envelope carries the failure message");
	}

	[Test]
	[Category("Unit")]
	[Description("resolve-oauth-system-user advertises its stable name and returns a success envelope with the resolved user.")]
	public void ResolveOAuthSystemUser_ShouldAdvertiseNameAndReturnSuccess_WhenCommandResolves() {
		// Arrange
		GetToolName<ResolveOAuthSystemUserTool>(nameof(ResolveOAuthSystemUserTool.ResolveOAuthSystemUser))
			.Should().Be(ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName,
				because: "the MCP tool name must stay centralized on the production tool type");
		ResolveOAuthSystemUserResult expected = new("11111111-1111-1111-1111-111111111111", "Supervisor", true);
		ResolveOAuthSystemUserCommand command = Substitute.For<ResolveOAuthSystemUserCommand>(
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		command.ResolveSystemUser(Arg.Any<ResolveOAuthSystemUserOptions>()).Returns(expected);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ResolveOAuthSystemUserCommand>(Arg.Any<ResolveOAuthSystemUserOptions>()).Returns(command);
		ResolveOAuthSystemUserTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		ResolveOAuthSystemUserResponse response = tool.ResolveOAuthSystemUser(new ResolveOAuthSystemUserArgs("dev"));

		// Assert
		response.Success.Should().BeTrue(because: "a resolved user is a success envelope");
		response.User.Should().Be(expected, because: "the tool surfaces the command's resolved user");
	}

	[Test]
	[Category("Unit")]
	[Description("create-oauth-technical-user advertises destructive metadata and surfaces the deferred role-grant result.")]
	public void CreateOAuthTechnicalUser_ShouldBeDestructiveAndReturnDeferredRoleGrant_WhenCommandResolves() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateOAuthTechnicalUserTool)
			.GetMethod(nameof(CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUser))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();
		attribute.Destructive.Should().BeTrue(because: "creating a technical user mutates Creatio");
		CreateOAuthTechnicalUserResult expected =
			new("33333333-3333-3333-3333-333333333333", "svc", false, "Role grant deferred: ...");
		CreateOAuthTechnicalUserCommand command = Substitute.For<CreateOAuthTechnicalUserCommand>(
			Substitute.For<IIdentityServiceCreatioClient>(), Substitute.For<ILogger>());
		command.CreateTechnicalUser(Arg.Any<CreateOAuthTechnicalUserOptions>()).Returns(expected);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CreateOAuthTechnicalUserCommand>(Arg.Any<CreateOAuthTechnicalUserOptions>()).Returns(command);
		CreateOAuthTechnicalUserTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		CreateOAuthTechnicalUserResponse response = tool.CreateOAuthTechnicalUser(new CreateOAuthTechnicalUserArgs("dev"));

		// Assert
		response.Success.Should().BeTrue(because: "a created user is a success envelope");
		response.User!.RoleGranted.Should().BeFalse(because: "the REST-only path never grants a role");
	}

	[Test]
	[Category("Unit")]
	[Description("create-server-to-server-oauth-app advertises destructive metadata and returns the client credentials in the structured envelope.")]
	public void CreateServerToServerOAuthApp_ShouldBeDestructiveAndReturnCredentials_WhenCommandResolves() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateServerToServerOAuthAppTool)
			.GetMethod(nameof(CreateServerToServerOAuthAppTool.CreateServerToServerOAuthApp))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();
		attribute.Destructive.Should().BeTrue(because: "creating an OAuth app mutates Creatio");
		CreateServerToServerOAuthAppResult expected =
			new("client-id", "client-secret", "44444444-4444-4444-4444-444444444444", "client_credentials");
		CreateServerToServerOAuthAppCommand command = Substitute.For<CreateServerToServerOAuthAppCommand>(
			Substitute.For<IIdentityServiceCreatioClient>(),
			Substitute.For<ResolveOAuthSystemUserCommand>(
				Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>()),
			Substitute.For<ILogger>());
		command.CreateApp(Arg.Any<CreateServerToServerOAuthAppOptions>()).Returns(expected);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CreateServerToServerOAuthAppCommand>(Arg.Any<CreateServerToServerOAuthAppOptions>()).Returns(command);
		CreateServerToServerOAuthAppTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		CreateServerToServerOAuthAppResponse response =
			tool.CreateServerToServerOAuthApp(new CreateServerToServerOAuthAppArgs("dev", SystemUserId: "44444444-4444-4444-4444-444444444444"));

		// Assert
		response.Success.Should().BeTrue(because: "a created app is a success envelope");
		response.App!.ClientSecret.Should().Be("client-secret",
			because: "the secret is surfaced only in the structured tool result");
	}

	[Test]
	[Category("Unit")]
	[Description("verify-oauth-app advertises its stable name and returns a success envelope with the verification result.")]
	public void VerifyOAuthApp_ShouldAdvertiseNameAndReturnSuccess_WhenCommandResolves() {
		// Arrange
		GetToolName<VerifyOAuthAppTool>(nameof(VerifyOAuthAppTool.VerifyOAuthApp))
			.Should().Be(VerifyOAuthAppTool.VerifyOAuthAppToolName,
				because: "the MCP tool name must stay centralized on the production tool type");
		VerifyOAuthAppResult expected = new(true, 200, true, "https://crm-is.creatio.com");
		VerifyOAuthAppCommand command = Substitute.For<VerifyOAuthAppCommand>(
			Substitute.For<ISysSettingsManager>(), Substitute.For<IIdentityServerUrlResolver>(),
			Substitute.For<IIdentityServerProbe>(), Substitute.For<IServiceUrlBuilder>(),
			new EnvironmentSettings { Uri = "http://localhost" }, Substitute.For<ILogger>());
		command.Verify(Arg.Any<VerifyOAuthAppOptions>()).Returns(expected);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<VerifyOAuthAppCommand>(Arg.Any<VerifyOAuthAppOptions>()).Returns(command);
		VerifyOAuthAppTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		VerifyOAuthAppResponse response = tool.VerifyOAuthApp(new VerifyOAuthAppArgs("dev", "cid", "secret"));

		// Assert
		response.Success.Should().BeTrue(because: "a completed verification is a success envelope");
		response.Result.Should().Be(expected, because: "the tool surfaces the command's verification result");
	}
}
