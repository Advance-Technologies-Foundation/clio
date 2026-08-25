using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Text;
using Clio.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CustomLoggingConfiguratorTests : BaseClioModuleTests {
	private const string PackageName = "UsrCodexVirtualEntity";
	private const string LoggerName = "UsrCodexVirtualEntityApp";
	private ICustomLoggingConfigurator _configurator;

	public override void Setup() {
		base.Setup();
		_configurator = Container.GetRequiredService<ICustomLoggingConfigurator>();
	}

	[Test]
	[Description("Adds the deterministic rule and target to a Net8 installation while preserving unrelated text.")]
	public void Configure_ShouldAddExpectedEntries_WhenNet8LayoutIsValid() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false, includeUtf8Bom: true);
		byte[] originalRules = FileSystem.File.ReadAllBytes(installation.RulesPath);
		string expectedRules = ReadText(installation.RulesPath).Replace(
			"\t\t<logger name=\"*\"",
			"\t\t<logger name=\"UsrCodexVirtualEntityApp\" writeTo=\"usrCodexVirtualEntityAppender\" minlevel=\"Info\" final=\"true\" />\r\n\t\t<logger name=\"*\"",
			StringComparison.Ordinal);
		string expectedTargets = ReadText(installation.TargetsPath).Replace(
			"\t\t<target name=\"file\"",
			"\t\t<target name=\"usrCodexVirtualEntityAppender\" xsi:type=\"File\" layout=\"${DefaultLayout}\" fileName=\"${TodayLogPath}/UsrCodexVirtualEntity.log\" />\r\n\t\t<target name=\"file\"",
			StringComparison.Ordinal);

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "info", null);

		// Assert
		result.Success.Should().BeTrue(because: $"both valid NLog documents can be updated safely; error: {result.ErrorMessage}");
		result.Changed.Should().BeTrue(because: "the package route was absent before the command ran");
		result.LoggerName.Should().Be(LoggerName, because: "the generated constant is the routing source of truth");
		result.TargetName.Should().Be("usrCodexVirtualEntityAppender",
			because: "the target name must be deterministic and omit the generated App suffix");
		result.LogPath.Should().Be("${TodayLogPath}/UsrCodexVirtualEntity.log",
			because: "the default log file is derived from the logger name");
		string rules = ReadText(installation.RulesPath);
		rules.Should().Contain("<!-- keep-rules-comment -->",
			because: "unrelated user-authored configuration must remain present");
		rules.IndexOf("name=\"UsrCodexVirtualEntityApp\"", StringComparison.Ordinal).Should().BeLessThan(
			rules.IndexOf("name=\"*\"", StringComparison.Ordinal),
			because: "the package-specific rule must precede the default catch-all rule");
		ReadText(installation.TargetsPath).Should().Contain(
			"fileName=\"${TodayLogPath}/UsrCodexVirtualEntity.log\"",
			because: "the dedicated target must write beneath Creatio's daily log directory");
		byte[] updatedRules = FileSystem.File.ReadAllBytes(installation.RulesPath);
		updatedRules.Take(3).Should().Equal([0xEF, 0xBB, 0xBF],
			because: "the UTF-8 byte-order mark must survive a targeted insertion");
		ReadText(installation.RulesPath).Should().NotMatchRegex("(?<!\\r)\\n",
			because: "a CRLF document must not gain mixed newline styles");
		updatedRules.Should().NotEqual(originalRules,
			because: "the first run must add the absent rule while preserving unrelated source text");
		ReadText(installation.RulesPath).Should().Be(expectedRules,
			because: "the first run must insert only the deterministic logger element into the original source");
		ReadText(installation.TargetsPath).Should().Be(expectedTargets,
			because: "the first run must insert only the deterministic target element into the original source");
	}

	[Test]
	[Description("Finds NLog files beneath Terrasoft.WebApp for a .NET Framework installation.")]
	public void Configure_ShouldUseTerrasoftWebApp_WhenNetFrameworkLayoutIsValid() {
		// Arrange
		Installation installation = AddInstallation(netFramework: true);

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Warn", "custom-package");

		// Assert
		result.Success.Should().BeTrue(because: $"the supported NetFramework layout places the application under Terrasoft.WebApp; error: {result.ErrorMessage}");
		result.LogPath.Should().Be("${TodayLogPath}/custom-package.log",
			because: "a simple file-name override receives the required log extension");
		ReadText(installation.RulesPath).Should().Contain("minlevel=\"Warn\"",
			because: "the canonical requested minimum level must be written to the logger rule");
	}

	[Test]
	[Description("Leaves both files byte-identical when the exact custom route already exists.")]
	public void Configure_ShouldBeIdempotent_WhenRunTwice() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false, includeUtf8Bom: true);
		CustomLoggingConfigurationResult first = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);
		byte[] rulesAfterFirstRun = FileSystem.File.ReadAllBytes(installation.RulesPath);
		byte[] targetsAfterFirstRun = FileSystem.File.ReadAllBytes(installation.TargetsPath);

		// Act
		CustomLoggingConfigurationResult second = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		first.Success.Should().BeTrue(because: $"the first run establishes the route used by the idempotency check; error: {first.ErrorMessage}");
		second.Success.Should().BeTrue(because: "an exact existing route is a successful no-op");
		second.Changed.Should().BeFalse(because: "an exact existing rule and target must not be duplicated or rewritten");
		FileSystem.File.ReadAllBytes(installation.RulesPath).Should().Equal(rulesAfterFirstRun,
			because: "an idempotent rerun must preserve every original byte including the BOM");
		FileSystem.File.ReadAllBytes(installation.TargetsPath).Should().Equal(targetsAfterFirstRun,
			because: "an idempotent rerun must preserve the target document byte-for-byte");
	}

	[Test]
	[Description("Validates both XML documents before writing either one.")]
	public void Configure_ShouldNotChangeRules_WhenTargetsXmlIsMalformed() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false);
		byte[] originalRules = FileSystem.File.ReadAllBytes(installation.RulesPath);
		FileSystem.File.WriteAllText(installation.TargetsPath, "<nlog><targets>");

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeFalse(because: "malformed target XML makes the two-file change unsafe");
		result.ErrorMessage.Should().NotBeNullOrWhiteSpace(because: "the caller needs actionable XML diagnostics");
		FileSystem.File.ReadAllBytes(installation.RulesPath).Should().Equal(originalRules,
			because: "both documents must validate before the first file is committed");
	}

	[Test]
	[Description("Rejects an existing same-name logger with different attributes without changing the target file.")]
	public void Configure_ShouldRejectConflict_WithoutChangingTargets() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false,
			extraRule: "\t\t<logger name=\"UsrCodexVirtualEntityApp\" writeTo=\"different\" minlevel=\"Debug\" final=\"false\" />\r\n");
		byte[] originalTargets = FileSystem.File.ReadAllBytes(installation.TargetsPath);

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeFalse(because: "silently replacing a user-authored same-name rule would lose intent");
		result.ErrorMessage.Should().Contain("conflicting", because: "the failure must identify the name collision");
		FileSystem.File.ReadAllBytes(installation.TargetsPath).Should().Equal(originalTargets,
			because: "a rule conflict must be detected before either document is committed");
	}

	[Test]
	[Description("Rejects an exact package logger placed after the catch-all because NLog would never route to it.")]
	public void Configure_ShouldRejectExactLogger_WhenItFollowsCatchAll() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false);
		string rules = "<nlog><rules><logger name=\"*\" writeTo=\"file\" minlevel=\"Info\" />"
			+ "<logger name=\"UsrCodexVirtualEntityApp\" writeTo=\"usrCodexVirtualEntityAppender\" minlevel=\"Info\" final=\"true\" />"
			+ "</rules></nlog>";
		FileSystem.File.WriteAllText(installation.RulesPath, rules);
		byte[] originalTargets = FileSystem.File.ReadAllBytes(installation.TargetsPath);

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeFalse(because: "an exact but unreachable logger is not a valid idempotent result");
		result.ErrorMessage.Should().Contain("after the default catch-all",
			because: "the operator needs the precise ordering defect to repair");
		FileSystem.File.ReadAllBytes(installation.TargetsPath).Should().Equal(originalTargets,
			because: "ordering validation must complete before either document is committed");
	}

	[Test]
	[Description("Inserts valid elements into compact single-line NLog XML without treating prior markup as indentation.")]
	public void Configure_ShouldInsertValidElements_WhenXmlIsCompact() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false);
		FileSystem.File.WriteAllText(installation.RulesPath,
			"<nlog><rules><logger name=\"*\" writeTo=\"file\" minlevel=\"Info\" /></rules></nlog>");
		FileSystem.File.WriteAllText(installation.TargetsPath,
			"<nlog xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><variable name=\"TodayLogPath\" value=\"Logs\" /><variable name=\"DefaultLayout\" value=\"${message}\" /><targets><target name=\"file\" xsi:type=\"File\" layout=\"${DefaultLayout}\" fileName=\"${TodayLogPath}/Common.log\" /></targets></nlog>");

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeTrue(because: $"compact XML is structurally valid and has safe anchors; error: {result.ErrorMessage}");
		ReadText(installation.RulesPath).Should().Contain("<rules><logger name=\"UsrCodexVirtualEntityApp\"",
			because: "the new element must be inserted directly before the existing compact anchor");
	}

	[Test]
	[Description("Places the exact package rule before an earlier final wildcard rule.")]
	public void Configure_ShouldInsertBeforeWildcard_WhenEarlierRuleIsFinal() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false,
			extraRule: "\t\t<logger name=\"Usr*\" writeTo=\"file\" minlevel=\"Info\" final=\"true\" />\r\n");

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeTrue(because: $"the exact rule can be inserted before all existing rules; error: {result.ErrorMessage}");
		string rules = ReadText(installation.RulesPath);
		rules.IndexOf($"name=\"{LoggerName}\"", StringComparison.Ordinal).Should().BeLessThan(
			rules.IndexOf("name=\"Usr*\"", StringComparison.Ordinal),
			because: "the exact rule must run before a terminating wildcard rule");
	}

	[TestCase("DefaultLayout")]
	[TestCase("TodayLogPath")]
	[Description("Rejects a target whose required NLog variable is missing.")]
	public void Configure_ShouldFail_WhenRequiredVariableIsMissing(string variableName) {
		// Arrange
		Installation installation = AddInstallation(netFramework: false);
		string targets = ReadText(installation.TargetsPath);
		string variable = variableName == "DefaultLayout"
			? "\t<variable name=\"DefaultLayout\" value=\"${message}\" />\r\n"
			: "\t<variable name=\"TodayLogPath\" value=\"Logs\" />\r\n";
		FileSystem.File.WriteAllText(installation.TargetsPath, targets.Replace(variable, string.Empty, StringComparison.Ordinal));
		byte[] originalRules = FileSystem.File.ReadAllBytes(installation.RulesPath);

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeFalse(because: "the generated target depends on both variables");
		result.ErrorMessage.Should().Contain(variableName, because: "the error must identify the missing variable");
		FileSystem.File.ReadAllBytes(installation.RulesPath).Should().Equal(originalRules,
			because: "both candidates must validate before either original is changed");
	}

	[TestCase("../Package", "Info", null)]
	[TestCase(PackageName, "Verbose", null)]
	[TestCase(PackageName, "Info", "../outside.log")]
	[TestCase(PackageName, "Info", "NUL.log")]
	[Description("The public configurator validates unsafe inputs even when invoked outside the CLI validator.")]
	public void Configure_ShouldRejectInput_WhenInputIsUnsafe(string packageName, string minLevel, string fileName) {
		// Arrange
		Installation installation = AddInstallation(netFramework: false);
		byte[] originalRules = FileSystem.File.ReadAllBytes(installation.RulesPath);
		byte[] originalTargets = FileSystem.File.ReadAllBytes(installation.TargetsPath);

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, packageName, minLevel, fileName);

		// Assert
		result.Success.Should().BeFalse(because: "the behavior boundary must not rely on a particular caller for validation");
		FileSystem.File.ReadAllBytes(installation.RulesPath).Should().Equal(originalRules,
			because: "invalid input must be rejected before filesystem mutation");
		FileSystem.File.ReadAllBytes(installation.TargetsPath).Should().Equal(originalTargets,
			because: "invalid input must be rejected before filesystem mutation");
	}

	[Test]
	[Description("Reports a missing generated logger constant as an actionable package error.")]
	public void Configure_ShouldFail_WhenLoggerConstantIsMissing() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false, constantsContent: "internal static class Constants { }");

		// Act
		CustomLoggingConfigurationResult result = _configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeFalse(because: "there is no safe logger name to configure");
		result.ErrorMessage.Should().Contain("LoggerName", because: "the diagnostic must name the missing generated contract");
	}

	[Test]
	[Description("Restores both originals when writing the second file fails.")]
	public void Configure_ShouldRollbackBothFiles_WhenSecondWriteFails() {
		// Arrange
		Installation installation = AddInstallation(netFramework: false);
		byte[] originalRules = FileSystem.File.ReadAllBytes(installation.RulesPath);
		byte[] originalTargets = FileSystem.File.ReadAllBytes(installation.TargetsPath);
		IFile faultingFile = WriteFaultProxy.Create(FileSystem.File, failOnWrite: 2);
		IFileSystem faultingFileSystem = Substitute.For<IFileSystem>();
		faultingFileSystem.File.Returns(faultingFile);
		faultingFileSystem.Path.Returns(FileSystem.Path);
		IServiceProvider container = new BindingsModule(faultingFileSystem).Register(
			EnvironmentSettings,
			applyBootstrapRepairs: false);
		ICustomLoggingConfigurator configurator = container.GetRequiredService<ICustomLoggingConfigurator>();

		// Act
		CustomLoggingConfigurationResult result = configurator.Configure(
			installation.EnvironmentRoot, PackageName, "Info", null);

		// Assert
		result.Success.Should().BeFalse(because: "the second write was deliberately interrupted");
		result.ErrorMessage.Should().Contain("restored", because: "the failure must confirm the rollback outcome");
		FileSystem.File.ReadAllBytes(installation.RulesPath).Should().Equal(originalRules,
			because: "the file whose replacement failed must retain its original content");
		FileSystem.File.ReadAllBytes(installation.TargetsPath).Should().Equal(originalTargets,
			because: "the already-replaced first file must be restored from its backup");
	}

	private Installation AddInstallation(
		bool netFramework,
		bool includeUtf8Bom = false,
		string extraRule = "",
		string constantsContent = null) {
		string environmentRoot = FileSystem.Path.Combine(FileSystem.Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N"));
		string applicationRoot = netFramework
			? FileSystem.Path.Combine(environmentRoot, "Terrasoft.WebApp")
			: environmentRoot;
		string rulesPath = FileSystem.Path.Combine(applicationRoot, "nlog.config");
		string targetsPath = FileSystem.Path.Combine(applicationRoot, "nlog.targets.config");
		string constantsPath = FileSystem.Path.Combine(
			applicationRoot, "Terrasoft.Configuration", "Pkg", PackageName, "Files", "src", "cs", "Constants.cs");
		string rules = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
			+ "<nlog xmlns=\"http://www.nlog-project.org/schemas/NLog.xsd\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n"
			+ "\t<!-- keep-rules-comment -->\r\n\t<rules>\r\n" + extraRule
			+ "\t\t<logger name=\"*\" writeTo=\"file\" minlevel=\"Info\" />\r\n\t</rules>\r\n</nlog>\r\n";
		string targets = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
			+ "<nlog xmlns=\"http://www.nlog-project.org/schemas/NLog.xsd\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n"
			+ "\t<!-- keep-target-comment -->\r\n\t<variable name=\"TodayLogPath\" value=\"Logs\" />\r\n"
			+ "\t<variable name=\"DefaultLayout\" value=\"${message}\" />\r\n\t<targets>\r\n"
			+ "\t\t<target name=\"file\" xsi:type=\"File\" layout=\"${DefaultLayout}\" fileName=\"${TodayLogPath}/Common.log\" />\r\n"
			+ "\t</targets>\r\n</nlog>\r\n";
		Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: includeUtf8Bom);
		FileSystem.AddFile(rulesPath, new System.IO.Abstractions.TestingHelpers.MockFileData(Encode(encoding, rules)));
		FileSystem.AddFile(targetsPath, new System.IO.Abstractions.TestingHelpers.MockFileData(Encode(encoding, targets)));
		FileSystem.AddFile(constantsPath, new System.IO.Abstractions.TestingHelpers.MockFileData(
			constantsContent ?? $"internal static class Constants {{\r\n\tinternal const string LoggerName = \"{LoggerName}\";\r\n}}"));
		return new Installation(environmentRoot, rulesPath, targetsPath);
	}

	private string ReadText(string path) => Encoding.UTF8.GetString(FileSystem.File.ReadAllBytes(path)).TrimStart('\uFEFF');

	private static byte[] Encode(Encoding encoding, string content) =>
		encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();

	private sealed record Installation(string EnvironmentRoot, string RulesPath, string TargetsPath);

	private class WriteFaultProxy : DispatchProxy {
		private IFile _inner;
		private int _failOnWrite;
		private int _writeCount;

		internal static IFile Create(IFile inner, int failOnWrite) {
			IFile proxy = DispatchProxy.Create<IFile, WriteFaultProxy>();
			WriteFaultProxy state = (WriteFaultProxy)(object)proxy;
			state._inner = inner;
			state._failOnWrite = failOnWrite;
			return proxy;
		}

		protected override object Invoke(MethodInfo targetMethod, object[] args) {
			if (targetMethod.Name == nameof(IFile.WriteAllText) && ++_writeCount == _failOnWrite) {
				throw new IOException("Injected second-file write failure.");
			}
			return targetMethod.Invoke(_inner, args);
		}
	}
}
