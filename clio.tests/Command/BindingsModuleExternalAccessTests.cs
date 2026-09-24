using System;
using System.IO.Abstractions.TestingHelpers;
using ATF.Repository.Providers;
using Clio;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// AC-5: an ATF-backed command on an external-access environment must say why it cannot run, not run
/// as somebody else.
/// </summary>
/// <remarks>
/// The whole of AC-5 is one <c>throw</c> placed ABOVE the access-token and login/password branches in
/// <c>BuildRemoteDataProvider</c>. Its position is the safety property: falling through reaches the
/// credential branch, and with no credentials present that is the "Supervisor" default — a silent
/// connection to a customer production site as the wrong identity. A later edit that reorders the
/// branches compiles and passes CI, so the ordering is pinned here rather than left to review.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class BindingsModuleExternalAccessTests {

	private static ServiceProvider RegisterFor(EnvironmentSettings settings) {
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		return (ServiceProvider)new BindingsModule(fileSystem).Register(settings);
	}

	[Test]
	[Description("An external-access session is cookies, and ATF.Repository has no session-cookie constructor, so the data provider must refuse with a message that names the reason.")]
	public void DataProvider_ShouldRefuse_ForAnExternalAccessEnvironment() {
		// Arrange
		using ServiceProvider provider = RegisterFor(new EnvironmentSettings {
			Uri = "https://customer.creatio.com",
			ExternalAccessToken = "jwt",
			IsNetCore = true
		});
		IDataProvider dataProvider = provider.GetRequiredService<IDataProvider>();

		// Act
		// The registration is lazy, so the refusal surfaces on first use rather than at resolution.
		Action act = () => dataProvider.GetSysSettingValue<string>("BuildNumber");

		// Assert
		act.Should().Throw<NotSupportedException>()
			.Which.Message.Should().Contain("ATF.Repository",
				because: "falling through would build a Supervisor-credentialed provider against a customer site");
	}

	[Test]
	[Description("The refusal has to win over the credential branch, or a token co-supplied with a login would connect as that login instead of saying the command cannot run.")]
	public void DataProvider_ShouldRefuse_EvenWhenCredentialsArePresent() {
		// Arrange
		using ServiceProvider provider = RegisterFor(new EnvironmentSettings {
			Uri = "https://customer.creatio.com",
			ExternalAccessToken = "jwt",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		});
		IDataProvider dataProvider = provider.GetRequiredService<IDataProvider>();

		// Act
		Action act = () => dataProvider.GetSysSettingValue<string>("BuildNumber");

		// Assert
		act.Should().Throw<NotSupportedException>(
			because: "the ordering of the guard above the login/password branch is the safety property of AC-5");
	}

	[Test]
	[Description("The combination rule must hold on the DI path too — it used to live inside the application-client factory, so this dispatch path authenticated silently under the grant.")]
	public void DataProvider_ShouldRefuse_WhenAnAccessTokenIsCoSupplied() {
		// Arrange
		using ServiceProvider provider = RegisterFor(new EnvironmentSettings {
			Uri = "https://customer.creatio.com",
			ExternalAccessToken = "jwt",
			AccessToken = "api-token",
			IsNetCore = true
		});
		IDataProvider dataProvider = provider.GetRequiredService<IDataProvider>();

		// Act
		Action act = () => dataProvider.GetSysSettingValue<string>("BuildNumber");

		// Assert
		act.Should().Throw<NotSupportedException>()
			.Which.Message.Should().Contain("exactly one",
				because: "every construction site must ask the same question, not just the factory");
	}
}
