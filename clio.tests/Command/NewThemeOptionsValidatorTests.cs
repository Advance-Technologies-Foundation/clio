using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class NewThemeOptionsValidatorTests {

	#region Fields: Private

	private NewThemeOptionsValidator _validator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_validator = new NewThemeOptionsValidator();
	}

	[Test]
	[Description("Valid CSS class name and package pass validation.")]
	public void Validate_ShouldPass_WhenOptionsValid() {
		// Arrange
		NewThemeOptions options = new() { CssClassName = "acme-dark-theme", PackageName = "UsrThemes" };

		// Act
		bool isValid = _validator.Validate(options).IsValid;

		// Assert
		isValid.Should().BeTrue("because a valid class name and package satisfy the rules");
	}

	[TestCase("1bad")]
	[TestCase("bad class")]
	[TestCase("-leading")]
	[Description("An invalid CSS class name fails validation.")]
	public void Validate_ShouldFail_WhenCssClassNameInvalid(string cssClassName) {
		// Arrange
		NewThemeOptions options = new() { CssClassName = cssClassName, PackageName = "UsrThemes" };

		// Act
		bool isValid = _validator.Validate(options).IsValid;

		// Assert
		isValid.Should().BeFalse("because cssClassName must match ^[A-Za-z][A-Za-z0-9_-]*$");
	}

	[Test]
	[Description("A missing package fails validation.")]
	public void Validate_ShouldFail_WhenPackageMissing() {
		// Arrange
		NewThemeOptions options = new() { CssClassName = "acme-dark-theme" };

		// Act
		bool isValid = _validator.Validate(options).IsValid;

		// Assert
		isValid.Should().BeFalse("because the package name is required");
	}

	[Test]
	[Description("An explicit id that violates the pattern fails validation.")]
	public void Validate_ShouldFail_WhenExplicitIdInvalid() {
		// Arrange
		NewThemeOptions options = new() {
			CssClassName = "acme-dark-theme", PackageName = "UsrThemes", Id = "bad id!"
		};

		// Act
		bool isValid = _validator.Validate(options).IsValid;

		// Assert
		isValid.Should().BeFalse("because an explicit id must match ^[A-Za-z0-9_-]+$");
	}

	#endregion

}
