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
public sealed class GetAppInfoCommandTests : BaseCommandTests<GetAppInfoOptions>
{
	private GetAppInfoCommand _command;
	private IApplicationInfoService _service;
	private ILogger _logger;

	public override void Setup()
	{
		base.Setup();
		_command = Container.GetRequiredService<GetAppInfoCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IApplicationInfoService>();
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

	private static ApplicationInfoResult BuildResult() =>
		new("pkg-uid", "UsrMyApp", [],
			ApplicationId: "app-id",
			ApplicationName: "My App",
			ApplicationCode: "UsrMyApp",
			ApplicationVersion: "1.0.0.0");

	[Test]
	[Description("Returns success and prints application summary when called with a valid code.")]
	public void Execute_Should_Return_Success_And_Print_Summary_When_Code_Provided()
	{
		// Arrange
		GetAppInfoOptions options = new() { Environment = "dev", Code = "UsrMyApp" };
		_service.GetApplicationInfo("dev", null, "UsrMyApp").Returns(BuildResult());

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a valid code should return the standard success exit code");
		_service.Received(1).GetApplicationInfo("dev", null, "UsrMyApp");
		_logger.Received(1).WriteInfo(Arg.Is<string>(m => m.Contains("My App")));
	}

	[Test]
	[Description("Returns failure exit code and logs an error when neither code nor id is supplied.")]
	public void Execute_Should_Return_Failure_When_Neither_Code_Nor_Id_Provided()
	{
		// Arrange
		GetAppInfoOptions options = new() { Environment = "dev" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "omitting both --code and --id must fail with a non-zero exit code");
		_service.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("--code") || m.Contains("--id")));
	}

	[Test]
	[Description("Returns failure exit code when environment is missing.")]
	public void Execute_Should_Return_Failure_When_Environment_Is_Missing()
	{
		// Arrange
		GetAppInfoOptions options = new() { Environment = string.Empty, Code = "UsrMyApp" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "missing environment should yield a non-zero exit code");
		_service.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
	}

	[Test]
	[Description("Outputs indented JSON when --json flag is set.")]
	public void Execute_Should_Output_Json_When_JsonFormat_Enabled()
	{
		// Arrange
		GetAppInfoOptions options = new() { Environment = "dev", Code = "UsrMyApp", JsonFormat = true };
		_service.GetApplicationInfo("dev", null, "UsrMyApp").Returns(BuildResult());

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the json format flag should not affect success");
		_logger.Received(1).WriteInfo(Arg.Is<string>(m => m.TrimStart().StartsWith("{")));
	}

	[Test]
	[Description("Returns failure exit code and logs the exception message when the service throws.")]
	public void Execute_Should_Return_Failure_When_Service_Throws()
	{
		// Arrange
		GetAppInfoOptions options = new() { Environment = "dev", Code = "UsrMyApp" };
		_service.GetApplicationInfo(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
			.Throws(new InvalidOperationException("Not found"));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a service exception must yield a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Not found")));
	}
}
