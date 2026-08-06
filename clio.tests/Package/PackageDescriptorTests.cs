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
	[Description("Both input kinds agree on the same instant, which is the property that matters: the caller's choice of Now or UtcNow must not change what is recorded.")]
	public void ConvertToModifiedOnUtc_ShouldAgree_AcrossInputKinds() {
		// Arrange
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
