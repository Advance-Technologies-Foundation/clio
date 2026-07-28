using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class CreateAppCommandTests : BaseCommandTests<CreateAppOptions>
{
	private CreateAppCommand _command;
	private IApplicationCreateService _service;
	private ILogger _logger;

	public override void Setup()
	{
		base.Setup();
		_command = Container.GetRequiredService<CreateAppCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IApplicationCreateService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _service);
		containerBuilder.AddTransient(_ => _logger);
	}

	[TearDown]
	public void ClearReceivedCalls()
	{
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Returns success and logs the created application identity when the service call succeeds.")]
	public void Execute_Should_Return_Success_And_Log_Result_When_Service_Succeeds()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2",
			IconBackground = "#1F5F8B"
		};
		ApplicationInfoResult result = new(
			"pkg-uid",
			"UsrMyApp",
			[],
			ApplicationId: "app-id",
			ApplicationName: "My App",
			ApplicationCode: "UsrMyApp",
			ApplicationVersion: "1.0.0.0");
		_service.CreateApplication("dev", Arg.Any<ApplicationCreateRequest>()).Returns(result);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "successful creation should return the standard success exit code");
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r =>
				r.Name == "My App" &&
				r.Code == "UsrMyApp" &&
				r.TemplateCode == "AppFreedomUIv2" &&
				r.IconBackground == "#1F5F8B"));
		_logger.Received(1).WriteInfo(Arg.Is<string>(m => m.Contains("My App") && m.Contains("UsrMyApp")));
	}

	[Test]
	[Description("Passes with-mobile-pages=true to the service by default so existing calls keep generating the full page set.")]
	public void Execute_Should_Pass_WithMobilePages_True_By_Default()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2"
		};
		_service.CreateApplication("dev", Arg.Any<ApplicationCreateRequest>())
			.Returns(new ApplicationInfoResult("pkg-uid", "UsrMyApp", []));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a default create call should succeed");
		// with-mobile-pages defaults to true to preserve the existing five-page behavior
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r => r.WithMobilePages));
	}

	[Test]
	[Description("Passes with-mobile-pages=false to the service when the flag is explicitly set to false.")]
	public void Execute_Should_Pass_WithMobilePages_False_When_Flag_Is_Disabled()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2",
			WithMobilePagesValue = "false"
		};
		_service.CreateApplication("dev", Arg.Any<ApplicationCreateRequest>())
			.Returns(new ApplicationInfoResult("pkg-uid", "UsrMyApp", []));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a web-only create call should succeed");
		// --with-mobile-pages false must flow through to the create request so mobile pages are skipped
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r => !r.WithMobilePages));
	}

	[Test]
	[Description("Returns failure exit code and logs an error when with-mobile-pages receives an unsupported value.")]
	public void Execute_Should_Return_Failure_When_WithMobilePages_Value_Is_Invalid()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2",
			WithMobilePagesValue = "maybe"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an invalid with-mobile-pages value should yield a non-zero exit code");
		_service.DidNotReceiveWithAnyArgs().CreateApplication(default(string)!, default!);
		_service.DidNotReceiveWithAnyArgs().CreateApplication(default(EnvironmentSettings)!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("with-mobile-pages")));
	}

	[Test]
	[Description("Returns failure exit code and logs the error message when environment is missing.")]
	public void Execute_Should_Return_Failure_When_Environment_Is_Missing()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = string.Empty,
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "missing environment should yield a non-zero exit code");
		_service.DidNotReceiveWithAnyArgs().CreateApplication(default(string)!, default!);
		_service.DidNotReceiveWithAnyArgs().CreateApplication(default(EnvironmentSettings)!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Environment")));
	}

	[Test]
	[Description("Returns failure exit code and logs the exception message when the service throws.")]
	public void Execute_Should_Return_Failure_When_Service_Throws()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2"
		};
		_service.CreateApplication(Arg.Any<string>(), Arg.Any<ApplicationCreateRequest>())
			.Throws(new InvalidOperationException("Service unavailable"));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a service exception should yield a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Service unavailable")));
	}
}
