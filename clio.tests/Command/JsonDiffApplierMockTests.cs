using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Data-driven port of the client <c>json-applier.service.spec.ts</c> mock suite
/// (<c>json-applier-differ-mock.ts</c>, converted verbatim to <c>JsonDiffApplierMock.json</c>). Each case runs
/// <see cref="JsonDiffApplier.ApplyDiff"/> with a single diff/options pair and asserts deep-equality of the result
/// (or the expected exception), mirroring the spec's <c>testFunction</c> / <c>testExceptionFunction</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class JsonDiffApplierMockTests {

	private const string FixtureRelativePath = "Command/McpServer/Fixtures/JsonDiffApplierMock.json";

	private static JObject LoadFixture() {
		string path = Path.Combine(AppContext.BaseDirectory, FixtureRelativePath);
		File.Exists(path).Should().BeTrue(because: $"the mock fixture must be copied to '{path}'");
		return JObject.Parse(File.ReadAllText(path));
	}

	public static IEnumerable<TestCaseData> MockCases() {
		JObject fixture = LoadFixture();
		foreach (JProperty group in fixture.Properties()) {
			if (group.Value is not JArray cases) {
				continue;
			}
			int index = 0;
			foreach (JToken caseToken in cases) {
				var testCase = (JObject)caseToken;
				int currentIndex = index++;
				// Mirror iteratorFn: applier suite skips cases flagged skipInApplierTest (skipInDifferTest is differ-only).
				if (testCase.Value<bool?>("skipInApplierTest") == true) {
					continue;
				}
				string testName = testCase.Value<string>("testName") ?? "(unnamed)";
				yield return new TestCaseData(testCase)
					.SetName(SanitizeName($"{group.Name} #{currentIndex}: {testName}"));
			}
		}
	}

	[TestCaseSource(nameof(MockCases))]
	public void ApplyDiff_MatchesClientApplier(JObject testCase) {
		var source = (JArray)(testCase["sourceObject"] ?? new JArray());
		var diff = (JArray)(testCase["diff"] ?? new JArray());
		JsonApplierOperationsOptions options = ParseOptions(testCase["options"]);
		var applier = new JsonDiffApplier(disableApplyMoveIfIndirectParentMoved: false);

		string expectedException = testCase.Value<string>("expectedException");
		if (expectedException is not null) {
			Action act = () => applier.ApplyDiff(source, [diff], [options]);
			act.Should().Throw<JsonDiffApplierException>()
				.Which.Message.Should().Be(expectedException);
			return;
		}

		JToken expected = testCase["expectedResultObject"];
		JToken result = applier.ApplyDiff(source, [diff], [options]);
		JToken.DeepEquals(result, expected).Should().BeTrue(
			because: $"the C# applier must match the client result.\nexpected: {expected}\nactual:   {result}");
	}

	private static JsonApplierOperationsOptions ParseOptions(JToken options) =>
		options is JObject obj
			? new JsonApplierOperationsOptions {
				ApplyMoveIfIndirectParentMoved = obj.Value<bool?>("applyMoveIfIndirectParentMoved") ?? false,
			}
			: null;

	private static string SanitizeName(string name) {
		char[] cleaned = name.Select(c => "(),\"".IndexOf(c) >= 0 ? ' ' : c).ToArray();
		return new string(cleaned);
	}
}
