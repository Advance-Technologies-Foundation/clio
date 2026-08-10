using System;
using System.Linq;
using System.Reflection;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Guards the ABSENCE of a bundled-package version constant.
/// </summary>
/// <remarks>
/// An unusual thing to test, and the reason is that nothing else can. Removing that constant was the central
/// decision of <c>spec/adr/adr-bundled-package-version-source-of-truth.md</c>: a bundled archive is a content
/// file copied to the build output, so it can be replaced without recompiling, which makes a version compiled
/// into this assembly a claim about bytes it may no longer describe — and during development it was exactly
/// that, with three build outputs holding three different archives under one constant.
/// <para>
/// Reintroducing it would break nothing that any test observes. It would compile, every existing test would
/// pass, and the damage would appear later as an environment told it is behind a version clio does not ship.
/// The requirements document still described the deleted design for a while after it was deleted, which is
/// how a reader would come to add it back in good faith. Prose cannot stop that; this can — within its reach,
/// which is this TYPE. A version parked on another type, or under a name without "Version" in it, would still
/// pass; the guard is aimed at the specific regression the ADR forbids, not at every conceivable spelling.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class BundledPackagesTests {

	[Test]
	[Description("BundledPackages must expose no version-shaped member for a bundled package. The version clio ships is read from the archive's own descriptor by IBundledPackageCatalog; a constant here would be a compile-time claim about a content file that can change without a rebuild. Matched by NAME rather than by a known list, so a differently-spelled reintroduction — ProcessBuilderVersion, BundledProcessBuilderVersion, ProcessBuilderPackageVersion — is caught too.")]
	public void BundledPackages_ShouldExposeNoVersionConstant() {
		// Arrange
		// cliogate's [RequiresPackage] floors are version-shaped literals that legitimately live at their
		// declaration sites, not here, so nothing in this type is expected to survive the filter. The filter is
		// on the MEMBER NAME: a version read from an archive never needs a name in this assembly.
		MemberInfo[] members = typeof(BundledPackages)
			.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

		// Act
		string[] versionShaped = members
			.Where(member => member.Name.Contains("Version", StringComparison.OrdinalIgnoreCase))
			.Select(member => member.Name)
			.ToArray();

		// Assert
		versionShaped.Should().BeEmpty(
			because: "the version a bundled package ships is a property of the ARCHIVE, read at runtime by "
				+ "IBundledPackageCatalog. A constant here cannot be kept true — the archive is a content file "
				+ "copied to the build output, so it changes without a rebuild — and the failure mode is silent: "
				+ "environments get told they are behind a version clio does not carry. See "
				+ "spec/adr/adr-bundled-package-version-source-of-truth.md before deleting this test");
	}

}
