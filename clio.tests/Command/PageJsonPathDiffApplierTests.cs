using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageJsonPathDiffApplierTests {
	[Test]
	[Description("Apply resolves nested aliases inside objects and arrays when path operations target _id values")]
	public void Apply_WhenOperationsTargetNestedAliases_UpdatesTheExpectedNodes() {
		IPageJsonPathDiffApplier applier = new PageJsonPathDiffApplier();
		JObject source = JObject.Parse("""
			{
			  "values": {
			    "MainContainer": {
			      "_id": "MainContainer",
			      "items": [
			        {
			          "_id": "Field1",
			          "label": "Original"
			        }
			      ]
			    }
			  }
			}
			""");
		JArray operations = [
			new JObject {
				["operation"] = "merge",
				["name"] = "Field1",
				["values"] = new JObject {
					["label"] = "Updated"
				}
			},
			new JObject {
				["operation"] = "insert",
				["parentName"] = "MainContainer",
				["path"] = new JArray("items"),
				["index"] = 1,
				["values"] = new JObject {
					["_id"] = "Field2",
					["label"] = "Inserted"
				}
			}
		];
		JObject result = applier.Apply(source, operations);
		JArray items = (JArray)result["values"]!["MainContainer"]!["items"]!;
		items.Should().HaveCount(2,
			because: "the applier should keep existing aliased items and insert new ones into the resolved target array");
		items[0]!["label"]!.ToString().Should().Be("Updated",
			because: "merge should locate aliased array items by _id and update their properties");
		items[1]!["_id"]!.ToString().Should().Be("Field2",
			because: "insert should resolve the aliased parent container before appending the new item");
	}
}
