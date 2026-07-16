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
