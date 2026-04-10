using System;
using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class DataForgeConfigResolverTests {
	[Test]
	[Category("Unit")]
	[Description("Resolve should prefer OAuth credentials from the clio environment settings over IdentityServer syssettings when both are present.")]
	public void Resolve_Should_Prefer_Environment_OAuth_Settings() {
		// Arrange
		EnvironmentSettings settings = new() {
			AuthAppUri = "https://identity.example/connect/token",
			ClientId = "clio-client",
			ClientSecret = "clio-secret"
		};
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("https://dataforge.example/");
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeServiceQueryTimeout").Returns(15000);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeSimilarTablesResultLimit").Returns(50);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeLookupResultLimit").Returns(5);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeTableRelationshipsCountLimit").Returns(5);
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("https://syssettings.identity/connect/token");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("sys-client");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("sys-secret");
		DataForgeConfigResolver resolver = new(settings, sysSettingsManager);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		// Assert
		result.ServiceUrl.Should().Be("https://dataforge.example/");
		result.AuthMode.Should().Be(DataForgeAuthMode.OAuthClientCredentials);
		result.TokenUrl.Should().Be("https://identity.example/connect/token");
		result.ClientId.Should().Be("clio-client");
		result.ClientSecret.Should().Be("clio-secret");
		result.Scope.Should().Be("use_enrichment");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should use IdentityServer syssettings only when the caller explicitly allows the fallback and the clio environment does not already contain OAuth credentials.")]
	public void Resolve_Should_Use_SysSettings_OAuth_Fallback_Only_When_Enabled() {
		// Arrange
		EnvironmentSettings settings = new();
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("https://dataforge.example/");
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeServiceQueryTimeout").Returns(10000);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeSimilarTablesResultLimit").Returns(50);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeLookupResultLimit").Returns(5);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeTableRelationshipsCountLimit").Returns(5);
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("https://identity-from-syssettings/connect/token");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("sys-client");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("sys-secret");
		DataForgeConfigResolver resolver = new(settings, sysSettingsManager);

		// Act
		DataForgeResolvedConfig disabledFallbackResult = resolver.Resolve(new DataForgeConfigRequest());
		DataForgeResolvedConfig enabledFallbackResult = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = true
		});

		// Assert
		disabledFallbackResult.AuthMode.Should().Be(DataForgeAuthMode.None,
			because: "syssettings fallback must remain opt-in to keep auth primarily managed by clio");
		enabledFallbackResult.AuthMode.Should().Be(DataForgeAuthMode.OAuthClientCredentials);
		enabledFallbackResult.TokenUrl.Should().Be("https://identity-from-syssettings/connect/token");
		enabledFallbackResult.ClientId.Should().Be("sys-client");
		enabledFallbackResult.ClientSecret.Should().Be("sys-secret");
		enabledFallbackResult.Scope.Should().Be("use_enrichment");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should reject missing DataForgeServiceUrl because direct DataForge calls cannot be constructed without the server-owned endpoint.")]
	public void Resolve_Should_Reject_Missing_Service_Url() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns(string.Empty);
		DataForgeConfigResolver resolver = new(new EnvironmentSettings(), sysSettingsManager);

		// Act
		FluentActions.Invoking(() => resolver.Resolve(new DataForgeConfigRequest()))
			.Should().Throw<InvalidOperationException>()
			.WithMessage("*DataForgeServiceUrl*");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should normalize string syssettings returned as JSON string literals so quoted URLs and client ids remain valid for outbound DataForge calls.")]
	public void Resolve_Should_Unquote_Json_String_SysSettings() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("\"https://dataforge.example/\"");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("\"https://identity.example/connect/token\"");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("\"sys-client\"");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("\"sys-secret\"");
		DataForgeConfigResolver resolver = new(new EnvironmentSettings(), sysSettingsManager);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = true
		});

		// Assert
		result.ServiceUrl.Should().Be("https://dataforge.example/");
		result.TokenUrl.Should().Be("https://identity.example/connect/token");
		result.ClientId.Should().Be("sys-client");
		result.ClientSecret.Should().Be("sys-secret");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should normalize IdentityServer syssettings into a concrete token endpoint when the syssetting contains only the identity service base URL.")]
	public void Resolve_Should_Append_ConnectToken_To_IdentityServerUrl_Fallback() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("https://dataforge.example/");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("https://identity.example:31390");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("sys-client");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("sys-secret");
		DataForgeConfigResolver resolver = new(new EnvironmentSettings(), sysSettingsManager);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = true
		});

		// Assert
		result.TokenUrl.Should().Be("https://identity.example:31390/connect/token");
		result.Scope.Should().Be("use_enrichment");
	}
}
