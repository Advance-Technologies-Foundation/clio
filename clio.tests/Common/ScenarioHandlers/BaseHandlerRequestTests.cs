using System.Collections.Generic;
using Clio.Common.ScenarioHandlers;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;

namespace Clio.Tests.Common.ScenarioHandlers;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal class BaseHandlerRequestTests {
    [Test]
    [Description("Returns the stored string value when the requested argument key is present.")]
    public void GetRequired_ShouldReturnValue_WhenKeyIsPresent() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["k"] = "value" }
        };

        // Act
        string result = request.GetRequired("k");

        // Assert
        result.Should().Be("value",
            because: "the accessor returns the raw value stored under the requested key");
    }

    [Test]
    [Description("Throws a ValidationException when the requested argument key is missing.")]
    public void GetRequired_ShouldThrowValidationException_WhenKeyIsMissing() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["other"] = "value" }
        };

        // Act
        System.Action act = () => request.GetRequired("missing");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "a required argument that is absent from the bag is a contract violation");
    }

    [Test]
    [Description("Throws a ValidationException when the requested argument value is empty.")]
    public void GetRequired_ShouldThrowValidationException_WhenValueIsEmpty() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["k"] = string.Empty }
        };

        // Act
        System.Action act = () => request.GetRequired("k");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "an empty value does not satisfy the required-and-non-empty contract");
    }

    [Test]
    [Description("Throws a ValidationException when the requested argument value is null.")]
    public void GetRequired_ShouldThrowValidationException_WhenValueIsNull() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["k"] = null }
        };

        // Act
        System.Action act = () => request.GetRequired("k");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "a null value does not satisfy the required-and-non-empty contract");
    }

    [Test]
    [Description("Throws a ValidationException when the requested argument value is whitespace-only.")]
    public void GetRequired_ShouldThrowValidationException_WhenValueIsWhitespaceOnly() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["k"] = "   " }
        };

        // Act
        System.Action act = () => request.GetRequired("k");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "a whitespace-only value does not satisfy the required contract, matching the FluentValidation IsNullOrWhiteSpace rule");
    }

    [Test]
    [Description("Parses a valid integer string into an int via the typed accessor.")]
    public void GetRequiredOfInt_ShouldParseValue_WhenValueIsValidInteger() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["port"] = "8080" }
        };

        // Act
        int result = request.GetRequired<int>("port");

        // Assert
        result.Should().Be(8080,
            because: "the typed accessor converts the stored string to the requested numeric type");
    }

    [Test]
    [Description("Throws a ValidationException when an int argument value cannot be parsed.")]
    public void GetRequiredOfInt_ShouldThrowValidationException_WhenValueIsNotInteger() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["port"] = "abc" }
        };

        // Act
        System.Action act = () => request.GetRequired<int>("port");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "an unparseable numeric value is surfaced as a validation failure rather than a raw format exception");
    }

    [Test]
    [Description("Parses valid boolean strings into a bool via the typed accessor.")]
    public void GetRequiredOfBool_ShouldParseValue_WhenValueIsValidBoolean() {
        // Arrange
        BaseHandlerRequest trueRequest = new() {
            Arguments = new Dictionary<string, string> { ["flag"] = "True" }
        };
        BaseHandlerRequest falseRequest = new() {
            Arguments = new Dictionary<string, string> { ["flag"] = "False" }
        };

        // Act
        bool trueResult = trueRequest.GetRequired<bool>("flag");
        bool falseResult = falseRequest.GetRequired<bool>("flag");

        // Assert
        trueResult.Should().BeTrue(
            because: "the typed accessor converts the stored 'True' string to a boolean");
        falseResult.Should().BeFalse(
            because: "the typed accessor converts the stored 'False' string to a boolean");
    }

    [Test]
    [Description("Throws a ValidationException when a bool argument value cannot be parsed.")]
    public void GetRequiredOfBool_ShouldThrowValidationException_WhenValueIsNotBoolean() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = new Dictionary<string, string> { ["flag"] = "notabool" }
        };

        // Act
        System.Action act = () => request.GetRequired<bool>("flag");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "an unparseable boolean value is surfaced as a validation failure rather than a raw format exception");
    }

    [Test]
    [Description("Throws a ValidationException instead of a null reference exception when the arguments bag is null.")]
    public void GetRequiredOfInt_ShouldThrowValidationException_WhenArgumentsIsNull() {
        // Arrange
        BaseHandlerRequest request = new() {
            Arguments = null
        };

        // Act
        System.Action act = () => request.GetRequired<int>("port");

        // Assert
        act.Should().Throw<ValidationException>(
            because: "a null arguments bag must be reported as a validation failure rather than dereferenced");
    }
}
