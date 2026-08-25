using Clio.Package;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class PackageInstallOptionsTests {

	[Test]
	[Description("Equal instances must return the same hash code, otherwise this type breaks the Equals/GetHashCode contract required by hash-based collections such as Dictionary and HashSet (sonar csharpsquid:S1206).")]
	public void GetHashCode_Should_ReturnSameValue_ForEqualInstances() {
		// Arrange
		PackageInstallOptions left = new() {
			InstallSqlScript = true,
			InstallPackageData = false,
			ContinueIfError = true,
			SkipConstraints = false,
			SkipValidateActions = true,
			ExecuteValidateActions = false
		};
		PackageInstallOptions right = new() {
			InstallSqlScript = true,
			InstallPackageData = false,
			ContinueIfError = true,
			SkipConstraints = false,
			SkipValidateActions = true,
			ExecuteValidateActions = false
		};

		// Act & Assert
		left.Equals(right).Should().BeTrue(because: "both instances share the same field values");
		left.GetHashCode().Should().Be(right.GetHashCode(),
			because: "Equals reports these instances as equal, so their hash codes must match or hash-based collections would misbehave");
	}

	[Test]
	[Description("Different instances are allowed (not required) to differ in hash code, but must not throw and must remain stable across calls.")]
	public void GetHashCode_Should_BeStable_AcrossRepeatedCalls() {
		// Arrange
		PackageInstallOptions options = new() { InstallSqlScript = false, ExecuteValidateActions = true };

		// Act
		int first = options.GetHashCode();
		int second = options.GetHashCode();

		// Assert
		second.Should().Be(first, because: "GetHashCode must return a stable value for the same, unmodified instance");
	}
}
