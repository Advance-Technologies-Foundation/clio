using System;
using System.Threading;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class KeyedSemaphoreTests {

	[Test]
	[Description("GetOrAdd returns the same SemaphoreSlim instance for repeated calls with the same key so callers serialize against one gate.")]
	public void GetOrAdd_ShouldReturnSameInstance_WhenKeyIsRepeated() {
		// Arrange
		KeyedSemaphore sut = new();

		// Act
		SemaphoreSlim first = sut.GetOrAdd("envapp");
		SemaphoreSlim second = sut.GetOrAdd("envapp");

		// Assert
		second.Should().BeSameAs(first,
			because: "the same key must map to one shared per-key gate so all callers on that key serialize");
	}

	[Test]
	[Description("GetOrAdd returns distinct SemaphoreSlim instances for different keys so unrelated keys run fully in parallel.")]
	public void GetOrAdd_ShouldReturnDistinctInstances_WhenKeysDiffer() {
		// Arrange
		KeyedSemaphore sut = new();

		// Act
		SemaphoreSlim a = sut.GetOrAdd("envappA");
		SemaphoreSlim b = sut.GetOrAdd("envappB");

		// Assert
		b.Should().NotBeSameAs(a,
			because: "different keys must get independent gates so unrelated work is not serialized against each other");
	}

	[Test]
	[Description("The backing dictionary uses StringComparer.Ordinal, so keys differing only by case map to distinct gates (callers own any case normalization).")]
	public void GetOrAdd_ShouldTreatDifferentCaseAsDistinct_WhenKeysDifferOnlyByCase() {
		// Arrange
		KeyedSemaphore sut = new();

		// Act
		SemaphoreSlim lower = sut.GetOrAdd("envapp");
		SemaphoreSlim upper = sut.GetOrAdd("ENVAPP");

		// Assert
		upper.Should().NotBeSameAs(lower,
			because: "the registry is Ordinal (case-sensitive): case-insensitivity is the caller's responsibility, not this primitive's");
	}

	[Test]
	[Description("Entries are never evicted: after a Release the same key still returns the same gate and continues to serialize a second concurrent acquire.")]
	public void GetOrAdd_ShouldKeepEntry_AfterReleaseSoSameKeyStillSerializes() {
		// Arrange
		KeyedSemaphore sut = new();
		SemaphoreSlim gate = sut.GetOrAdd("envapp");

		// Act
		gate.Wait();
		gate.Release();
		SemaphoreSlim afterRelease = sut.GetOrAdd("envapp");

		// Assert
		afterRelease.Should().BeSameAs(gate,
			because: "the entry survives Release (never-evict), so a later acquire on the same key uses the same gate");
		afterRelease.Wait(0).Should().BeTrue(
			because: "after the prior Release the gate is free, so the next acquire succeeds");
		sut.GetOrAdd("envapp").Wait(0).Should().BeFalse(
			because: "the retained gate still serializes: a second concurrent acquire on the same key is blocked while the first holds it");
		afterRelease.Release();
	}

	[Test]
	[Description("GetOrAdd throws ArgumentNullException for a null key rather than creating a gate under a null key.")]
	public void GetOrAdd_ShouldThrowArgumentNullException_WhenKeyIsNull() {
		// Arrange
		KeyedSemaphore sut = new();

		// Act
		Action act = () => sut.GetOrAdd(null!);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a null key is a caller error and must fail fast, not silently register a null-keyed gate");
	}
}
