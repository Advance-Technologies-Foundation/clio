using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Verifies the colour math reproduces the committed golden in
/// <c>Theming/Fixtures/color-math-parity.json</c> exactly, hex-for-hex, across a broad,
/// adversarially-seeded set of inputs (generateScale, deriveSecondary, accentCandidates). A coverage
/// guard keeps the input set broad.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class ColorMathParityTests {

	private const string FixtureRelativePath = "Theming/Fixtures/color-math-parity.json";

	/// <summary>The tolerance applied when comparing generated colours to the golden — zero, i.e. exact hex equality.</summary>
	private const int BitExactToleranceUlps = 0;

	[Test]
	[Description("Every fixture entry's generateScale output must equal the golden hex-for-hex.")]
	public void GenerateScale_ShouldMatchTsGolden_ForEveryParityFixtureEntry() {
		// Arrange
		IReadOnlyList<ParityEntry> entries = LoadFixture();
		List<string> mismatches = new();

		// Act
		foreach (ParityEntry entry in entries) {
			IReadOnlyDictionary<int, string> actual = PaletteGenerator.GenerateScale(entry.Hex);
			foreach (KeyValuePair<string, string> golden in entry.GenerateScale) {
				int step = int.Parse(golden.Key, CultureInfo.InvariantCulture);
				if (!actual.TryGetValue(step, out string actualHex) || actualHex != golden.Value) {
					mismatches.Add($"{entry.Hex} [{entry.Seed}] step {step}: expected {golden.Value}, got {(actualHex ?? "<missing>")}");
				}
			}
		}

		// Assert
		mismatches.Should().BeEmpty(
			because: "the generated scale must equal the golden exactly; divergences:\n" + string.Join("\n", mismatches));
	}

	[Test]
	[Description("Every fixture entry's deriveSecondary output must equal the golden exactly.")]
	public void DeriveSecondary_ShouldMatchTsGolden_ForEveryParityFixtureEntry() {
		// Arrange
		IReadOnlyList<ParityEntry> entries = LoadFixture();
		List<string> mismatches = new();

		// Act
		foreach (ParityEntry entry in entries) {
			string actual = PaletteGenerator.DeriveSecondary(entry.Hex);
			if (actual != entry.DeriveSecondary) {
				mismatches.Add($"{entry.Hex} [{entry.Seed}]: expected {entry.DeriveSecondary}, got {actual}");
			}
		}

		// Assert
		mismatches.Should().BeEmpty(
			because: "C# deriveSecondary must match the golden exactly; divergences:\n" + string.Join("\n", mismatches));
	}

	[Test]
	[Description("Every fixture entry's accentCandidates (hex + offset, in order) must equal the golden exactly.")]
	public void GenerateAccentCandidates_ShouldMatchTsGolden_ForEveryParityFixtureEntry() {
		// Arrange
		IReadOnlyList<ParityEntry> entries = LoadFixture();
		List<string> mismatches = new();

		// Act
		foreach (ParityEntry entry in entries) {
			IReadOnlyList<AccentCandidate> actual = PaletteGenerator.GenerateAccentCandidates(entry.Hex);
			if (actual.Count != entry.AccentCandidates.Count) {
				mismatches.Add($"{entry.Hex} [{entry.Seed}]: expected {entry.AccentCandidates.Count} candidates, got {actual.Count}");
				continue;
			}
			for (int i = 0; i < actual.Count; i++) {
				if (actual[i].Hex != entry.AccentCandidates[i].Hex || actual[i].Offset != entry.AccentCandidates[i].Offset) {
					mismatches.Add($"{entry.Hex} [{entry.Seed}] candidate {i}: expected {entry.AccentCandidates[i].Hex}@{entry.AccentCandidates[i].Offset}, got {actual[i].Hex}@{actual[i].Offset}");
				}
			}
		}

		// Assert
		mismatches.Should().BeEmpty(
			because: "C# accentCandidates must match the golden exactly (stable order + hue offsets); divergences:\n" + string.Join("\n", mismatches));
	}

	[Test]
	[Description("The fixture must stay broad and adversarially seeded: a regression to a thin/random set must fail this coverage guard.")]
	public void ParityFixture_ShouldContainAdversarialBoundarySeeds_WhenLoaded() {
		// Arrange
		IReadOnlyList<ParityEntry> entries = LoadFixture();
		HashSet<string> seedFamilies = entries.Select(e => e.Seed).ToHashSet();

		// Act / Assert
		entries.Count.Should().BeGreaterThanOrEqualTo(50,
			because: "the parity gate needs a broad input set, not a handful of points");

		string[] requiredFamilies = {
			"calibration-primary", "extreme-black", "extreme-white", "gray-mid",
			"pure-rgb", "yellow-mode", "dark-mode", "light-mode", "hue-spread", "vivid"
		};
		foreach (string family in requiredFamilies) {
			seedFamilies.Should().Contain(family,
				because: $"the '{family}' adversarial seed family must survive — dropping it would blind the gate to that precision-sensitive path");
		}

		entries.Should().Contain(e => e.Hex == "#004fd6",
			because: "#004fd6 is the calibration primary every other value is calibrated against");
		entries.Should().Contain(e => e.Hex == "#ffd700",
			because: "a yellow-mode colour exercises the extra sqrt/pow libm paths in the darker ramp");
	}

	[Test]
	[Description("The parity comparison uses exact equality — zero tolerance.")]
	public void ParityGate_ShouldBeBitExact_WithZeroTolerance() {
		// The parity tests above compare with exact string equality. This test pins the recorded
		// decision: the bit-exact branch was taken (Math.Pow/Math.Cbrt matched V8 on the adversarial
		// fixture). Any future ULP drift must raise this constant with a justification — not silently.
		BitExactToleranceUlps.Should().Be(0,
			because: "the parity gate outcome is bit-exact; widening the tolerance requires a deliberate, justified change");
	}

	private static IReadOnlyList<ParityEntry> LoadFixture() {
		string fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, FixtureRelativePath);
		File.Exists(fixturePath).Should().BeTrue(
			because: $"the parity fixture must be copied to the test output at '{fixturePath}'");
		string json = File.ReadAllText(fixturePath);
		List<ParityEntry> entries = JsonSerializer.Deserialize<List<ParityEntry>>(json);
		entries.Should().NotBeNullOrEmpty(because: "the parity fixture must deserialise into entries");
		return entries;
	}

	private sealed record ParityEntry {
		[JsonPropertyName("hex")] public string Hex { get; init; }
		[JsonPropertyName("seed")] public string Seed { get; init; }
		[JsonPropertyName("generateScale")] public Dictionary<string, string> GenerateScale { get; init; }
		[JsonPropertyName("deriveSecondary")] public string DeriveSecondary { get; init; }
		[JsonPropertyName("accentCandidates")] public List<ParityAccent> AccentCandidates { get; init; }
	}

	private sealed record ParityAccent {
		[JsonPropertyName("hex")] public string Hex { get; init; }
		[JsonPropertyName("offset")] public int Offset { get; init; }
	}
}
