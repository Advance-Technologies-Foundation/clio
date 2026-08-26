using Clio.Common.IIS;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class WindowsIISSiteDetectorTruncateForLogTests {

	[Test]
	[Description("Returns null instead of throwing when the value is null, matching the null-conditional debug-log idiom it replaced.")]
	public void TruncateForLog_Should_ReturnNull_WhenValueIsNull() {
		// Act
		string result = WindowsIISSiteDetector.TruncateForLog(null, 200);

		// Assert
		result.Should().BeNull(because: "a null value has nothing to log and must not throw");
	}

	[Test]
	[Description("Returns the value unchanged when it is shorter than maxLength.")]
	public void TruncateForLog_Should_ReturnValueUnchanged_WhenShorterThanMaxLength() {
		// Act
		string result = WindowsIISSiteDetector.TruncateForLog("short value", 200);

		// Assert
		result.Should().Be("short value", because: "a value shorter than the cap needs no truncation");
	}

	[Test]
	[Description("Truncates to exactly maxLength characters when the value is longer, confirming the removed '?? 0' fallback was genuinely dead code (sonar csharpsquid:S2583) since this path is only reached when value is already known non-null.")]
	public void TruncateForLog_Should_TruncateToMaxLength_WhenValueIsLonger() {
		// Arrange
		string longValue = new('x', 300);

		// Act
		string result = WindowsIISSiteDetector.TruncateForLog(longValue, 200);

		// Assert
		result.Should().HaveLength(200, because: "the debug log line must be capped at maxLength characters");
	}
}
