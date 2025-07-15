using System.Collections.Generic;
using System.Linq;
using Autofac;
using Clio.Command;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NUnit.Framework;

namespace Clio.Tests.Validators
{
    [TestFixture]
    [Category("UnitTests")]
    public class TurnFarmModeOptionsValidatorTests : BaseClioModuleTests
    {
        private TurnFarmModeOptionsValidator _validator;

        public override void Setup()
        {
            base.Setup();
            _validator = Container.Resolve<TurnFarmModeOptionsValidator>();
        }

        [Test]
        public void Validate_ReturnsValid_WhenAllRequiredFieldsProvided()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "1",
                InstanceId = "Node1",
                Environment = "TestEnv"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Test]
        public void Validate_ReturnsValid_WhenSitePathProvidedInsteadOfEnvironment()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "1",
                InstanceId = "Node1",
                SitePath = "C:\\TestSite"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Test]
        public void Validate_ReturnsError_WhenTenantIdIsEmpty()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "",
                InstanceId = "Node1",
                Environment = "TestEnv"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors.First().ErrorMessage.Should().Be("Tenant ID is required");
            result.Errors.First().PropertyName.Should().Be("TenantId");
        }

        [Test]
        public void Validate_ReturnsError_WhenTenantIdIsNull()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = null,
                InstanceId = "Node1",
                Environment = "TestEnv"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors.First().ErrorMessage.Should().Be("Tenant ID is required");
        }

        [Test]
        public void Validate_ReturnsError_WhenInstanceIdIsEmpty()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "1",
                InstanceId = "",
                Environment = "TestEnv"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors.First().ErrorMessage.Should().Be("Instance ID is required");
            result.Errors.First().PropertyName.Should().Be("InstanceId");
        }

        [Test]
        public void Validate_ReturnsError_WhenInstanceIdIsNull()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "1",
                InstanceId = null,
                Environment = "TestEnv"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors.First().ErrorMessage.Should().Be("Instance ID is required");
        }

        [Test]
        public void Validate_ReturnsError_WhenBothEnvironmentAndSitePathAreEmpty()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "1",
                InstanceId = "Node1",
                Environment = "",
                SitePath = ""
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors.First().ErrorMessage.Should().Be("Either environment name or site path must be provided");
        }

        [Test]
        public void Validate_ReturnsError_WhenBothEnvironmentAndSitePathAreNull()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "1",
                InstanceId = "Node1",
                Environment = null,
                SitePath = null
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors.First().ErrorMessage.Should().Be("Either environment name or site path must be provided");
        }

        [Test]
        public void Validate_ReturnsMultipleErrors_WhenMultipleFieldsAreInvalid()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                TenantId = "",
                InstanceId = "",
                Environment = "",
                SitePath = ""
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(3);
            
            var errorMessages = result.Errors.Select(e => e.ErrorMessage).ToList();
            errorMessages.Should().Contain("Tenant ID is required");
            errorMessages.Should().Contain("Instance ID is required");
            errorMessages.Should().Contain("Either environment name or site path must be provided");
        }

        [Test]
        public void Validate_ReturnsValid_WhenDefaultValuesAreUsed()
        {
            // Arrange
            var options = new TurnFarmModeOptions
            {
                // TenantId has default value "1" set by constructor
                // InstanceId has default value "AUTO" set by constructor
                Environment = "TestEnv"
            };

            // Act
            ValidationResult result = _validator.Validate(options);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }
    }
}