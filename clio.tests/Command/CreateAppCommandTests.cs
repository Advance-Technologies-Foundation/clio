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
	[Description("Leaves the optional template data unset when neither --entity-schema-name nor --app-section-description is supplied.")]
	public void Execute_Should_Not_Send_OptionalTemplateData_When_Template_Options_Are_Absent()
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
		exitCode.Should().Be(0, because: "a create call without the new template options should still succeed");
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r => r.OptionalTemplateData == null));
	}

	[Test]
	[Description("Sends entitySchemaName together with useExistingEntitySchema=true when --entity-schema-name is supplied.")]
	public void Execute_Should_Imply_UseExistingEntitySchema_When_EntitySchemaName_Is_Supplied()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2",
			EntitySchemaName = "UsrExistingEntity"
		};
		_service.CreateApplication("dev", Arg.Any<ApplicationCreateRequest>())
			.Returns(new ApplicationInfoResult("pkg-uid", "UsrMyApp", []));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "binding the primary section to an existing entity is a supported create call");
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r =>
				r.OptionalTemplateData != null &&
				r.OptionalTemplateData.EntitySchemaName == "UsrExistingEntity" &&
				r.OptionalTemplateData.UseExistingEntitySchema == true &&
				r.OptionalTemplateData.AppSectionDescription == null));
	}

	[Test]
	[Description("Sends only the section description, without implying useExistingEntitySchema, when --app-section-description is supplied alone.")]
	public void Execute_Should_Send_Only_SectionDescription_When_EntitySchemaName_Is_Absent()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2",
			AppSectionDescription = "Orders of the current account"
		};
		_service.CreateApplication("dev", Arg.Any<ApplicationCreateRequest>())
			.Returns(new ApplicationInfoResult("pkg-uid", "UsrMyApp", []));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a section description alone is a valid create call");
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r =>
				r.OptionalTemplateData != null &&
				r.OptionalTemplateData.AppSectionDescription == "Orders of the current account" &&
				r.OptionalTemplateData.EntitySchemaName == null &&
				r.OptionalTemplateData.UseExistingEntitySchema == null));
	}

	[Test]
	[Description("Sends the entity schema name, the implied useExistingEntitySchema flag and the section description together when both template options are supplied.")]
	public void Execute_Should_Send_Both_Template_Options_When_Both_Are_Supplied()
	{
		// Arrange
		CreateAppOptions options = new() {
			Environment = "dev",
			Name = "My App",
			Code = "UsrMyApp",
			TemplateCode = "AppFreedomUIv2",
			EntitySchemaName = "UsrExistingEntity",
			AppSectionDescription = "Reuses the existing entity"
		};
		_service.CreateApplication("dev", Arg.Any<ApplicationCreateRequest>())
			.Returns(new ApplicationInfoResult("pkg-uid", "UsrMyApp", []));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "combining an existing entity with a section description is a valid create call");
		_service.Received(1).CreateApplication(
			"dev",
			Arg.Is<ApplicationCreateRequest>(r =>
				r.OptionalTemplateData != null &&
				r.OptionalTemplateData.EntitySchemaName == "UsrExistingEntity" &&
				r.OptionalTemplateData.UseExistingEntitySchema == true &&
				r.OptionalTemplateData.AppSectionDescription == "Reuses the existing entity"));
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
