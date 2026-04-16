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
		_service.DidNotReceiveWithAnyArgs().CreateApplication(default!, default!);
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
