using System;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Guards the descriptor's <c>ModifiedOnUtc</c> conversion.
/// </summary>
/// <remarks>
/// This field decides whether Creatio rewrites a package's <c>SysPackage</c> row at all:
/// <c>PackageStorageComposer.ApplySourcePackageChanges</c> compares it and sets
/// <c>IsPackageDescriptorChanged</c>, without which <c>PackageDBStorage.SavePackageDescriptor</c> returns
/// early and the package's recorded version and metadata stay as they were. A conversion that is off by the
/// local UTC offset therefore does not merely write a cosmetically wrong timestamp — it writes a wrong value
/// into the field the platform makes that decision from.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class PackageDescriptorTests {

	/// <summary>Parses the <c>/Date(unix-ms)/</c> wire form back to a UTC instant.</summary>
	private static DateTimeOffset ParseModifiedOnUtc(string value) {
		string digits = value.Replace("/Date(", string.Empty).Replace(")/", string.Empty);
		return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(digits));
	}

	[Test]
	[Description("Converting a Local DateTime yields the same UTC instant, so a caller passing DateTime.Now records the moment it actually happened.")]
	public void ConvertToModifiedOnUtc_ShouldPreserveTheInstant_WhenInputIsLocal() {
		// Arrange
		DateTime localNow = DateTime.Now;

		// Act
		DateTimeOffset converted = ParseModifiedOnUtc(PackageDescriptor.ConvertToModifiedOnUtc(localNow));

		// Assert
		converted.Should().BeCloseTo(new DateTimeOffset(localNow), TimeSpan.FromSeconds(1),
			because: "the conversion must map a local wall-clock reading onto the instant it denotes; "
				+ "set-pkg-version, PackageCreator and Workspace all pass DateTime.Now");
	}

	[Test]
	[Description("Converting a Utc DateTime yields the same UTC instant, which used to be off by the local offset because truncating to whole seconds dropped DateTimeKind.")]
	public void ConvertToModifiedOnUtc_ShouldPreserveTheInstant_WhenInputIsUtc() {
		// Arrange
		DateTime utcNow = DateTime.UtcNow;

		// Act
		DateTimeOffset converted = ParseModifiedOnUtc(PackageDescriptor.ConvertToModifiedOnUtc(utcNow));

		// Assert
		converted.Should().BeCloseTo(new DateTimeOffset(utcNow, TimeSpan.Zero), TimeSpan.FromSeconds(1),
			because: "a Utc input must not be shifted again. The component DateTime constructor used for "
				+ "second-truncation returns Unspecified unless the kind is passed, and ToUniversalTime "
				+ "treats Unspecified as local — so this input was silently moved back by the local offset. "
				+ "SchemaBuilder passes DateTime.UtcNow and was affected");
	}

	[Test]
	[Description("Serialises a known UTC instant to a hard-coded unix-millisecond literal, so the conversion is pinned by arithmetic rather than by the host's time zone.")]
	public void ConvertToModifiedOnUtc_ShouldMatchAKnownLiteral() {
		// Arrange
		// 2026-08-05 19:13:02 UTC. Chosen with non-zero seconds so the truncation is observable, and with
		// milliseconds that must be dropped.
		DateTime instant = new(2026, 8, 5, 19, 13, 2, 456, DateTimeKind.Utc);

		// Act
		string serialised = PackageDescriptor.ConvertToModifiedOnUtc(instant);

		// Assert
		serialised.Should().Be("/Date(1785957182000)/",
			because: "every OTHER test in this fixture derives its expectation from the ambient zone, so all of "
				+ "them go VACUOUS on a host whose UTC offset is zero — a Linux or macOS box, a container, a "
				+ "re-imaged runner. On such a host ToLocalTime() is the identity and DateTime.UtcNow equals "
				+ "DateTime.Now, so reverting the DateTimeKind fix this fixture exists to pin would leave them "
				+ "all green. Today's runners happen to have a non-zero offset and nothing said so. This "
				+ "assertion is arithmetic and holds on any host: the literal encodes both the instant and the "
				+ "millisecond truncation. The value was derived INDEPENDENTLY (Python "
				+ "datetime(2026,8,5,19,13,2,tzinfo=utc).timestamp()*1000), not copied from this code's own "
				+ "output — pasting the observed value would make the test agree with whatever the code does. "
				+ "It caught a mistake on the way in: the first literal written here was off by 1.3 days");
	}

	[Test]
	[Description("Both input kinds agree on the same instant, which is the property that matters: the caller's choice of Now or UtcNow must not change what is recorded.")]
	public void ConvertToModifiedOnUtc_ShouldAgree_AcrossInputKinds() {
		// Arrange
		// This test can only distinguish anything where local time differs from UTC; on a zero-offset host
		// ToLocalTime() is the identity and it degenerates to x == x. Stated rather than hidden, and covered
		// unconditionally by ConvertToModifiedOnUtc_ShouldMatchAKnownLiteral above.
		Assume.That(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 5, 19, 13, 2, DateTimeKind.Utc)),
			Is.Not.EqualTo(TimeSpan.Zero),
			"the host's UTC offset is zero, so the two spellings are the same value and this test cannot fail");
		DateTime instant = new(2026, 8, 5, 19, 13, 2, DateTimeKind.Utc);

		// Act
		string fromUtc = PackageDescriptor.ConvertToModifiedOnUtc(instant);
		string fromLocal = PackageDescriptor.ConvertToModifiedOnUtc(instant.ToLocalTime());

		// Assert
		fromUtc.Should().Be(fromLocal,
			because: "the two spellings denote one instant, so they must serialize identically; before the "
				+ "kind was preserved they differed by the local offset and only one of them was right");
	}
}
