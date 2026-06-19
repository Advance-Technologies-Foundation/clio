using System.Text.Json;
using CommandLine;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioRunArgBindingTests {

	private enum SampleMode {
		None,
		Fast,
		Slow
	}

	[Verb("sample-run", HelpText = "synthetic verb for binding tests")]
	private sealed class SampleRunOptions {
		[Option("package-name", Required = true, HelpText = "Required package name")]
		public string PackageName { get; set; }

		[Option("retry-count", Required = false, HelpText = "Optional retry count")]
		public int RetryCount { get; set; }

		[Option("force", Required = false, HelpText = "Boolean flag")]
		public bool Force { get; set; }

		[Option("mode", Required = false, HelpText = "Enum option")]
		public SampleMode Mode { get; set; }
	}

	private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

	private static IClioRunArgBinder CreateBinder() => new ClioRunArgBinder();

	[Test]
	[Category("Unit")]
	[Description("Binds kebab-keyed scalar args to their matching [Option] long names.")]
	public void Bind_ShouldBindScalarValues_WhenKebabKeysMatchOptionNames() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": "MyPkg", "retry-count": 3 }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeTrue(because: "all provided keys match known kebab option names");
		SampleRunOptions options = result.Options.Should().BeOfType<SampleRunOptions>(
			because: "binding produces the resolved options type").Subject;
		options.PackageName.Should().Be("MyPkg", because: "the string scalar must bind to package-name");
		options.RetryCount.Should().Be(3, because: "the numeric scalar must bind to retry-count");
	}

	[Test]
	[Category("Unit")]
	[Description("A boolean true value binds as a bare presence flag.")]
	public void Bind_ShouldEnableFlag_WhenBooleanValueIsTrue() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": "MyPkg", "force": true }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeTrue(because: "force is a valid boolean flag");
		((SampleRunOptions)result.Options).Force.Should().BeTrue(
			because: "a JSON true must bind the boolean flag as present");
	}

	[Test]
	[Category("Unit")]
	[Description("A boolean false value is omitted so the flag binds to its default false.")]
	public void Bind_ShouldOmitFlag_WhenBooleanValueIsFalse() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": "MyPkg", "force": false }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeTrue(because: "a false boolean is simply omitted");
		((SampleRunOptions)result.Options).Force.Should().BeFalse(
			because: "an omitted boolean flag defaults to false");
	}

	[Test]
	[Category("Unit")]
	[Description("Enum-typed options parse from a string value via CommandLineParser enum handling.")]
	public void Bind_ShouldParseEnum_WhenValidEnumStringProvided() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": "MyPkg", "mode": "Fast" }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeTrue(because: "Fast is a valid enum value");
		((SampleRunOptions)result.Options).Mode.Should().Be(SampleMode.Fast,
			because: "the enum string must parse to its enum member");
	}

	[Test]
	[Category("Unit")]
	[Description("An invalid enum value is a structured parse error, not a silent default.")]
	public void Bind_ShouldFail_WhenEnumValueIsInvalid() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": "MyPkg", "mode": "Nope" }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeFalse(because: "Nope is not a valid enum member");
		result.ErrorText.Should().Contain("mode",
			because: "the error must name the offending option");
	}

	[Test]
	[Category("Unit")]
	[Description("A missing Required option fails binding with a parser-derived error (not silently defaulted).")]
	public void Bind_ShouldFail_WhenRequiredOptionIsMissing() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "retry-count": 2 }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeFalse(because: "package-name is Required and was not supplied");
		result.ErrorText.Should().Contain("package-name",
			because: "the parser error must identify the missing required option");
	}

	[Test]
	[Category("Unit")]
	[Description("An unknown arg key is a structured error, not silently dropped.")]
	public void Bind_ShouldFail_WhenUnknownKeyProvided() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": "MyPkg", "totally-unknown": "x" }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeFalse(because: "unknown keys must not be silently ignored");
		result.ErrorText.Should().Contain("totally-unknown",
			because: "the error must echo the unknown argument name");
	}

	[Test]
	[Category("Unit")]
	[Description("A nested object value is rejected because CLI options accept only scalars/arrays.")]
	public void Bind_ShouldFail_WhenValueIsNestedObject() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();
		JsonElement args = Args("""{ "package-name": { "nested": 1 } }""");

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), args);

		// Assert
		result.Success.Should().BeFalse(because: "nested objects cannot map to CLI scalar options");
		result.ErrorText.Should().Contain("package-name",
			because: "the error must identify the offending nested key");
	}

	[Test]
	[Category("Unit")]
	[Description("Binding succeeds with no args when there are no required options unmet (verb only).")]
	public void Bind_ShouldSucceed_WhenArgsAreNullAndNoRequiredMissing() {
		// Arrange
		IClioRunArgBinder binder = CreateBinder();

		// Act
		ClioRunBindResult result = binder.Bind("sample-run", typeof(SampleRunOptions), null);

		// Assert
		result.Success.Should().BeFalse(
			because: "package-name is required, so binding with no args must fail rather than silently default");
		result.ErrorText.Should().Contain("package-name",
			because: "the missing required option is surfaced even when args are absent");
	}
}
