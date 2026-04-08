using System;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class ApplicationSectionUpdateServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionUpdateService _sut = null!;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_environmentSettings = new EnvironmentSettings {
			Uri = "https://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		_settingsRepository.FindEnvironment("sandbox").Returns(_environmentSettings);
		_applicationClientFactory.CreateEnvironmentClient(_environmentSettings).Returns(_applicationClient);
		_serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://example.invalid/{callInfo.ArgAt<string>(0)}");
		_sut = new ApplicationSectionUpdateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService);
	}

	[Test]
	[Description("Updates section caption as plain text and returns structured before-and-after section metadata.")]
	public void UpdateSection_Should_Update_Caption_As_Plain_Text() {
		// Arrange
		string? updateBody = null;
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp").Returns(
			new ApplicationInfoResult(
				"pkg-uid",
				"UsrOrdersApp",
				[],
				[],
				"app-id",
				"Orders App",
				"UsrOrdersApp",
				"8.3.0"));
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"Code\"", StringComparison.Ordinal)))
			.Returns(
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"{\"en-US\":\"Orders\"}","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""",
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"Caption\"", StringComparison.Ordinal)))
			.Returns(callInfo => {
				updateBody = callInfo.ArgAt<string>(1);
				return """{"success":true}""";
			});

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			"sandbox",
			new ApplicationSectionUpdateRequest(
				"UsrOrdersApp",
				"UsrOrders",
				Caption: "Orders"));

		// Assert
		result.PreviousSection.Caption.Should().Be("{\"en-US\":\"Orders\"}",
			because: "the response should expose the stored caption before the repair update");
		result.Section.Caption.Should().Be("Orders",
			because: "the response should expose the plain-text caption after the update");
		updateBody.Should().NotBeNullOrWhiteSpace(
			because: "the section update flow should send an UpdateQuery payload");
		updateBody.Should().Contain("\"Caption\"",
			because: "the payload should update the section caption field");
		updateBody.Should().Contain("\"value\":\"Orders\"",
			because: "the updated caption must be persisted as plain text");
		updateBody.Should().NotContain("{\\u0022en-US\\u0022:\\u0022Orders\\u0022",
			because: "the update flow must not persist a JSON-string caption literal again");
	}

	[Test]
	[Description("Updates section description only and keeps other mutable fields out of the UpdateQuery payload.")]
	public void UpdateSection_Should_Update_Description_Only() {
		// Arrange
		string? updateBody = null;
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp").Returns(
			new ApplicationInfoResult(
				"pkg-uid",
				"UsrOrdersApp",
				[],
				[],
				"app-id",
				"Orders App",
				"UsrOrdersApp",
				"8.3.0"));
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"Code\"", StringComparison.Ordinal)))
			.Returns(
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Old description","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""",
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"New description","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"Description\"", StringComparison.Ordinal)))
			.Returns(callInfo => {
				updateBody = callInfo.ArgAt<string>(1);
				return """{"success":true}""";
			});

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			"sandbox",
			new ApplicationSectionUpdateRequest(
				"UsrOrdersApp",
				"UsrOrders",
				Description: "New description"));

		// Assert
		result.PreviousSection.Description.Should().Be("Old description",
			because: "the response should expose the original description");
		result.Section.Description.Should().Be("New description",
			because: "the response should expose the updated description");
		updateBody.Should().Contain("\"Description\"",
			because: "the payload should update the description field");
		updateBody.Should().NotContain("\"Caption\"",
			because: "description-only updates should leave caption untouched");
		updateBody.Should().NotContain("\"LogoId\"",
			because: "description-only updates should leave icon id untouched");
		updateBody.Should().NotContain("\"IconBackground\"",
			because: "description-only updates should leave icon background untouched");
	}

	[Test]
	[Description("Updates section icon metadata only and preserves the caption and description fields.")]
	public void UpdateSection_Should_Update_Icon_Metadata_Only() {
		// Arrange
		string? updateBody = null;
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp").Returns(
			new ApplicationInfoResult(
				"pkg-uid",
				"UsrOrdersApp",
				[],
				[],
				"app-id",
				"Orders App",
				"UsrOrdersApp",
				"8.3.0"));
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"Code\"", StringComparison.Ordinal)))
			.Returns(
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""",
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"11111111-1111-1111-1111-111111111111","IconBackground":"#A1B2C3","ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"LogoId\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal)))
			.Returns(callInfo => {
				updateBody = callInfo.ArgAt<string>(1);
				return """{"success":true}""";
			});

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			"sandbox",
			new ApplicationSectionUpdateRequest(
				"UsrOrdersApp",
				"UsrOrders",
				IconId: "11111111-1111-1111-1111-111111111111",
				IconBackground: "#A1B2C3"));

		// Assert
		result.Section.IconId.Should().Be("11111111-1111-1111-1111-111111111111",
			because: "the response should expose the updated icon id");
		result.Section.IconBackground.Should().Be("#A1B2C3",
			because: "the response should expose the updated icon background");
		updateBody.Should().Contain("\"LogoId\"",
			because: "icon updates should target the section icon id field");
		updateBody.Should().Contain("\"IconBackground\"",
			because: "icon updates should target the section icon background field");
		updateBody.Should().NotContain("\"Caption\"",
			because: "icon-only updates should not rewrite caption");
		updateBody.Should().NotContain("\"Description\"",
			because: "icon-only updates should not rewrite description");
	}

	[Test]
	[Description("Rejects section update requests that do not provide any mutable fields.")]
	public void UpdateSection_Should_Reject_Request_Without_Mutable_Fields() {
		// Arrange
		ApplicationSectionUpdateRequest request = new(
			"UsrOrdersApp",
			"UsrOrders");

		// Act
		Action action = () => _sut.UpdateSection("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*At least one mutable field*",
				because: "the update flow should fail fast when there is nothing to change");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
	}
}
