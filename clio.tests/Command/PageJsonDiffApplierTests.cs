using System;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
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

	[Test]
	[Description("Insert into a moved element must succeed when both operations share the same depth+index sort key")]
	public void ApplyDiff_WhenInsertTargetsMovedElement_InsertsChildAfterMoveIsApplied() {
		IPageJsonDiffApplier applier = new PageJsonDiffApplier();
		JArray source = [
			new JObject {
				["name"] = "Root",
				["items"] = new JArray {
					new JObject { ["name"] = "Container", ["items"] = new JArray() },
					new JObject { ["name"] = "Other",     ["items"] = new JArray() }
				}
			}
		];
		JArray operations = [
			new JObject {
				["operation"] = "move",
				["name"] = "Container",
				["parentName"] = "Root",
				["path"] = new JArray("items"),
				["index"] = 1
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "Child",
				["parentName"] = "Container",
				["path"] = new JArray("items"),
				["index"] = 0,
				["values"] = new JObject { ["label"] = "ChildLabel" }
			}
		];

		JArray result = applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);

		JArray rootItems = result[0]!["items"] as JArray;
		rootItems.Should().NotBeNull();
		JObject container = rootItems!.Children<JObject>().FirstOrDefault(t => t["name"]?.ToString() == "Container");
		container.Should().NotBeNull(because: "Container should still be present after move");
		JArray containerItems = container!["items"] as JArray;
		containerItems.Should().ContainSingle(because: "Child should be inserted into the moved Container");
		containerItems![0]["name"]!.ToString().Should().Be("Child",
			because: "insert into a moved element must apply after the move re-insert");
	}

	[Test]
	[Description("BasePageFreedomTemplate-style scenario: insert creates new parent container, moves relocate elements into it, and nested inserts target moved elements")]
	public void ApplyDiff_WhenInsertCreatesParentAndMovesRelocate_NestedChildrenEndUpInsideMovedElements() {
		IPageJsonDiffApplier applier = new PageJsonDiffApplier();
		JArray source = [
			new JObject {
				["name"] = "MainHeader",
				["items"] = new JArray {
					new JObject { ["name"] = "ActionContainer", ["items"] = new JArray() }
				}
			},
			new JObject {
				["name"] = "MainContainer",
				["items"] = new JArray()
			}
		];
		JArray operations = [
			new JObject {
				["operation"] = "remove",
				["name"] = "ActionContainer"
			},
			new JObject {
				["operation"] = "move",
				["name"] = "MainHeader",
				["parentName"] = "Main",
				["propertyName"] = "items",
				["index"] = 0
			},
			new JObject {
				["operation"] = "move",
				["name"] = "MainContainer",
				["parentName"] = "Main",
				["propertyName"] = "items",
				["index"] = 1
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "Main",
				["index"] = 0,
				["values"] = new JObject {
					["type"] = "crt.FlexContainer",
					["items"] = new JArray()
				}
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "CardContentWrapper",
				["parentName"] = "MainContainer",
				["propertyName"] = "items",
				["index"] = 0,
				["values"] = new JObject {
					["type"] = "crt.GridContainer",
					["items"] = new JArray()
				}
			}
		];

		JArray result = applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);

		result.Should().ContainSingle(because: "only Main should remain at the root level after moves");
		JObject main = result[0] as JObject;
		main!["name"]!.ToString().Should().Be("Main");
		JArray mainItems = main["items"] as JArray;
		mainItems.Should().HaveCount(2, because: "MainHeader and MainContainer should be moved into Main");
		mainItems![0]!["name"]!.ToString().Should().Be("MainHeader");
		mainItems[1]!["name"]!.ToString().Should().Be("MainContainer");
		JArray mainContainerItems = mainItems[1]!["items"] as JArray;
		mainContainerItems.Should().ContainSingle(
			because: "CardContentWrapper must end up inside the moved MainContainer");
		mainContainerItems![0]!["name"]!.ToString().Should().Be("CardContentWrapper");
	}

	[Test]
	[Description("Failed inserts are retried as moves when their parent appears later via another insert")]
	public void ApplyDiff_WhenInsertTargetsParentCreatedLater_IsRetriedAfterParentExists() {
		IPageJsonDiffApplier applier = new PageJsonDiffApplier();
		JArray source = new();
		JArray operations = [
			new JObject {
				["operation"] = "insert",
				["name"] = "Child",
				["parentName"] = "Parent",
				["propertyName"] = "items",
				["index"] = 0,
				["values"] = new JObject { ["label"] = "ChildLabel" }
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "Parent",
				["index"] = 0,
				["values"] = new JObject {
					["type"] = "crt.Container",
					["items"] = new JArray()
				}
			}
		];

		JArray result = applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);

		result.Should().ContainSingle();
		JObject parent = result[0] as JObject;
		parent!["name"]!.ToString().Should().Be("Parent");
		JArray parentItems = parent["items"] as JArray;
		parentItems.Should().ContainSingle(because: "Child insert must be retried once Parent exists");
		parentItems![0]!["name"]!.ToString().Should().Be("Child");
	}

	[Test]
	[Description("Path-based sort processes shorter paths first for insert ascending ordering")]
	public void ApplyDiff_WhenMultipleNestedInsertsExist_OrdersByPathLengthAscending() {
		IPageJsonDiffApplier applier = new PageJsonDiffApplier();
		JArray source = new();
		JArray operations = [
			new JObject {
				["operation"] = "insert",
				["name"] = "Leaf",
				["parentName"] = "Mid",
				["propertyName"] = "items",
				["index"] = 0,
				["values"] = new JObject()
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "Mid",
				["parentName"] = "Root",
				["propertyName"] = "items",
				["index"] = 0,
				["values"] = new JObject { ["items"] = new JArray() }
			},
			new JObject {
				["operation"] = "insert",
				["name"] = "Root",
				["index"] = 0,
				["values"] = new JObject { ["items"] = new JArray() }
			}
		];

		JArray result = applier.ApplyDiff(source, [operations], [new PageJsonDiffApplyOptions(true)]);

		result.Should().ContainSingle();
		JObject root = result[0] as JObject;
		root!["name"]!.ToString().Should().Be("Root");
		JArray rootItems = root["items"] as JArray;
		rootItems!.Should().ContainSingle();
		JObject mid = rootItems[0] as JObject;
		mid!["name"]!.ToString().Should().Be("Mid");
		JArray midItems = mid["items"] as JArray;
		midItems!.Should().ContainSingle();
		midItems[0]!["name"]!.ToString().Should().Be("Leaf");
	}
}
