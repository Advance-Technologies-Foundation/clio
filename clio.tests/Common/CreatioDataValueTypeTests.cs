using System;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CreatioDataValueTypeTests {
	[TestCase("6b6b74e2-820d-490e-a017-2b73d4ccf2b0", "Integer")]
	[TestCase("d21e9ef4-c064-4012-b286-fa1a8171da44", "DateTime")]
	[Description("Resolves canonical Creatio data value type metadata by UId.")]
	public void TryGet_ShouldReturnCanonicalType_WhenUIdIsKnown(string uId, string expectedName) {
		// Arrange
		Guid value = Guid.Parse(uId);

		// Act
		bool found = CreatioDataValueType.TryGet(value, out CreatioDataValueTypeInfo info);

		// Assert
		found.Should().BeTrue(because: "known Creatio type UIds must be available to semantic consumers");
		info.Name.Should().Be(expectedName, because: "the canonical registry owns the type name mapping");
	}
}
