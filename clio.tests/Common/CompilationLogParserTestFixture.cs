using System;
using System.Text.Json;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompilationLogParserTestFixture {

	
	private CompilationLogParser _sut;
	private static readonly Func<string, string> NormalizeLineEndings =
		text => text.Replace("\r\n", "\n").Replace("\r", "\n");
	
	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	[Description("Formats Creatio compilation-result JSON with distinct error and warning totals.")]
	public void ParseCreatioCompilationLog_ShouldMatchExpectedOutput_WhenPayloadIsValid(
		string input, string expectedOutput){

		// Arrange
		_sut = new CompilationLogParser();
		
		string desiredOutputContent = System.IO.File.ReadAllText(expectedOutput);
		string inputContent = System.IO.File.ReadAllText(input);
		
		
		// Act
		string result = _sut.ParseCreatioCompilationLog(inputContent);

		// Assert
		NormalizeLineEndings(result).Should().Be(NormalizeLineEndings(desiredOutputContent).TrimEnd(),
			because:"the parsed output should match the expected output");
		
	}

	[Test]
	[Description("Preserves warning severity when formatting and deserializing Creatio compilation diagnostics.")]
	public void ParseCreatioCompilationLog_ShouldPreserveWarningSeverity_WhenPayloadContainsWarning(){
		// Arrange
		_sut = new CompilationLogParser();
		const string input = """
			{"errors":[{"line":7,"column":3,"errorNumber":"CS0168","errorText":"Variable is declared but never used","warning":true,"fileName":"Example.cs"}],"buildResult":0,"success":true}
			""";

		// Act
		string formatted = _sut.ParseCreatioCompilationLog(input);
		CreatioCompilationLogResponse response = _sut.DeserializeCreatioCompilationLog(input);

		// Assert
		formatted.Should().Contain("Example.cs(7,3): Warning CS0168",
			because: "the compiler warning flag must remain visible in human-readable CLI output");
		formatted.Should().EndWith("Succeeded: True. Errors: 0. Warnings: 1.",
			because: "warning-only successful builds must not be reported as containing errors");
		response.errors.Should().ContainSingle(diagnostic => diagnostic.warning,
			because: "the typed MCP mapping needs the original warning flag");
	}

	[Test]
	[Description("Rejects JSON error envelopes that do not contain Creatio's compilation-result fields.")]
	public void DeserializeCreatioCompilationLog_ShouldThrow_WhenPayloadHasUnexpectedShape(){
		// Arrange
		_sut = new CompilationLogParser();
		const string input = """{"message":"Functionality is disabled"}""";

		// Act
		Action act = () => _sut.DeserializeCreatioCompilationLog(input);

		// Assert
		act.Should().Throw<JsonException>()
			.WithMessage("*Expected errors, buildResult, and success fields*",
				because: "an endpoint error object must not be reported as a successfully retrieved compilation result");
	}

}
