using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ResourceStringHelperTests {
	[Test]
	[Description("CleanAndMerge preserves existing custom resources and registers missing body and explicit resources")]
	public void CleanAndMerge_WhenBodyAndExplicitResourcesContainMissingKeys_PreservesAndRegistersExpectedEntries() {
		JArray localizableStrings = [
			new JObject {
				["name"] = "UsrExistingTitle",
				["values"] = new JArray {
					new JObject {
						["cultureName"] = "en-US",
						["value"] = "Existing title"
					}
				}
			},
			new JObject {
				["name"] = "Caption",
				["values"] = new JArray {
					new JObject {
						["cultureName"] = "en-US",
						["value"] = "Base caption"
					}
				}
			}
		];
		Dictionary<string, string> resources = new() {
			["UsrTitle"] = "Explicit title",
			["UsrDetached"] = "Detached title"
		};
		HashSet<string> bodyKeys = ["UsrExistingTitle", "UsrTitle", "Caption"];
		(JArray cleaned, List<string> registered) result = ResourceStringHelper.CleanAndMerge(
			localizableStrings,
			resources,
			bodyKeys);
		result.cleaned.Should().HaveCount(3,
			because: "only existing custom keys and newly registered custom resources should remain after cleanup");
		result.cleaned[0]!["name"]!.ToString().Should().Be("UsrExistingTitle",
			because: "existing custom localizable strings should be preserved");
		result.cleaned[1]!["name"]!.ToString().Should().Be("UsrTitle",
			because: "missing body keys with explicit resources should be registered");
		result.cleaned[2]!["name"]!.ToString().Should().Be("UsrDetached",
			because: "explicit custom resources outside the body should still be added");
		result.registered.Should().Equal(["UsrTitle", "UsrDetached"],
			because: "the helper should report only the keys that were newly registered");
	}

	[Test]
	[Description("DeriveCaption inserts spaces between camel-case words and removes the Usr prefix")]
	public void DeriveCaption_WhenKeyContainsUsrPrefixAndCamelCase_ReturnsReadableCaption() {
		const string key = "UsrAccountPrimaryContact_caption";
		string caption = ResourceStringHelper.DeriveCaption(key);
		caption.Should().Be("Account Primary Contact",
			because: "generated resource captions should stay human-readable for automatically registered Usr keys");
	}
}
