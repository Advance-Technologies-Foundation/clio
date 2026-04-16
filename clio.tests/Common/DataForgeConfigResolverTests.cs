using System;
using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
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
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("https://syssettings.identity/connect/token");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("sys-client");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("sys-secret");
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		DataForgeConfigResolver resolver = CreateResolver(settings, sysSettingsManager, directReader);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		// Assert
		result.ServiceUrl.Should().Be("https://dataforge.example/",
			because: "the configured Data Forge service URL should be preserved when the gateway returns a valid URL");
		result.AuthMode.Should().Be(DataForgeAuthMode.OAuthClientCredentials,
			because: "environment OAuth credentials should win over syssettings fallback");
		result.TokenUrl.Should().Be("https://identity.example/connect/token",
			because: "the environment identity endpoint should be used when present");
		result.ClientId.Should().Be("clio-client",
			because: "the environment client id should win over syssettings fallback");
		result.ClientSecret.Should().Be("clio-secret",
			because: "the environment client secret should win over syssettings fallback");
		result.Scope.Should().Be("use_enrichment",
			because: "the default Data Forge scope should stay stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should use IdentityServer syssettings only when the caller explicitly allows the fallback and the clio environment does not already contain OAuth credentials.")]
	public void Resolve_Should_Use_SysSettings_OAuth_Fallback_Only_When_Enabled() {
		// Arrange
		EnvironmentSettings settings = new();
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("https://dataforge.example/");
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("https://identity-from-syssettings/connect/token");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("sys-client");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("sys-secret");
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		DataForgeConfigResolver resolver = CreateResolver(settings, sysSettingsManager, directReader);

		// Act
		DataForgeResolvedConfig disabledFallbackResult = resolver.Resolve(new DataForgeConfigRequest());
		DataForgeResolvedConfig enabledFallbackResult = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = true
		});

		// Assert
		disabledFallbackResult.AuthMode.Should().Be(DataForgeAuthMode.None,
			because: "syssettings fallback must remain opt-in to keep auth primarily managed by clio");
		enabledFallbackResult.AuthMode.Should().Be(DataForgeAuthMode.OAuthClientCredentials,
			because: "the explicit opt-in should enable IdentityServer syssettings fallback");
		enabledFallbackResult.TokenUrl.Should().Be("https://identity-from-syssettings/connect/token",
			because: "the syssettings token endpoint should be used when fallback is enabled");
		enabledFallbackResult.ClientId.Should().Be("sys-client",
			because: "the syssettings client id should be used when fallback is enabled");
		enabledFallbackResult.ClientSecret.Should().Be("sys-secret",
			because: "the syssettings client secret should be used when fallback is enabled");
		enabledFallbackResult.Scope.Should().Be("use_enrichment",
			because: "the default Data Forge scope should stay stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should reject missing DataForgeServiceUrl because direct DataForge calls cannot be constructed without the server-owned endpoint.")]
	public void Resolve_Should_Reject_Missing_Service_Url() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns(string.Empty);
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadTextValue("DataForgeServiceUrl")
			.Returns(new DataForgeSysSettingReadResult(false, null, null));
		DataForgeConfigResolver resolver = CreateResolver(new EnvironmentSettings(), sysSettingsManager, directReader);

		// Act
		Action act = () => resolver.Resolve(new DataForgeConfigRequest());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*DataForgeServiceUrl*not configured*");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should normalize string syssettings returned as JSON string literals so quoted URLs and client ids remain valid for outbound DataForge calls.")]
	public void Resolve_Should_Unquote_Json_String_SysSettings() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("\"https://dataforge.example/\"");
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("\"https://identity.example/connect/token\"");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("\"sys-client\"");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("\"sys-secret\"");
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		DataForgeConfigResolver resolver = CreateResolver(new EnvironmentSettings(), sysSettingsManager, directReader);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = true
		});

		// Assert
		result.ServiceUrl.Should().Be("https://dataforge.example/",
			because: "gateway syssettings can return JSON-quoted strings that must be normalized before use");
		result.TokenUrl.Should().Be("https://identity.example/connect/token",
			because: "quoted identity URLs must be normalized before OAuth token calls");
		result.ClientId.Should().Be("sys-client",
			because: "quoted client ids must be normalized before OAuth token calls");
		result.ClientSecret.Should().Be("sys-secret",
			because: "quoted client secrets must be normalized before OAuth token calls");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should normalize IdentityServer syssettings into a concrete token endpoint when the syssetting contains only the identity service base URL.")]
	public void Resolve_Should_Append_ConnectToken_To_IdentityServerUrl_Fallback() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("https://dataforge.example/");
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerUrl").Returns("https://identity.example:31390");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientId").Returns("sys-client");
		sysSettingsManager.GetSysSettingValueByCode("IdentityServerClientSecret").Returns("sys-secret");
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		DataForgeConfigResolver resolver = CreateResolver(new EnvironmentSettings(), sysSettingsManager, directReader);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = true
		});

		// Assert
		result.TokenUrl.Should().Be("https://identity.example:31390/connect/token",
			because: "identity base URLs should be normalized to the concrete token endpoint");
		result.Scope.Should().Be("use_enrichment",
			because: "the default Data Forge scope should remain stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should fall back to the direct SysSettings reader when the cliogate path returns HTML instead of a valid DataForgeServiceUrl value.")]
	public void Resolve_Should_Fall_Back_To_Direct_Read_When_Gateway_Returns_Html() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl")
			.Returns("<!DOCTYPE html><html><body>404 gateway not found</body></html>");
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadTextValue("DataForgeServiceUrl")
			.Returns(new DataForgeSysSettingReadResult(true, "https://data-forge-stage.bpmonline.com/", null));
		DataForgeConfigResolver resolver = CreateResolver(new EnvironmentSettings(), sysSettingsManager, directReader);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		// Assert
		result.ServiceUrl.Should().Be("https://data-forge-stage.bpmonline.com/",
			because: "HTML gateway responses should be rejected and retried through direct site reads");
		directReader.Received(1).ReadTextValue("DataForgeServiceUrl");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should fall back to the direct SysSettings reader when the cliogate path throws while reading DataForgeServiceUrl.")]
	public void Resolve_Should_Fall_Back_To_Direct_Read_When_Gateway_Throws() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl")
			.Returns(_ => throw new InvalidOperationException("cliogate endpoint is unavailable"));
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadTextValue("DataForgeServiceUrl")
			.Returns(new DataForgeSysSettingReadResult(true, "https://data-forge-stage.bpmonline.com/", null));
		DataForgeConfigResolver resolver = CreateResolver(new EnvironmentSettings(), sysSettingsManager, directReader);

		// Act
		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		// Assert
		result.ServiceUrl.Should().Be("https://data-forge-stage.bpmonline.com/",
			because: "Data Forge should remain functional even when cliogate is missing or outdated");
		directReader.Received(1).ReadTextValue("DataForgeServiceUrl");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should reject service URLs that are found but are not absolute http/https URIs, even after falling back to the direct SysSettings reader.")]
	public void Resolve_Should_Reject_Invalid_Service_Url_Returned_By_Direct_Read() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns(string.Empty);
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadTextValue("DataForgeServiceUrl")
			.Returns(new DataForgeSysSettingReadResult(true, "not-a-valid-url", null));
		DataForgeConfigResolver resolver = CreateResolver(new EnvironmentSettings(), sysSettingsManager, directReader);

		// Act
		Action act = () => resolver.Resolve(new DataForgeConfigRequest());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not a valid absolute http/https URL*");
	}

	private static DataForgeConfigResolver CreateResolver(
		EnvironmentSettings settings,
		ISysSettingsManager sysSettingsManager,
		IDataForgeSysSettingDirectReader directReader) {
		return new(settings, sysSettingsManager, directReader);
	}

	private static void ConfigureDefaultNumericSysSettings(ISysSettingsManager sysSettingsManager) {
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeServiceQueryTimeout").Returns(30_000);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeSimilarTablesResultLimit").Returns(50);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeLookupResultLimit").Returns(5);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeTableRelationshipsCountLimit").Returns(5);
	}
}
