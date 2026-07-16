using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("uninstall-creatio")]
public sealed class UninstallWarningIisApplicationPoolResolverE2ETests {
	private const string ToolName = UninstallCreatioTool.UninstallCreatioToolName;
	private const string ApplicationsXml = """
		<?xml version="1.0" encoding="UTF-8"?>
		<appcmd>
		  <APP path="/studio" APP.NAME="Default Web Site/studio" APPPOOL.NAME="studio-pool" SITE.NAME="Default Web Site" />
		</appcmd>
		""";

	[Test]
	[Description("Reads Uri from raw clio environment output when EnvironmentPath is empty.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness reads URI without EnvironmentPath")]
	public void ResolveEnvironmentValue_ShouldReturnUri_WhenEnvironmentPathIsEmpty() {
		// Arrange
		const string rawEnvironment = "EnvironmentPath: \r\nUri: http://ts1-agent80:88/studio\r\n";

		// Act
		string result = ClioEnvironmentCommandResolver.ResolveEnvironmentValue(rawEnvironment, "Uri");

		// Assert
		result.Should().Be("http://ts1-agent80:88/studio",
			because: "the warning scenario needs the registered URI and must not require unrelated path metadata");
	}

	[Test]
	[Description("Matches a registered sandbox URI to one IIS application behind a wildcard binding.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness resolves wildcard IIS binding")]
	public void Resolve_ShouldReturnApplicationPool_WhenWildcardBindingAndPathMatch() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent80:88/studio/");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="Default Web Site" bindings="http/*:88:" state="Started" />
			</appcmd>
			""";

		// Act
		string result = IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, ApplicationsXml, host => host == "ts1-agent80");

		// Assert
		result.Should().Be("studio-pool",
			because: "TeamCity exposes the sandbox through a wildcard IIS binding and application path");
	}

	[Test]
	[Description("Uses TeamCity's explicit pool when the public sandbox URL is routed independently of local IIS paths.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness resolves TeamCity routed IIS pool")]
	public void Resolve_ShouldReturnExpectedPool_WhenPublicUrlDoesNotMatchLocalIisPath() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent54:88/studioenu_15736567_0716");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="studioenu_15736567_0716" bindings="http/*:40120:" state="Started" />
			</appcmd>
			""";
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="studioenu_15736567_0716/" APPPOOL.NAME="studioenu_15736567_0716" SITE.NAME="studioenu_15736567_0716" />
			</appcmd>
			""";

		// Act
		string result = IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, applicationsXml, "studioenu_15736567_0716",
			host => host == "ts1-agent54");

		// Assert
		result.Should().Be("studioenu_15736567_0716",
			because: "TeamCity's explicit pool is cross-checked against the URL target and live IIS assignment");
	}

	[Test]
	[Description("Rejects TeamCity's routed pool when multiple applications share the sandbox profile.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness identifies routed shared pool as non-applicable")]
	public void Resolve_ShouldThrowSharedPoolException_WhenRoutedPoolIsSharedWithinSandboxSite() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent54:88/studioenu_15736978_0716");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="studioenu_15736978_0716" bindings="http/*:40120:" state="Started" />
			</appcmd>
			""";
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="studioenu_15736978_0716/" APPPOOL.NAME="studioenu_15736978_0716" SITE.NAME="studioenu_15736978_0716" />
			  <APP path="/0" APP.NAME="studioenu_15736978_0716/0" APPPOOL.NAME="studioenu_15736978_0716" SITE.NAME="studioenu_15736978_0716" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, applicationsXml, "studioenu_15736978_0716",
			host => host == "ts1-agent54");

		// Assert
		act.Should().Throw<SharedIisApplicationPoolException>(
				because: "locking a profile shared by multiple applications cannot exercise safe pool deletion")
			.WithMessage("*assigned to 2 IIS applications*");
	}

	[Test]
	[Description("Uses an explicit pool when a local root-site URL matches the pool's sole IIS application.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness resolves explicit local root-site pool")]
	public void Resolve_ShouldReturnExpectedPool_WhenLocalRootSiteMatchesDirectly() {
		// Arrange
		Uri environmentUri = new("http://localhost:40293/");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="clio893" bindings="http/*:40293:" state="Started" />
			</appcmd>
			""";
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="clio893/" APPPOOL.NAME="clio893" SITE.NAME="clio893" />
			</appcmd>
			""";

		// Act
		string result = IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, applicationsXml, "clio893", host => host == "localhost");

		// Assert
		result.Should().Be("clio893",
			because: "developer-local validation uses a directly bound root IIS site rather than TeamCity routing");
	}

	[Test]
	[Description("Rejects an explicit pool that does not identify the registered sandbox URL target.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects unrelated explicit pool")]
	public void Resolve_ShouldThrow_WhenExpectedPoolDoesNotMatchUriTarget() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent54:88/studioenu_15736567_0716");
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="Unrelated/" APPPOOL.NAME="unrelated-pool" SITE.NAME="Unrelated" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, "<appcmd />", applicationsXml, "unrelated-pool",
			host => host == "ts1-agent54");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an explicit build parameter must not authorize locking an unrelated IIS profile")
			.WithMessage("*does not match the registered URI target*");
	}

	[Test]
	[Description("Rejects an unrelated explicitly named pool even when multiple IIS applications share it.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects unrelated shared explicit pool")]
	public void Resolve_ShouldThrow_WhenUnrelatedExpectedPoolHasMultipleAssignments() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent54:88/studioenu_15736567_0716");
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="First/" APPPOOL.NAME="studioenu_15736567_0716" SITE.NAME="First" />
			  <APP path="/nested" APP.NAME="Second/nested" APPPOOL.NAME="studioenu_15736567_0716" SITE.NAME="Second" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, "<appcmd />", applicationsXml, "studioenu_15736567_0716",
			host => host == "ts1-agent54");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a shared-pool skip must not mask an explicit pool unrelated to the registered sandbox")
			.WithMessage("*does not match the registered URI target*");
	}

	[Test]
	[Description("Rejects a routed pool whose sole IIS assignment belongs to an unrelated live site.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects unrelated routed pool assignment")]
	public void Resolve_ShouldThrow_WhenRoutedPoolAssignmentDoesNotIdentifyTarget() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent54:88/studioenu_15736567_0716");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="Unrelated" bindings="http/*:40120:" state="Started" />
			</appcmd>
			""";
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="Unrelated/" APPPOOL.NAME="studioenu_15736567_0716" SITE.NAME="Unrelated" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, applicationsXml, "studioenu_15736567_0716",
			host => host == "ts1-agent54");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a matching URL tail must not authorize an unrelated IIS site's application pool")
			.WithMessage("*does not match the registered URI target*");
	}

	[Test]
	[Description("Rejects a routed pool assignment whose referenced IIS site is absent.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects routed pool without live site")]
	public void Resolve_ShouldThrow_WhenRoutedPoolSiteIsMissing() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent54:88/studioenu_15736567_0716");
		const string applicationsXml = """
			<appcmd>
			  <APP path="/" APP.NAME="studioenu_15736567_0716/" APPPOOL.NAME="studioenu_15736567_0716" SITE.NAME="studioenu_15736567_0716" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, "<appcmd />", applicationsXml, "studioenu_15736567_0716",
			host => host == "ts1-agent54");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a stale AppCmd application record must not authorize destructive profile setup")
			.WithMessage("*does not match the registered URI target*");
	}

	[Test]
	[Description("Rejects a non-HTTP sandbox URI before resolving an explicit IIS application pool.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects non-HTTP routed target")]
	public void Resolve_ShouldThrow_WhenUriSchemeIsNotHttp() {
		// Arrange
		Uri environmentUri = new("file://ts1-agent54/studioenu_15736567_0716");

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, "<appcmd />", "<appcmd />", "studioenu_15736567_0716",
			host => host == "ts1-agent54");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "IIS web targets must use an HTTP transport even when a pool parameter is present")
			.WithMessage("*must use HTTP or HTTPS*");
	}

	[Test]
	[Description("Reads a TeamCity configuration parameter through its Java-properties file indirection.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness reads TeamCity application pool parameter")]
	public void TeamCityBuildParameterResolve_ShouldReturnApplicationPoolName_WhenConfigurationFileDefinesParameter() {
		// Arrange
		const string buildPropertiesPath = "build.properties";
		const string configurationPropertiesPath = @"C:\TeamCity\temp\configuration.properties";
		Dictionary<string, string[]> files = new(StringComparer.OrdinalIgnoreCase) {
			[buildPropertiesPath] = [@"teamcity.configuration.properties.file=C\:\\TeamCity\\temp\\configuration.properties"],
			[configurationPropertiesPath] = ["ApplicationPoolName=studioenu_15736567_0716"]
		};

		// Act
		string? result = TeamCityBuildParameterResolver.Resolve(
			"ApplicationPoolName", buildPropertiesPath, files.ContainsKey, path => files[path]);

		// Assert
		result.Should().Be("studioenu_15736567_0716",
			because: "TeamCity stores configuration parameters in the referenced properties file rather than process environment variables");
	}

	[Test]
	[Description("Returns no TeamCity parameter when the referenced configuration-properties file is unavailable.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness fails closed without TeamCity configuration properties")]
	public void TeamCityBuildParameterResolve_ShouldReturnNull_WhenConfigurationFileIsMissing() {
		// Arrange
		const string buildPropertiesPath = "build.properties";
		string[] buildProperties =
			[@"teamcity.configuration.properties.file=C\:\\TeamCity\\temp\\missing.properties"];

		// Act
		string? result = TeamCityBuildParameterResolver.Resolve(
			"ApplicationPoolName", buildPropertiesPath,
			path => path == buildPropertiesPath, _ => buildProperties);

		// Assert
		result.Should().BeNull(
			because: "an unavailable TeamCity properties file must never invent an IIS application-pool target");
	}

	[Test]
	[Description("Rejects a sandbox URI whose IIS binding does not match.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects unmatched IIS binding")]
	public void Resolve_ShouldThrow_WhenBindingDoesNotMatch() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent80:88/studio?access_token=secret-sentinel");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="Default Web Site" bindings="http/*:88:other-agent" state="Started" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, ApplicationsXml, host => host == "ts1-agent80");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an unmatched target must fail before the destructive MCP call")
			.WithMessage("*matched 0 IIS applications*")
			.And.Message.Should().NotContain("secret-sentinel",
				because: "query credentials must not be written into TeamCity failure diagnostics");
	}

	[Test]
	[Description("Rejects a sandbox URI that ambiguously matches applications in more than one IIS site.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects ambiguous IIS target")]
	public void Resolve_ShouldThrow_WhenApplicationMatchIsAmbiguous() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent80:88/studio");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="First" bindings="http/*:88:" state="Started" />
			  <SITE SITE.NAME="Second" bindings="http/*:88:" state="Started" />
			</appcmd>
			""";
		const string applicationsXml = """
			<appcmd>
			  <APP path="/studio" APP.NAME="First/studio" APPPOOL.NAME="first-pool" SITE.NAME="First" />
			  <APP path="/studio" APP.NAME="Second/studio" APPPOOL.NAME="second-pool" SITE.NAME="Second" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, applicationsXml, host => host == "ts1-agent80");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an ambiguous target must fail before the destructive MCP call")
			.WithMessage("*matched 2 IIS applications*");
	}

	[Test]
	[Description("Rejects a matched IIS application that has no application-pool name.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects application without pool")]
	public void Resolve_ShouldThrow_WhenApplicationPoolNameIsMissing() {
		// Arrange
		Uri environmentUri = new("http://ts1-agent80:88/studio");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="Default Web Site" bindings="http/*:88:" state="Started" />
			</appcmd>
			""";
		const string applicationsXml = """
			<appcmd>
			  <APP path="/studio" APP.NAME="Default Web Site/studio" SITE.NAME="Default Web Site" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, applicationsXml, host => host == "ts1-agent80");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a target without an explicit pool must fail before the destructive MCP call")
			.WithMessage("*has no application-pool name*");
	}

	[Test]
	[Description("Rejects a foreign registered hostname even when a wildcard IIS binding and application path match.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects foreign host on wildcard binding")]
	public void Resolve_ShouldThrow_WhenRegisteredHostIsNotLocal() {
		// Arrange
		Uri environmentUri = new("http://other-agent:88/studio");
		const string sitesXml = """
			<appcmd>
			  <SITE SITE.NAME="Default Web Site" bindings="http/*:88:" state="Started" />
			</appcmd>
			""";

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, sitesXml, ApplicationsXml, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a wildcard binding must not authorize a stale URI for another machine")
			.WithMessage("*does not identify the current machine*");
	}

	[Test]
	[Description("Recognizes the current machine name as a local destructive E2E target.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness recognizes current machine name")]
	public void HostIdentifiesCurrentMachine_ShouldReturnTrue_WhenHostIsMachineName() {
		// Arrange
		string host = Environment.MachineName;

		// Act
		bool result = IisApplicationPoolResolver.HostIdentifiesCurrentMachine(host);

		// Assert
		result.Should().BeTrue(
			because: "TeamCity registers the sandbox with the build agent machine name");
	}

	[Test]
	[Description("Rejects user information in the registered sandbox URI without leaking it into diagnostics.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall warning harness rejects URI user information safely")]
	public void Resolve_ShouldThrowWithoutSecret_WhenUriContainsUserInformation() {
		// Arrange
		Uri environmentUri = new("http://secret-user:secret-password@ts1-agent80:88/studio");

		// Act
		Action act = () => IisApplicationPoolResolver.Resolve(
			environmentUri, "<appcmd />", "<appcmd />", host => host == "ts1-agent80");

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "IIS target URIs never require embedded credentials")
			.WithMessage("*must not contain user information*")
			.Which;
		exception.Message.Should().NotContain("secret-user",
			because: "URI usernames must not be written into TeamCity failure diagnostics");
		exception.Message.Should().NotContain("secret-password",
			because: "URI passwords must not be written into TeamCity failure diagnostics");
	}
}
