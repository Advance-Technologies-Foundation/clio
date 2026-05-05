using System;
using System.Collections.Generic;
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
	[Description("Resolve should use canonical DataForge and IdentityServer syssettings from the direct reader instead of any persisted local OAuth state.")]
	public void Resolve_Should_Use_Canonical_SysSettings_For_DataForge_Config() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("https://dataforge.example/"),
			["DataForgeServiceQueryTimeout"] = Found("30000"),
			["DataForgeSimilarTablesResultLimit"] = Found("50"),
			["DataForgeLookupResultLimit"] = Found("5"),
			["DataForgeTableRelationshipsCountLimit"] = Found("5"),
			["IdentityServerUrl"] = Found("https://syssettings.identity/connect/token"),
			["IdentityServerClientId"] = Found("sys-client"),
			["IdentityServerClientSecret"] = Found("sys-secret")
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		result.ServiceUrl.Should().Be("https://dataforge.example/");
		result.AuthMode.Should().Be(DataForgeAuthMode.OAuthClientCredentials);
		result.TokenUrl.Should().Be("https://syssettings.identity/connect/token");
		result.ClientId.Should().Be("sys-client");
		result.ClientSecret.Should().Be("sys-secret");
		result.Scope.Should().Be("use_enrichment");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should not require the legacy syssettings auth fallback flag because DataForge now treats Creatio syssettings as the canonical source.")]
	public void Resolve_Should_Ignore_Legacy_Fallback_Flag_When_SysSettings_Are_Available() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("https://dataforge.example/"),
			["DataForgeServiceQueryTimeout"] = Found("30000"),
			["DataForgeSimilarTablesResultLimit"] = Found("50"),
			["DataForgeLookupResultLimit"] = Found("5"),
			["DataForgeTableRelationshipsCountLimit"] = Found("5"),
			["IdentityServerUrl"] = Found("https://identity-from-syssettings/connect/token"),
			["IdentityServerClientId"] = Found("sys-client"),
			["IdentityServerClientSecret"] = Found("sys-secret")
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest {
			AllowSysSettingsAuthFallback = false
		});

		result.AuthMode.Should().Be(DataForgeAuthMode.OAuthClientCredentials);
		result.TokenUrl.Should().Be("https://identity-from-syssettings/connect/token");
		result.ClientId.Should().Be("sys-client");
		result.ClientSecret.Should().Be("sys-secret");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should keep explicit request OAuth credentials as the highest-priority emergency override.")]
	public void Resolve_Should_Prefer_Explicit_Request_OAuth_Override() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("https://dataforge.example/"),
			["DataForgeServiceQueryTimeout"] = Found("30000"),
			["DataForgeSimilarTablesResultLimit"] = Found("50"),
			["DataForgeLookupResultLimit"] = Found("5"),
			["DataForgeTableRelationshipsCountLimit"] = Found("5"),
			["IdentityServerUrl"] = Found("https://identity-from-syssettings/connect/token"),
			["IdentityServerClientId"] = Found("sys-client"),
			["IdentityServerClientSecret"] = Found("sys-secret")
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest {
			AuthAppUri = "https://request.identity/connect/token",
			ClientId = "request-client",
			ClientSecret = "request-secret"
		});

		result.TokenUrl.Should().Be("https://request.identity/connect/token");
		result.ClientId.Should().Be("request-client");
		result.ClientSecret.Should().Be("request-secret");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should return auth mode none when IdentityServer syssettings are not available and no explicit request credentials were supplied.")]
	public void Resolve_Should_Not_Fall_Back_To_Persisted_Environment_OAuth_When_SysSettings_Are_Missing() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("https://dataforge.example/"),
			["DataForgeServiceQueryTimeout"] = Found("30000"),
			["DataForgeSimilarTablesResultLimit"] = Found("50"),
			["DataForgeLookupResultLimit"] = Found("5"),
			["DataForgeTableRelationshipsCountLimit"] = Found("5"),
			["IdentityServerUrl"] = Missing(),
			["IdentityServerClientId"] = Missing(),
			["IdentityServerClientSecret"] = Missing()
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		result.AuthMode.Should().Be(DataForgeAuthMode.None);
		result.TokenUrl.Should().BeNull();
		result.ClientId.Should().BeNull();
		result.ClientSecret.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should reject missing DataForgeServiceUrl because direct DataForge calls cannot be constructed without the server-owned endpoint.")]
	public void Resolve_Should_Reject_Missing_Service_Url() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Missing()
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		Action act = () => resolver.Resolve(new DataForgeConfigRequest());

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*DataForgeServiceUrl*not configured*");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should normalize syssettings returned as JSON string literals so quoted URLs and client ids remain valid for outbound DataForge calls.")]
	public void Resolve_Should_Unquote_Json_String_SysSettings() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("\"https://dataforge.example/\""),
			["DataForgeServiceQueryTimeout"] = Found("30000"),
			["DataForgeSimilarTablesResultLimit"] = Found("50"),
			["DataForgeLookupResultLimit"] = Found("5"),
			["DataForgeTableRelationshipsCountLimit"] = Found("5"),
			["IdentityServerUrl"] = Found("\"https://identity.example/connect/token\""),
			["IdentityServerClientId"] = Found("\"sys-client\""),
			["IdentityServerClientSecret"] = Found("\"sys-secret\"")
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		result.ServiceUrl.Should().Be("https://dataforge.example/");
		result.TokenUrl.Should().Be("https://identity.example/connect/token");
		result.ClientId.Should().Be("sys-client");
		result.ClientSecret.Should().Be("sys-secret");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should normalize IdentityServer syssettings into a concrete token endpoint when the syssetting contains only the identity service base URL.")]
	public void Resolve_Should_Append_ConnectToken_To_IdentityServerUrl_Fallback() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("https://dataforge.example/"),
			["DataForgeServiceQueryTimeout"] = Found("30000"),
			["DataForgeSimilarTablesResultLimit"] = Found("50"),
			["DataForgeLookupResultLimit"] = Found("5"),
			["DataForgeTableRelationshipsCountLimit"] = Found("5"),
			["IdentityServerUrl"] = Found("https://identity.example:31390"),
			["IdentityServerClientId"] = Found("sys-client"),
			["IdentityServerClientSecret"] = Found("sys-secret")
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		DataForgeResolvedConfig result = resolver.Resolve(new DataForgeConfigRequest());

		result.TokenUrl.Should().Be("https://identity.example:31390/connect/token");
		result.Scope.Should().Be("use_enrichment");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve should reject service URLs that are found in SysSettings but are not absolute http/https URIs.")]
	public void Resolve_Should_Reject_Invalid_Service_Url_Returned_By_Direct_Read() {
		IDataForgeSysSettingDirectReader directReader = CreateDirectReader(new Dictionary<string, DataForgeSysSettingReadResult> {
			["DataForgeServiceUrl"] = Found("not-a-valid-url")
		});
		DataForgeConfigResolver resolver = CreateResolver(directReader);

		Action act = () => resolver.Resolve(new DataForgeConfigRequest());

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not a valid absolute http/https URL*");
	}

	private static DataForgeConfigResolver CreateResolver(IDataForgeSysSettingDirectReader directReader) {
		return new(directReader, Substitute.For<ISysSettingsManager>());
	}

	private static IDataForgeSysSettingDirectReader CreateDirectReader(
		IReadOnlyDictionary<string, DataForgeSysSettingReadResult> valuesByCode) {
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadValue(Arg.Any<string>())
			.Returns(callInfo => valuesByCode.TryGetValue(callInfo.Arg<string>(), out DataForgeSysSettingReadResult result)
				? result
				: Missing());
		directReader.ReadTextValue(Arg.Any<string>())
			.Returns(callInfo => valuesByCode.TryGetValue(callInfo.Arg<string>(), out DataForgeSysSettingReadResult result)
				? result
				: Missing());
		return directReader;
	}

	private static DataForgeSysSettingReadResult Found(string value) => new(true, value, null);

	private static DataForgeSysSettingReadResult Missing() => new(false, null, null);
}
