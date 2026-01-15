using System;
using System.Linq;
using Clio.Command;
using Clio.Requests.Validators;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NUnit.Framework;

namespace Clio.Tests.Validators;

public class ExternalLinkOptionsValidatorTestCase{
	#region Fields: Private

	private ExternalLinkOptionsValidator _sut;

	#endregion

	#region Methods: Public

	[Test]
	[Category("Unit")]
	[Description("Validates that strings which are not valid URIs are rejected with appropriate error details")]
	[TestCase("random_string")]
	[TestCase("more random text")]
	[TestCase("clio://IISScannerRequest /?return=count")]
	[TestCase("clio:// IISScannerRequest/?return=count")]
	[TestCase("clio:// IISScannerRequest /?return=count")]
	[TestCase("clio://  /?return=count")]
	public void ExternalLinkOptionsValidator_ShouldValidate_As_InValid_NotAUri(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};
		var expected = new {
			ErrorCode = "10",
			ErrorMessage = "Value is not in the correct format",
			Severity = Severity.Error,
			AttemptedValue = content
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeFalse(because: "the content is not a valid URI format");
		validationResults.Errors.Should().HaveCount(1, because: "there should be exactly one validation error");
		validationResults.Errors.FirstOrDefault()!.Severity.Should().Be(expected.Severity, because: "the error should be marked as an Error severity");
		validationResults.Errors.FirstOrDefault()!.ErrorMessage.Should().Be(expected.ErrorMessage, because: "the error message should indicate incorrect format");
		validationResults.Errors.FirstOrDefault()!.ErrorCode.Should().Be(expected.ErrorCode, because: "the error code should be '10' for format errors");
		validationResults.Errors.FirstOrDefault()!.AttemptedValue.Should().Be(expected.AttemptedValue, because: "the attempted value should match the input content");
	}

	[Test]
	[Category("Unit")]
	[Description("Validates that properly formatted clio:// URIs with valid commands are accepted")]
	[TestCase("clio://IISScannerRequest/?return=count")]
	public void ExternalLinkOptionsValidator_ShouldValidate_As_Valid(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeTrue(because: "the content is a valid clio URI with proper format");
	}


	[Test]
	[Category("Unit")]
	[Description("Validates that empty, whitespace, or null content strings are rejected")]
	[TestCase("")]
	[TestCase(" ")]
	[TestCase(null)]
	public void ExternalLinkValidator_ShouldValidate_As_InValid_EmptyContent(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeFalse(because: "empty, whitespace, or null content is not a valid external link");
	}


