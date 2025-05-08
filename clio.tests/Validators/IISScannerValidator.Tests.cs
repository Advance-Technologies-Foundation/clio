using System.Linq;
using Clio.Requests;
using Clio.Requests.Validators;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NUnit.Framework;

namespace Clio.Tests.Validators;

public class IISScannerRequestValidatorTestCase
{

    #region Setup/Teardown

    [SetUp]
    public void Init()
    {
        _sut = new ISSScannerValidator();
    }

    #endregion

    #region Fields: Private

    private ISSScannerValidator _sut;

    #endregion

    [Test]
    [Category("Unit")]
    [TestCase("clio://IISScannerRequest/?a=b")]
    [TestCase("clio://IISScannerRequest/?returnnn=a1")]
    public void ISSScannerValidator_ShouldValidate_As_InValid_When_ReturnIsMissing(string content)
    {
        //Arange 
        IISScannerRequest request = new()
        {
            Content = content
        };

        var expected = new
        {
            ErrorCode = "ARG001",
            ErrorMessage = "Return type cannot be empty",
            Severity = Severity.Error,
            AttemptedValue = content
        };
        //Act
        ValidationResult validationResults = _sut.Validate(request);
        validationResults.IsValid.Should().BeFalse();
        validationResults.Errors.Should().HaveCount(1);
        validationResults.Errors.FirstOrDefault()!.Severity.Should().Be(expected.Severity);
        validationResults.Errors.FirstOrDefault()!.ErrorMessage.Should().Be(expected.ErrorMessage);
        validationResults.Errors.FirstOrDefault()!.ErrorCode.Should().Be(expected.ErrorCode);
        validationResults.Errors.FirstOrDefault()!.AttemptedValue.Should().Be(expected.AttemptedValue);
    }

    [Test]
    [Category("Unit")]
    [TestCase("clio://IISScannerRequest/?return=a19")]
    [TestCase("clio://IISScannerRequest/?return=1")]
    [TestCase("clio://IISScannerRequest/?return=g")]
    public void ISSScannerValidator_ShouldValidate_As_InValid_WhenReturnParam_Is_Incorrect(string content)
    {
        //Arange 
        IISScannerRequest request = new()
        {
            Content = content
        };

        string[] allowedValues = new[]
        {
            "count", "details", "registerall", "remote"
        };
        var expected = new
        {
            ErrorCode = "ARG002",
            ErrorMessage = $"Return type must be one of {string.Join(", ", allowedValues)}",
            Severity = Severity.Error,
            AttemptedValue = content
        };
        //Act
        ValidationResult validationResults = _sut.Validate(request);
        validationResults.IsValid.Should().BeFalse();
        validationResults.Errors.Should().HaveCount(1);
        validationResults.Errors.FirstOrDefault()!.Severity.Should().Be(expected.Severity);
        validationResults.Errors.FirstOrDefault()!.ErrorMessage.Should().Be(expected.ErrorMessage);
        validationResults.Errors.FirstOrDefault()!.ErrorCode.Should().Be(expected.ErrorCode);
        validationResults.Errors.FirstOrDefault()!.AttemptedValue.Should().Be(expected.AttemptedValue);
    }

    [Test]
    [Category("Unit")]
    [TestCase("clio://IISScannerRequest/?return=count")]
    [TestCase("clio://IISScannerRequest/?return=details")]
    public void ISSScannerValidator_ShouldValidate_As_Valid(string content)
    {
        //Arange 
        IISScannerRequest request = new()
        {
            Content = content
        };
        //Act
        ValidationResult validationResults = _sut.Validate(request);
        validationResults.IsValid.Should().BeTrue();
    }

}
