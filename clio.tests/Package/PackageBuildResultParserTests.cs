using Clio.Package;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Issue #1708: the build-response parser shared by <c>compile-package</c> and <c>compile-configuration</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageBuildResultParserTests {

	[TestCase("{\"success\":true,\"errors\":{}}", true)]
	[TestCase("{\"success\":false,\"errors\":[{\"line\":\"twelve\"}]}", false)]
	[TestCase("{\"success\":false,\"errorInfo\":[],\"buildResult\":\"x\",\"errors\":[]}", false)]
	[Description("An errors, errorInfo or buildResult value of an unexpected shape does not discard the verdict next to it. The old compile-configuration model typed errors as object, so the success field survived; a whole-body typed parse would read such a response as no result at all.")]
	public void TryParseResponse_ShouldKeepVerdict_WhenErrorsCannotBeRead(string body, bool expectedSuccess) {
		// Arrange

		// Act
		PackageBuildResult result = PackageBuildResultParser.TryParseResponse(body);

		// Assert
		result.Should().NotBeNull(because: "the response carries a readable success field");
		result.Success.Should().Be(expectedSuccess, because: "the verdict must come from the success field");
		result.Diagnostics.Should().BeEmpty(because: "diagnostics that cannot be read are reported as absent");
	}

	[Test]
	[Description("errorInfo.errorCode is read, so compile-configuration can keep printing '<errorCode>: <message>' for a failed build.")]
	public void TryParseResponse_ShouldReadErrorCode_WhenErrorInfoCarriesIt() {
		// Arrange
		const string body = "{\"success\":false,\"errorInfo\":{\"errorCode\":\"CompilationError\",\"message\":\"Build failed\"}}";

		// Act
		PackageBuildResult result = PackageBuildResultParser.TryParseResponse(body);

		// Assert
		result.ErrorCode.Should().Be("CompilationError", because: "errorInfo.errorCode is part of the verdict");
		result.ErrorMessage.Should().Be("Build failed", because: "errorInfo.message is part of the verdict");
	}

	[Test]
	[Description("A JSON body that is not an object with a success field still reads as no result.")]
	public void TryParseResponse_ShouldReturnNull_WhenBodyIsBrokenJson() {
		// Arrange
		const string body = "{\"success\":";

		// Act
		PackageBuildResult result = PackageBuildResultParser.TryParseResponse(body);

		// Assert
		result.Should().BeNull(because: "a body that cannot be read carries no verdict");
	}

	[Test]
	[Description("Property names are read case-insensitively, as the typed model compile-configuration used before did: a PascalCase failure body must read as a failure, not as a missing verdict.")]
	public void TryParseResponse_ShouldReadVerdict_WhenPropertyNamesArePascalCase() {
		// Arrange
		const string body = "{\"Success\":false,\"ErrorInfo\":{\"ErrorCode\":\"E1\",\"Message\":\"x\"}}";

		// Act
		PackageBuildResult result = PackageBuildResultParser.TryParseResponse(body);

		// Assert
		result.Should().NotBeNull(because: "a PascalCase success field is still a verdict");
		result.Success.Should().BeFalse(because: "the body reports a failed build");
		result.ErrorMessage.Should().Be("x", because: "ErrorInfo.Message is read whatever its casing");
		result.ErrorCode.Should().Be("E1", because: "ErrorInfo.ErrorCode is read whatever its casing");
	}

	[TestCase("{\"success\":", true)]
	[TestCase("{\"buildResult\":0}", false)]
	[TestCase("", false)]
	[Description("A broken or truncated body is told apart from well-formed JSON without a success field, which TryParseResponse reports the same way (null).")]
	public void IsMalformedJson_ShouldDetectBrokenBody(string body, bool expected) {
		// Arrange

		// Act
		bool malformed = PackageBuildResultParser.IsMalformedJson(body);

		// Assert
		malformed.Should().Be(expected, because: "only a body that cannot be parsed is malformed");
	}

	[TestCase(12, null, "(CS0006) in Foo.cs: missing")]
	[TestCase(null, null, "(CS0006) in Foo.cs: missing")]
	[TestCase(12, 3, "(CS0006) in Foo.cs at (12,3): missing")]
	[Description("A position is rendered only when both the line and the column were supplied, so a diagnostic without one is not shown at a (0,0) location that does not exist.")]
	public void Format_ShouldOmitPosition_WhenLineOrColumnIsMissing(int? line, int? column, string expected) {
		// Arrange
		PackageBuildDiagnostic diagnostic = new("CS0006", "missing", "Foo.cs", line, column, false);

		// Act
		string rendered = diagnostic.ToString();

		// Assert
		rendered.Should().Be(expected, because: "only a position Creatio reported may be shown");
	}

}