	[Test]
	[Category("Unit")]
	[Description("Validates that URIs with non-existent commands are rejected with command not found error")]
	[TestCase("clio://randomCommand/?return=count")]
	[TestCase("clio://345634/?return=count")]
	public void ExternalLinkValidator_ShouldValidate_As_InValid_WhenCommandNotFound(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		Uri.TryCreate(content, UriKind.Absolute, out Uri uriFromString);
		string commandName = uriFromString?.Host;
		var expected = new {
			ErrorCode = "50",
			ErrorMessage = $"Command <{commandName}> not found",
			Severity = Severity.Error,
			AttemptedValue = commandName
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeFalse(because: "the command specified in the URI does not exist");
		validationResults.Errors.Should().HaveCount(1, because: "there should be exactly one validation error for the missing command");
		validationResults.Errors.FirstOrDefault()!.Severity.Should().Be(expected.Severity, because: "command not found should be an Error severity");
		validationResults.Errors.FirstOrDefault()!.ErrorMessage.Should().Be(expected.ErrorMessage, because: "the error message should indicate which command was not found");
		validationResults.Errors.FirstOrDefault()!.ErrorCode.Should().Be(expected.ErrorCode, because: "the error code should be '50' for command not found errors");
		validationResults.Errors.FirstOrDefault()!.AttemptedValue.Should().Be(expected.AttemptedValue, because: "the attempted value should be the invalid command name");
	}


	[Test]
	[Category("Unit")]
	[Description("Validates that URIs not starting with clio:// scheme are rejected")]
	[TestCase("c://IISScannerRequest/?return=count")]
	[TestCase("cl://IISScannerRequest/?return=count")]
	[TestCase("cli://IISScannerRequest/?return=count")]
	[TestCase("cli://")]
	public void ExternalLinkValidator_ShouldValidate_As_InValid_WhenDoesNotStartWithClio(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		var expected = new {
			ErrorCode = "20",
			ErrorMessage = "Value has to start with clio://",
			Severity = Severity.Error,
			AttemptedValue = content
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeFalse(because: "the URI does not start with the required clio:// scheme");
		validationResults.Errors.Should().HaveCount(1, because: "there should be exactly one validation error for invalid scheme");
		validationResults.Errors.FirstOrDefault()!.Severity.Should().Be(expected.Severity, because: "invalid scheme should be an Error severity");
		validationResults.Errors.FirstOrDefault()!.ErrorMessage.Should().Be(expected.ErrorMessage, because: "the error message should indicate the required clio:// scheme");
		validationResults.Errors.FirstOrDefault()!.ErrorCode.Should().Be(expected.ErrorCode, because: "the error code should be '20' for scheme validation errors");
		validationResults.Errors.FirstOrDefault()!.AttemptedValue.Should().Be(expected.AttemptedValue, because: "the attempted value should be the invalid URI");
	}


	[Test]
	[Category("Unit")]
	[Description("Validates that URIs with existing commands are accepted")]
	[TestCase("clio://IISScannerRequest/?return=count")]
	[TestCase("clio://Restart/?return=count")]
	[TestCase("clio://OpenUrl/?return=count")]
	public void ExternalLinkValidator_ShouldValidate_As_Valid_WhenCommandFound(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeTrue(because: "the URI contains a valid command that exists in the system");
	}
	
	[Test]
	[Category("Unit")]
	[Description("Validates that URIs with malformed query strings (empty keys or values) are rejected")]
	[TestCase("clio://IISScannerRequest/?return=", "return", " ")]
	[TestCase("clio://IISScannerRequest/?=count", " ", "count")]
	public void ExternalLinkValidator_ShouldValidate_As_InValid_QueryIsWrong(string content, string key, string val) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		Uri.TryCreate(content, UriKind.Absolute, out Uri uriFromString);
		var expected = new {
			ErrorCode = "50",
			ErrorMessage = $"Query not in correct format key is '{key}' when value '{val}'",
			Severity = Severity.Error,
			AttemptedValue = uriFromString
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);
		
		// Assert
		validationResults.IsValid.Should().BeFalse(because: "the query string contains empty keys or values which is invalid");
		validationResults.Errors.Should().HaveCount(1, because: "there should be exactly one validation error for the malformed query");
		validationResults.Errors.FirstOrDefault()!.Severity.Should().Be(expected.Severity, because: "malformed query should be an Error severity");
		validationResults.Errors.FirstOrDefault()!.ErrorMessage.Should().StartWith("Query not in correct format key is", because: "the error message should indicate the query format issue");
		validationResults.Errors.FirstOrDefault()!.ErrorCode.Should().Be(expected.ErrorCode, because: "the error code should be '50' for query format errors");
		validationResults.Errors.FirstOrDefault()!.AttemptedValue.Should().Be(expected.AttemptedValue, because: "the attempted value should be the URI with the invalid query");
	}

	[Test]
	[Category("Unit")]
	[Description("Validates that URIs with properly formatted query strings (including multiple parameters) are accepted")]
	[TestCase("clio://IISScannerRequest/?return=count")]
	[TestCase("clio://IISScannerRequest/?return=count&a=b")]
	[TestCase("clio://IISScannerRequest/?return=count&a=b&c=d")]
	[TestCase("clio://IISScannerRequest/?return=count&a=b&c=d&c=d,d")]
	public void ExternalLinkValidator_ShouldValidate_As_Valid_QueryIsCorrect(string content) {
		// Arrange
		ExternalLinkOptions request = new() {
			Content = content
		};

		// Act
		ValidationResult validationResults = _sut.Validate(request);

		// Assert
		validationResults.IsValid.Should().BeTrue(because: "the query string is properly formatted with valid key-value pairs");
	}

	[SetUp]
	public void Init() {
		_sut = new ExternalLinkOptionsValidator();
	}

	#endregion
}
