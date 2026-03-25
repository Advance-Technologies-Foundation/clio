using System;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class PageJsonDiffApplierTests {

	[Test]
	[Description("Applies insert, merge, set, move, remove, and remove-properties operations with view-config semantics")]
	public void ApplyDiff_WhenOperationsUseAllSupportedFamilies_ProducesExpectedViewConfig() {
		// Arrange
		IPageJsonDiffApplier applier = new PageJsonDiffApplier();
		JArray source = [
			new JObject {
				["name"] = "Container",
				["caption"] = "Base",
				["items"] = new JArray {
					new JObject {
						["name"] = "Field1",
						["label"] = "Original"
					},
					new JObject {
						["name"] = "Field2",
						["label"] = "Second"
					}
				}
			}
		];
		JArray operations = [
			new JObject {
				["operation"] = "merge",
				["name"] = "Container",
				["values"] = new JObject {
					["caption"] = "Merged"
				}
			},
			new JObject {
				["operation"] = "set",
				["name"] = "Field2",
				["values"] = new JObject {
					["label"] = "Updated"
				}
			},
			new JObject {
				["operation"] = "move",
				["name"] = "Field2",
				["parentName"] = "Container",
				["path"] = new JArray("items"),
				["index"] = 0
			},
			new JObject {
				["operation"] = "remove",
				["name"] = "Container",
				["properties"] = new JArray("caption")
			},
			new JObject {
				["operation"] = "remove",
				["name"] = "Field1"
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "Field3",
				["parentName"] = "Container",
				["path"] = new JArray("items"),
				["index"] = 1,
				["values"] = new JObject {
					["label"] = "Inserted"
				}
			}
		];

		// Act
		JArray result = applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);

		// Assert
		result.Should().ContainSingle(
			because: "the diff should keep the root container while mutating its children");
		result[0]!["caption"].Should().BeNull(
			because: "remove with properties should delete the requested object properties");
		(result[0]!["items"] as JArray).Should().HaveCount(2,
			because: "the diff should remove one item and insert one replacement");
		result[0]!["items"]![0]!["name"]!.ToString().Should().Be("Field2",
			because: "move should place the requested item at the target index");
		result[0]!["items"]![0]!["label"]!.ToString().Should().Be("Updated",
			because: "set should update nested properties after positional changes");
		result[0]!["items"]![1]!["name"]!.ToString().Should().Be("Field3",
			because: "insert should add a new named item into the requested collection");
		result[0]!["items"]![1]!["label"]!.ToString().Should().Be("Inserted",
			because: "insert should preserve the provided item payload");
	}

	[Test]
	[Description("Skips cache entries for items whose name property is explicitly null")]
	public void ApplyDiff_WhenItemNameIsNull_DoesNotThrowDuringLookup() {
		IPageJsonDiffApplier applier = new PageJsonDiffApplier();
		JArray source = [
			new JObject {
				["name"] = null,
				["items"] = new JArray()
			},
			new JObject {
				["name"] = "Container",
				["caption"] = "Base"
			}
		];
		JArray operations = [
			new JObject {
				["operation"] = "merge",
				["name"] = "Container",
				["values"] = new JObject {
					["caption"] = "Updated"
				}
			}
		];

		Action act = () => applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);

		JArray result = null;
		act.Should().NotThrow(
			because: "items with explicit null names should be ignored by the diff cache");
		result = applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);
		result[1]!["caption"]!.ToString().Should().Be("Updated",
			because: "named items after a null-name sibling should still be resolved and updated");
	}
}
