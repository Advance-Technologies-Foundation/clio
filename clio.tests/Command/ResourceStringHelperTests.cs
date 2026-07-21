using System;
using System.Collections.Generic;
using System.Linq;
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
	[Description("CleanAndMerge preserves all existing resources (base and custom) and registers missing body and explicit resources")]
	public void CleanAndMerge_WhenBodyAndExplicitResourcesContainMissingKeys_PreservesAndRegistersExpectedEntries() {
		// Arrange
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

		// Act
		(JArray cleaned, List<string> registered) result = ResourceStringHelper.CleanAndMerge(
			localizableStrings,
			resources,
			bodyKeys);

		// Assert
		result.cleaned.Should().HaveCount(4,
			because: "all existing keys (base and custom) plus newly registered resources should be preserved");
		result.cleaned[0]!["name"]!.ToString().Should().Be("UsrExistingTitle",
			because: "existing custom localizable strings should be preserved");
		result.cleaned[1]!["name"]!.ToString().Should().Be("Caption",
			because: "existing base localizable strings should be preserved");
		result.cleaned[2]!["name"]!.ToString().Should().Be("UsrTitle",
			because: "missing body keys with explicit resources should be registered");
		result.cleaned[3]!["name"]!.ToString().Should().Be("UsrDetached",
			because: "explicit custom resources outside the body should still be added");
		result.registered.Should().Equal(["UsrTitle", "UsrDetached"],
			because: "the helper should report only the keys that were newly registered");
	}

	[Test]
	[Description("CleanAndMerge preserves base platform strings like SaveButton, CancelButton when they exist in schema")]
	public void CleanAndMerge_WhenSchemaHasBasePlatformStrings_PreservesAllOfThem() {
		// Arrange
		JArray localizableStrings = [
			new JObject {
				["name"] = "SaveButton",
				["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "" } }
			},
			new JObject {
				["name"] = "CancelButton",
				["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "" } }
			},
			new JObject {
				["name"] = "GeneralInfoTab_caption",
				["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "" } }
			},
			new JObject {
				["name"] = "UsrName",
				["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "Name" } }
			}
		];
		HashSet<string> bodyKeys = ["UsrName"];

		// Act
		(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(
			localizableStrings, null, bodyKeys);

		// Assert
		cleaned.Should().HaveCount(4,
			because: "all existing strings including base platform strings must be preserved");
		var names = cleaned.Children<JObject>().Select(e => e["name"]!.ToString()).ToList();
		names.Should().Contain("SaveButton",
			because: "base platform string SaveButton must not be wiped");
		names.Should().Contain("CancelButton",
			because: "base platform string CancelButton must not be wiped");
		names.Should().Contain("GeneralInfoTab_caption",
			because: "base platform string GeneralInfoTab_caption must not be wiped");
		names.Should().Contain("UsrName",
			because: "custom Usr string must still be preserved");
		registered.Should().BeEmpty(
			because: "all keys already exist in schema, nothing new to register");
	}

	[Test]
	[Description("CleanAndMerge handles null localizableStrings input without throwing")]
	public void CleanAndMerge_WhenLocalizableStringsIsNull_ReturnsOnlyRegisteredKeys() {
		// Arrange
		HashSet<string> bodyKeys = ["UsrTitle"];

		// Act
		(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(
			null, null, bodyKeys);

		// Assert
		cleaned.Should().HaveCount(1,
			because: "only the Usr body key should be auto-registered with a derived caption");
		cleaned[0]!["name"]!.ToString().Should().Be("UsrTitle",
			because: "UsrTitle from body should be registered with derived caption");
		registered.Should().Equal(["UsrTitle"],
			because: "UsrTitle was newly registered");
	}

	[Test]
	[Description("CleanAndMerge does not duplicate entries when body key matches existing schema entry")]
	public void CleanAndMerge_WhenBodyKeyAlreadyExists_DoesNotDuplicate() {
		// Arrange
		JArray localizableStrings = [
			new JObject {
				["name"] = "SaveButton",
				["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "" } }
			},
			new JObject {
				["name"] = "UsrName",
				["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "Name" } }
			}
		];
		HashSet<string> bodyKeys = ["UsrName", "SaveButton"];

		// Act
		(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(
			localizableStrings, null, bodyKeys);

		// Assert
		cleaned.Should().HaveCount(2,
			because: "existing entries should not be duplicated even when they appear in body keys");
		registered.Should().BeEmpty(
			because: "both keys already exist, nothing new to register");
	}

	[Test]
	[Description("DeriveCaption inserts spaces between camel-case words and removes the Usr prefix")]
	public void DeriveCaption_WhenKeyContainsUsrPrefixAndCamelCase_ReturnsReadableCaption() {
		const string key = "UsrAccountPrimaryContact_caption";
		string caption = ResourceStringHelper.DeriveCaption(key);
		caption.Should().Be("Account Primary Contact",
			because: "generated resource captions should stay human-readable for automatically registered Usr keys");
	}

	[Test]
	[Description("ExtractKeys returns keys from both #ResourceString(Key)# macros and $Resources.Strings.Key runtime-binding syntax.")]
	public void ExtractKeys_WhenBodyContainsBothSyntaxForms_ReturnsAllKeys() {
		// Arrange
		const string body = """
			{
			  "items": [
			    { "label": "#ResourceString(UsrNameInput_label)#" },
			    { "caption": "$Resources.Strings.UsrInfoTab_caption" }
			  ]
			}
			""";

		// Act
		HashSet<string> keys = ResourceStringHelper.ExtractKeys(body);

		// Assert
		keys.Should().Contain("UsrNameInput_label",
			because: "#ResourceString(UsrNameInput_label)# is the macro form and must be extracted");
		keys.Should().Contain("UsrInfoTab_caption",
			because: "$Resources.Strings.UsrInfoTab_caption is the runtime-binding form and must also be extracted");
	}

	[Test]
	[Description("CleanAndMerge skips auto-derivation for DS-bound Usr keys when dsBoundKeys is provided")]
	public void CleanAndMerge_WhenUsrKeyIsDsBound_DoesNotAutoRegister() {
		// Arrange
		HashSet<string> bodyKeys = ["UsrStatus", "UsrCustomLabel"];
		var dsBoundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UsrStatus" };

		// Act
		(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(
			null, null, bodyKeys, dsBoundKeys);

		// Assert
		cleaned.Should().HaveCount(1,
			because: "only UsrCustomLabel should be auto-registered; UsrStatus is DS-bound and skipped");
		cleaned[0]!["name"]!.ToString().Should().Be("UsrCustomLabel",
			because: "non-DS-bound Usr keys should still get auto-derived captions");
		registered.Should().Equal(["UsrCustomLabel"],
			because: "only the non-DS-bound key should be reported as newly registered");
	}

	[Test]
	[Description("CleanAndMerge still registers DS-bound key when explicit resource value is provided")]
	public void CleanAndMerge_WhenDsBoundKeyHasExplicitResource_StillRegisters() {
		// Arrange
		HashSet<string> bodyKeys = ["UsrStatus"];
		var dsBoundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UsrStatus" };
		var resources = new Dictionary<string, string> { ["UsrStatus"] = "Custom Status Label" };

		// Act
		(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(
			null, resources, bodyKeys, dsBoundKeys);

		// Assert
		cleaned.Should().HaveCount(1,
			because: "explicit resources should override the DS-bound skip logic");
		cleaned[0]!["name"]!.ToString().Should().Be("UsrStatus",
			because: "when an explicit value is provided, the key should be registered regardless of DS-binding");
		registered.Should().Equal(["UsrStatus"],
			because: "the explicitly-provided key should be reported as registered");
	}

	[Test]
	[Description("WillResolve returns false for a widget-title key that is not in resources, not DS-bound, and not Usr-prefixed (the ENG-93098 dangling-binding case).")]
	public void WillResolve_WhenWidgetTitleKeyUnregistered_ReturnsFalse() {
		// Arrange
		const string key = "IndicatorWidget_CriticalRequests_title";

		// Act
		bool resolves = ResourceStringHelper.WillResolve(key, null, null);

		// Assert
		resolves.Should().BeFalse(
			because: "a non-Usr, non-DS-bound key absent from resources is never registered and renders raw");
	}

	[Test]
	[Description("WillResolve returns true when the key is passed explicitly through resources.")]
	public void WillResolve_WhenKeyInResources_ReturnsTrue() {
		// Arrange
		const string key = "IndicatorWidget_CriticalRequests_title";
		var resources = new Dictionary<string, string> { [key] = "Critical Requests" };

		// Act
		bool resolves = ResourceStringHelper.WillResolve(key, resources, null);

		// Assert
		resolves.Should().BeTrue(because: "clio registers keys passed explicitly through the resources parameter");
	}

	[Test]
	[Description("WillResolve returns true for a Usr-prefixed key (clio auto-derives a caption) and for a DS-bound key (platform auto-provides the caption).")]
	public void WillResolve_WhenUsrPrefixedOrDsBound_ReturnsTrue() {
		// Arrange
		var dsBoundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PDS_UsrStatus" };

		// Act
		bool usrResolves = ResourceStringHelper.WillResolve("UsrCustomTitle", null, null);
		bool dsResolves = ResourceStringHelper.WillResolve("PDS_UsrStatus", null, dsBoundKeys);

		// Assert
		usrResolves.Should().BeTrue(because: "Usr-prefixed keys are auto-derived by clio");
		dsResolves.Should().BeTrue(because: "DS-bound attribute captions are auto-provided by the platform");
	}

	[Test]
	[Description("CleanAndMerge with null dsBoundKeys behaves the same as before (backward compatible)")]
	public void CleanAndMerge_WhenDsBoundKeysIsNull_AutoDerivesAllUsrKeys() {
		// Arrange
		HashSet<string> bodyKeys = ["UsrStatus", "UsrName"];

		// Act
		(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(
			null, null, bodyKeys, null);

		// Assert
		cleaned.Should().HaveCount(2,
			because: "with null dsBoundKeys, all Usr keys should be auto-derived as before");
		registered.Should().Equal(["UsrStatus", "UsrName"],
			because: "both Usr keys should be registered when no DS-bound filtering is applied");
	}
}
