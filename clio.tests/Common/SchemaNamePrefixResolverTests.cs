using System;
using System.Collections.Generic;
using System.Net;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class SchemaNamePrefixResolverTests {

	#region Fields: Private

	private ILogger _logger;
	private ISysSettingsManager _sysSettingsManager;

	#endregion

	#region Methods: Private

	private SchemaNamePrefixResolver CreateSut(string environmentUri) {
		EnvironmentSettings environmentSettings = environmentUri is null
			? null
			: new EnvironmentSettings {Uri = environmentUri, Login = "Supervisor", Password = "Supervisor"};
		return new SchemaNamePrefixResolver(environmentSettings, _ => _sysSettingsManager, _logger);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_logger = Substitute.For<ILogger>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
	}

	[Test]
	[Description("Returns the environment SchemaNamePrefix value when no explicit prefix is supplied.")]
	public void Resolve_ShouldReturnEnvironmentPrefix_WhenNoExplicitPrefixIsSupplied() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix).Returns("Usr");
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().Be("Usr",
			because: "the environment decides which prefix Creatio accepts for a generated schema");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Strips the quoting the sys-setting endpoint adds around a text value.")]
	public void Resolve_ShouldStripQuoting_WhenEnvironmentReturnsQuotedValue() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix).Returns("\" Ktl \"");
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().Be("Ktl",
			because: "quoting and padding around the stored value are transport shape, not part of the prefix");
	}

	[Test]
	[Description("Prefers an explicit prefix over the environment and performs no Creatio request.")]
	public void Resolve_ShouldPreferExplicitPrefix_WhenBothSourcesAreAvailable() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix).Returns("Usr");
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(" Ktl ");

		// Assert
		prefix.Should().Be("Ktl",
			because: "an explicitly requested prefix must win and be usable without surrounding whitespace");
		_sysSettingsManager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
	}

	[Test]
	[Description("Honours an explicit empty prefix as a deliberate request to generate without a prefix.")]
	public void Resolve_ShouldReturnEmptyWithoutWarning_WhenExplicitPrefixIsEmpty() {
		// Arrange
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(string.Empty);

		// Assert
		prefix.Should().BeEmpty(because: "an explicit empty prefix asks for an unprefixed schema on purpose");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		_sysSettingsManager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
	}

	[Test]
	[Description("Warns and generates without a prefix when no environment was resolved for the command.")]
	public void Resolve_ShouldWarnAndReturnEmpty_WhenNoEnvironmentIsResolved() {
		// Arrange
		SchemaNamePrefixResolver sut = CreateSut(null);

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "add-package must keep working without an environment instead of failing the caller");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("No Creatio environment was resolved")
			&& message.Contains("loads the package from the file system")
			&& message.Contains("--schema-name-prefix")));
	}

	[Test]
	[Description("Treats an environment without a URI the same as no environment at all.")]
	public void Resolve_ShouldWarnAndReturnEmpty_WhenEnvironmentHasNoUri() {
		// Arrange
		SchemaNamePrefixResolver sut = CreateSut(string.Empty);

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "a placeholder environment carries no Creatio address to read the setting from");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("No Creatio environment was resolved")));
	}

	[Test]
	[Description("Reports an empty configured SchemaNamePrefix as information rather than as a warning.")]
	public void Resolve_ShouldReturnEmptyWithoutWarning_WhenEnvironmentConfiguresNoPrefix() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix).Returns(string.Empty);
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "an empty setting is the environment's own answer, not a failure to read it");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Warns and generates without a prefix when the environment cannot be read.")]
	public void Resolve_ShouldWarnAndReturnEmpty_WhenEnvironmentReadFails() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix)
			.Returns(_ => throw new WebException("Cannot connect to the application"));
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "an unreachable environment must not block local package generation");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Network error reading the SchemaNamePrefix system setting")
			&& message.Contains("--schema-name-prefix")));
	}

	[TestCaseSource(nameof(ReadFailureCases))]
	[Description("Reports a failed environment read by category and never repeats the server's own text.")]
	public void Resolve_ShouldReportFailureByCategory_WhenEnvironmentReadThrows(Exception thrown,
		string expectedCategory) {
		// Arrange
		const string serverText = "<html>Login page, cookie=BPMCSRF secret-value</html>";
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix)
			.Returns(_ => throw thrown);
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		sut.Resolve(null);

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(expectedCategory) && !message.Contains(serverText)));
		thrown.Message.Should().Contain(serverText,
			because: "the test is only meaningful while the exception really carries the server's response");
	}

	private static IEnumerable<TestCaseData> ReadFailureCases() {
		const string serverText = "<html>Login page, cookie=BPMCSRF secret-value</html>";
		yield return new TestCaseData(new WebException(serverText),
			"Network error reading the SchemaNamePrefix system setting").SetName("network");
		yield return new TestCaseData(new UnauthorizedAccessException(serverText),
			"Authentication error reading the SchemaNamePrefix system setting").SetName("authentication");
		yield return new TestCaseData(new InvalidOperationException(serverText),
			"Failed to read the SchemaNamePrefix system setting").SetName("other");
	}

	[Test]
	[Description("Warns and ignores an environment prefix that cannot start a C# identifier.")]
	public void Resolve_ShouldWarnAndReturnEmpty_WhenEnvironmentPrefixIsNotAnIdentifier() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix).Returns("9-Usr");
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "an unusable prefix would produce a class name that cannot compile locally either");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("9-Usr")));
	}

	[TestCase("Usr", true)]
	[TestCase("ClioMcp_", true)]
	[TestCase("_", true)]
	[TestCase("", true)]
	[TestCase(null, true)]
	[TestCase("9x", false)]
	[TestCase("a-b", false)]
	[TestCase("a b", false)]
	[Description("Accepts only prefixes that keep a generated schema name a valid C# identifier.")]
	public void IsValidPrefix_ShouldMatchIdentifierRules_WhenPrefixIsChecked(string prefix, bool expected) {
		// Arrange
		// The rule is a pure predicate; no collaborator participates.

		// Act
		bool actual = SchemaNamePrefixResolver.IsValidPrefix(prefix);

		// Assert
		actual.Should().Be(expected,
			because: "the prefix is concatenated into a generated class name and a folder name");
	}

	[Test]
	[Description("Rejects construction without the collaborators the resolver reports through.")]
	public void Constructor_ShouldThrow_WhenRequiredCollaboratorIsMissing() {
		// Arrange
		EnvironmentSettings environmentSettings = new() {Uri = "http://localhost"};

		// Act
		Action act = () => _ = new SchemaNamePrefixResolver(environmentSettings, null, _logger);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a resolver without a sys-settings factory could never read the environment");
	}

	#endregion

}
