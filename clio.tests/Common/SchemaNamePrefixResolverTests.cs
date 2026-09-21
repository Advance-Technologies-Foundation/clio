using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
	private EnvironmentSettings _capturedFactoryArgument;
	private EnvironmentSettings _environmentSettings;
	private readonly ManualResetEventSlim _neverAnswers = new(false);

	#endregion

	#region Methods: Private

	private SchemaNamePrefixResolver CreateSut(string environmentUri) =>
		CreateSut(environmentUri, TimeSpan.FromSeconds(SchemaNamePrefixResolver.DefaultReadBudgetSeconds));

	private SchemaNamePrefixResolver CreateSut(string environmentUri, TimeSpan readBudget) {
		_environmentSettings = environmentUri is null
			? null
			: new EnvironmentSettings {Uri = environmentUri, Login = "Supervisor", Password = "Supervisor"};
		// The argument is CAPTURED rather than discarded on purpose: the whole point of issue #1309 is
		// that the prefix comes from the environment the package is destined for, and a factory stub that
		// ignores its argument would keep every test green if production code passed null instead.
		return new SchemaNamePrefixResolver(_environmentSettings, settings => {
			_capturedFactoryArgument = settings;
			return _sysSettingsManager;
		}, _logger, readBudget);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_logger = Substitute.For<ILogger>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_capturedFactoryArgument = null;
		_environmentSettings = null;
		// TearDown releases the gate to free the abandoned read thread, so every test must start with it
		// closed again or the budget test would see a read that answers instantly.
		_neverAnswers.Reset();
	}

	[TearDown]
	public void TearDown() {
		// Released here, not in the test: the budget test deliberately abandons the read, and an
		// unreleased gate would leave that thread blocked for the rest of the run.
		_neverAnswers.Set();
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
		_capturedFactoryArgument.Should().BeSameAs(_environmentSettings,
			because: "the prefix must come from the environment the package is destined for, not from "
				+ "whichever environment happens to be active");
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
	[Description("Honours an explicit empty prefix but warns, because an empty MCP string usually means 'not provided'.")]
	public void Resolve_ShouldReturnEmptyWithWarning_WhenExplicitPrefixIsEmpty() {
		// Arrange
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(string.Empty);

		// Assert
		prefix.Should().BeEmpty(because: "an explicit empty prefix asks for an unprefixed schema on purpose");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("explicitly empty schema-name prefix")
			&& message.Contains("Omit the argument")));
		_sysSettingsManager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
	}

	[Test]
	[Description("Rejects a whitespace-only requested prefix instead of silently generating an unprefixed schema.")]
	public void Resolve_ShouldThrow_WhenExplicitPrefixIsOnlyWhitespace() {
		// Arrange
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		Action act = () => sut.Resolve("  ");

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "the resolver owns the contract, so a caller reaching it directly must not be able to "
				+ "bypass the whitespace-typo rule and get the unprefixed schema it exists to prevent")
			.WithMessage($"{SchemaNamePrefixResolver.InvalidPrefixMessage}*");
	}

	[TestCase(null, true)]
	[TestCase("", true)]
	[TestCase("Usr", true)]
	[TestCase(" Usr ", true)]
	[TestCase(" ", false)]
	[TestCase("\t", false)]
	[TestCase("9x", false)]
	[Description("Folds the null, empty, whitespace-only and identifier rules for a requested prefix into one predicate.")]
	public void IsValidRequestedPrefix_ShouldFoldEveryRequestRule_WhenPrefixIsChecked(string requestedPrefix,
		bool expected) {
		// Arrange
		// The rule is a pure predicate; no collaborator participates.

		// Act
		bool actual = SchemaNamePrefixResolver.IsValidRequestedPrefix(requestedPrefix);

		// Assert
		actual.Should().Be(expected,
			because: "every caller of the resolver must get the same answer about what it will honour");
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
			&& message.Contains("file-system package-load path")
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
	[Description("Stops instead of degrading when the environment read is genuinely cancelled.")]
	public void Resolve_ShouldPropagate_WhenEnvironmentReadIsCancelled() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix)
			.Returns(_ => throw new OperationCanceledException("caller cancelled"));
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		Action act = () => sut.Resolve(null);

		// Assert
		act.Should().Throw<OperationCanceledException>(
			because: "a cancelled read that degraded to a warning would hand the caller a completed, "
				+ "mis-generated package instead of stopping");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Degrades on a transport timeout, which arrives as a cancellation but is not one.")]
	public void Resolve_ShouldWarnAndReturnEmpty_WhenEnvironmentReadTimesOutInTransport() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix)
			.Returns(_ => throw new TaskCanceledException("The request was canceled due to timeout."));
		SchemaNamePrefixResolver sut = CreateSut("http://localhost");

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "an unresponsive environment must degrade to an unprefixed package, not crash the "
				+ "command, even though its timeout derives from OperationCanceledException");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Network error reading the SchemaNamePrefix system setting")));
	}

	[Test]
	[Description("Gives up on a read that never answers, warns, and still returns an empty prefix.")]
	public void Resolve_ShouldWarnAndReturnEmpty_WhenEnvironmentReadExceedsTheBudget() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(SysSettingCodes.SchemaNamePrefix)
			.Returns(_ => {
				_neverAnswers.Wait();
				return "Usr";
			});
		SchemaNamePrefixResolver sut = CreateSut("http://localhost", TimeSpan.FromMilliseconds(200));

		// Act
		string prefix = sut.Resolve(null);

		// Assert
		prefix.Should().BeEmpty(
			because: "a host that accepts the connection and never answers must cost the caller the "
				+ "prefix, not the package");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Timed out reading the SchemaNamePrefix system setting")
			&& message.Contains("--schema-name-prefix")));
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
