using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageBundleBuilderTests {

	[Test]
	[Description("Build merges hierarchy view config, diff-driven configs, resources, parameters, and optional properties when the designer returns current page first")]
	public void Build_WhenHierarchyContainsCurrentPageThenParent_MergesBundleData() {
		// Arrange
		IPageBundleBuilder builder = CreateBuilder();
		List<PageSchemaBundlePart> parts = [
			new(
				new PageDesignerHierarchySchema {
					UId = "child-page-uid",
					Name = "UsrTodo_FormPage",
					PackageUId = "child-package-uid",
					PackageName = "UsrTodo",
					SchemaVersion = 1,
					Body = "child-body",
					LocalizableStrings = new JArray {
						new JObject {
							["name"] = "Title",
							["values"] = new JArray {
								new JObject {
									["cultureName"] = "en-US",
									["value"] = "Child title"
								}
							}
						}
					},
					Parameters = new JArray {
						new JObject {
							["uId"] = "child-param-uid",
							["name"] = "AccountId",
							["caption"] = new JObject {
								["en-US"] = "Account"
							},
							["type"] = 11,
							["required"] = false,
							["parentSchemaUId"] = "child-page-uid",
							["lookup"] = "account-schema-uid",
							["schema"] = "Account"
						}
					},
					OptionalProperties = new JArray {
						new JObject {
							["key"] = "layout",
							["value"] = "child"
						}
					}
				},
				new PageParsedSchemaBody {
					ViewConfigDiff = new JArray {
						new JObject {
							["operation"] = "insert",
							["name"] = "NameField",
							["parentName"] = "MainContainer",
							["propertyName"] = "items",
							["values"] = new JObject {
								["type"] = "crt.Input"
							}
						}
					},
					ViewModelConfig = JObject.Parse("""{ "ignored": true }"""),
					ViewModelConfigDiff = new JArray {
						new JObject {
							["operation"] = "insert",
							["path"] = new JArray("values"),
							["propertyName"] = "ChildValue",
							["values"] = new JObject {
								["_id"] = "ChildValue",
								["type"] = "crt.StringAttribute"
							}
						}
					},
					ModelConfig = JObject.Parse("""{ "ignored": true }"""),
					ModelConfigDiff = new JArray {
						new JObject {
							["operation"] = "insert",
							["path"] = new JArray("dataSources"),
							["propertyName"] = "ChildDS",
							["values"] = new JObject {
								["type"] = "crt.EntityDataSource"
							}
						}
					},
					Handlers = "[{ request: 'child' }]",
					Converters = "{ Child: value => value }",
					Validators = "{ Child: ['required'] }",
					Deps = "['child']",
					Args = "(request, next)"
				}),
			new(
				new PageDesignerHierarchySchema {
					UId = "base-page-uid",
					Name = "BasePage",
					PackageUId = "base-package-uid",
					PackageName = "CrtBase",
					SchemaVersion = 1,
					Body = "base-body",
					LocalizableStrings = new JArray {
						new JObject {
							["name"] = "Title",
							["values"] = new JArray {
								new JObject {
									["cultureName"] = "en-US",
									["value"] = "Base title"
								}
							}
						}
					},
					Parameters = new JArray {
						new JObject {
							["uId"] = "base-param-uid",
							["name"] = "ParentId",
							["caption"] = new JObject {
								["en-US"] = "Parent"
							},
							["type"] = 10,
							["required"] = true,
							["parentSchemaUId"] = "base-page-uid",
							["lookup"] = "contact-schema-uid",
							["schema"] = "Contact"
						}
					},
					OptionalProperties = new JArray {
						new JObject {
							["key"] = "layout",
							["value"] = "base"
						}
					}
				},
				new PageParsedSchemaBody {
					ViewConfigDiff = new JArray {
						new JObject {
							["operation"] = "insert",
							["name"] = "MainContainer",
							["values"] = new JObject {
								["type"] = "crt.FlexContainer",
								["items"] = new JArray()
							}
						}
					},
					ViewModelConfig = JObject.Parse("""
						{
						  "values": {
						    "ParentValue": {
						      "_id": "ParentValue",
						      "type": "crt.StringAttribute"
						    }
						  }
						}
						"""),
					ModelConfig = JObject.Parse("""
						{
						  "dataSources": {
						    "BaseDS": {
						      "type": "crt.EntityDataSource"
						    }
						  }
						}
						"""),
					Handlers = "[{ request: 'base' }]",
					Converters = "{ Base: value => value }",
					Validators = "{ Base: ['required'] }",
					Deps = "['base']",
					Args = "(request)"
				})
		];

		// Act
		PageBundleInfo result = builder.Build(parts);

		// Assert
		result.Name.Should().Be("UsrTodo_FormPage",
			because: "the bundle should use the current schema name");
		result.ViewConfig.Should().HaveCount(1,
			because: "the child view config should be merged into the inherited container hierarchy");
		result.ViewConfig[0]!["items"]!.AsArray().Should().ContainSingle(
			because: "the inherited container should receive the inserted child component");
		result.ViewModelConfig["values"]!["ParentValue"]!.Should().NotBeNull(
			because: "parent view-model config should stay in the merged bundle");
		result.ViewModelConfig["values"]!["ChildValue"]!["_id"]!.ToString().Should().Be("ChildValue",
			because: "view-model diff should take precedence over the child direct config");
		result.ViewModelConfig["ignored"].Should().BeNull(
			because: "a child direct view-model config should be ignored when a diff is present");
		result.ModelConfig["dataSources"]!["BaseDS"]!.Should().NotBeNull(
			because: "parent model config should stay in the merged bundle");
		result.ModelConfig["dataSources"]!["ChildDS"]!.Should().NotBeNull(
			because: "model config diff should augment the accumulator");
		result.ModelConfig["ignored"].Should().BeNull(
			because: "a child direct model config should be ignored when a diff is present");
		result.Resources.Strings["Title"]!["en-US"]!.ToString().Should().Be("Child title",
			because: "child resources should override the parent for the same key and culture");
		result.Parameters.Should().ContainSingle(parameter => parameter.Name == "AccountId" && parameter.IsOwnParameter,
			because: "own parameters should be marked in the merged result");
		result.Parameters.Should().ContainSingle(parameter => parameter.Name == "ParentId" && !parameter.IsOwnParameter,
			because: "inherited parameters should remain in the merged result");
		JToken? mergedOptionalProperty = result.OptionalProperties
			.Select(node => node is null ? null : JToken.Parse(node.ToJsonString()))
			.SingleOrDefault(token => token?["key"]?.ToString() == "layout");
		mergedOptionalProperty.Should().NotBeNull(
			because: "the merged bundle should keep the overridden optional property");
		mergedOptionalProperty!["value"]!.ToString().Should().Be("child",
			because: "child optional properties should override duplicate parent keys");
		result.Handlers.Should().Be("[{ request: 'child' }]",
			because: "handlers should come from the current schema part");
		result.Deps.Should().Be("['child']",
			because: "deps should come from the current schema part");
		result.Args.Should().Be("(request, next)",
			because: "args should come from the current schema part");
	}

	[Test]
	[Description("Build deep-merges configs without diffs and overwrites arrays with child values")]
	public void Build_WhenConfigsHaveNoDiffs_OverwritesArraysDuringMerge() {
		// Arrange
		IPageBundleBuilder builder = CreateBuilder();
		List<PageSchemaBundlePart> parts = [
			new(
				new PageDesignerHierarchySchema {
					UId = "child-page-uid",
					Name = "UsrTodo_FormPage",
					PackageUId = "child-package-uid",
					PackageName = "UsrTodo",
					SchemaVersion = 1,
					Body = "child-body"
				},
				new PageParsedSchemaBody {
					ViewModelConfig = JObject.Parse("""
						{
						  "values": {
						    "List": {
						      "items": ["child"]
						    }
						  }
						}
						"""),
					ModelConfig = JObject.Parse("""
						{
						  "rules": ["child"]
						}
						""")
				}),
			new(
				new PageDesignerHierarchySchema {
					UId = "base-page-uid",
					Name = "BasePage",
					PackageUId = "base-package-uid",
					PackageName = "CrtBase",
					SchemaVersion = 1,
					Body = "base-body"
				},
				new PageParsedSchemaBody {
					ViewModelConfig = JObject.Parse("""
						{
						  "values": {
						    "List": {
						      "_id": "List",
						      "items": ["base"]
						    }
						  }
						}
						"""),
					ModelConfig = JObject.Parse("""
						{
						  "rules": ["base"],
						  "dataSources": {
						    "BaseDS": {
						      "type": "crt.EntityDataSource"
						    }
						  }
						}
						""")
				})
		];

		// Act
		PageBundleInfo result = builder.Build(parts);

		// Assert
		result.ViewModelConfig["values"]!["List"]!["items"]![0]!.ToString().Should().Be("child",
			because: "deep merge should overwrite arrays with the child value instead of concatenating");
		result.ModelConfig["rules"]![0]!.ToString().Should().Be("child",
			because: "model config merge should overwrite arrays with the child value instead of concatenating");
		result.ModelConfig["dataSources"]!["BaseDS"]!.Should().NotBeNull(
			because: "non-array parent model config nodes should still be preserved");
	}

	[Test]
	[Description("Uses the first hierarchy part as the current schema when the designer returns current page before parents")]
	public void Build_WhenCurrentPageIsFirst_UsesFirstPartForCurrentSchemaFields() {
		IPageBundleBuilder builder = CreateBuilder();
		List<PageSchemaBundlePart> parts = [
			new(
				new PageDesignerHierarchySchema {
					UId = "current-page-uid",
					Name = "UsrCurrent_FormPage",
					PackageUId = "current-package-uid",
					PackageName = "UsrCurrent",
					SchemaVersion = 1,
					Body = "current-body"
				},
				new PageParsedSchemaBody {
					Handlers = "[{ request: 'current' }]",
					Deps = "['current']",
					Args = "(request, next)"
				}),
			new(
				new PageDesignerHierarchySchema {
					UId = "parent-page-uid",
					Name = "CrtBasePage",
					PackageUId = "parent-package-uid",
					PackageName = "CrtBase",
					SchemaVersion = 1,
					Body = "parent-body"
				},
				new PageParsedSchemaBody {
					Handlers = "[{ request: 'parent' }]",
					Deps = "['parent']",
					Args = "(request)"
				})
		];

		PageBundleInfo result = builder.Build(parts);

		result.Name.Should().Be("UsrCurrent_FormPage",
			because: "bundle identity should come from the first current-page hierarchy item");
		result.Handlers.Should().Be("[{ request: 'current' }]",
			because: "handlers should come from the first current-page hierarchy item");
		result.Deps.Should().Be("['current']",
			because: "deps should come from the first current-page hierarchy item");
		result.Args.Should().Be("(request, next)",
			because: "args should come from the first current-page hierarchy item");
	}

	[Test]
	[Description("Two sequential Build calls on the same builder do not leak alias state across chains: an alias declared in the first chain must not affect the second chain's merge, while the alias still carries across layers within its own chain.")]
	public void Build_CalledTwice_DoesNotLeakAliasStateAcrossChains() {
		// Arrange — first chain declares an alias 'Btn' (real element 'RealButton') whose excludeOperations drops a
		// merge; a child layer then merges by that alias name. If the alias carries across layers within this chain
		// (required), the merge is excluded and the caption stays 'base'.
		IPageBundleBuilder builder = CreateBuilder();
		List<PageSchemaBundlePart> aliasChain = [
			new(
				new PageDesignerHierarchySchema {
					UId = "alias-child-uid", Name = "UsrAlias_FormPage",
					PackageUId = "alias-pkg-uid", PackageName = "UsrAlias", SchemaVersion = 1, Body = "child"
				},
				new PageParsedSchemaBody {
					ViewConfigDiff = new JArray {
						new JObject {
							["operation"] = "merge",
							["name"] = "Btn",
							["values"] = new JObject { ["caption"] = "child" }
						}
					}
				}),
			new(
				new PageDesignerHierarchySchema {
					UId = "alias-base-uid", Name = "BasePage",
					PackageUId = "alias-base-pkg-uid", PackageName = "CrtBase", SchemaVersion = 1, Body = "base"
				},
				new PageParsedSchemaBody {
					ViewConfigDiff = new JArray {
						new JObject {
							["operation"] = "insert",
							["name"] = "MainContainer",
							["values"] = new JObject { ["type"] = "crt.FlexContainer", ["items"] = new JArray() }
						},
						new JObject {
							["operation"] = "insert",
							["name"] = "RealButton",
							["parentName"] = "MainContainer",
							["propertyName"] = "items",
							["alias"] = new JObject { ["name"] = "Btn", ["excludeOperations"] = new JArray { "merge" } },
							["values"] = new JObject { ["type"] = "crt.Button", ["caption"] = "base" }
						}
					}
				})
		];

		// Second chain uses an element literally named 'Btn' (no alias) and merges it. If the first chain's alias had
		// leaked into this Build, the merge would be excluded and the caption would stay 'x'.
		List<PageSchemaBundlePart> plainChain = [
			new(
				new PageDesignerHierarchySchema {
					UId = "plain-child-uid", Name = "UsrPlain_FormPage",
					PackageUId = "plain-pkg-uid", PackageName = "UsrPlain", SchemaVersion = 1, Body = "child"
				},
				new PageParsedSchemaBody {
					ViewConfigDiff = new JArray {
						new JObject {
							["operation"] = "merge",
							["name"] = "Btn",
							["values"] = new JObject { ["caption"] = "merged" }
						}
					}
				}),
			new(
				new PageDesignerHierarchySchema {
					UId = "plain-base-uid", Name = "BasePage",
					PackageUId = "plain-base-pkg-uid", PackageName = "CrtBase", SchemaVersion = 1, Body = "base"
				},
				new PageParsedSchemaBody {
					ViewConfigDiff = new JArray {
						new JObject {
							["operation"] = "insert",
							["name"] = "MainContainer",
							["values"] = new JObject { ["type"] = "crt.FlexContainer", ["items"] = new JArray() }
						},
						new JObject {
							["operation"] = "insert",
							["name"] = "Btn",
							["parentName"] = "MainContainer",
							["propertyName"] = "items",
							["values"] = new JObject { ["type"] = "crt.Button", ["caption"] = "x" }
						}
					}
				})
		];

		// Act
		PageBundleInfo aliasResult = builder.Build(aliasChain);
		PageBundleInfo plainResult = builder.Build(plainChain);

		// Assert
		aliasResult.ViewConfig[0]!["items"]![0]!["caption"]!.ToString().Should().Be("base",
			because: "within one chain the alias carries across layers, so its excludeOperations drops the child merge");
		plainResult.ViewConfig[0]!["items"]![0]!["caption"]!.ToString().Should().Be("merged",
			because: "each Build call uses a fresh applier, so the first chain's alias must not exclude the second chain's merge");
	}

	[Test]
	[Description("Build surfaces a JsonDiffApplierException (not a bare InvalidCastException) when the view-config applier returns a non-array token, so the guarded cast keeps the failure inside the type the get-page / business-rule callers catch.")]
	public void Build_WhenViewConfigApplierReturnsNonArray_ThrowsNamingExpectedType() {
		// Arrange
		IJsonDiffApplier viewApplier = Substitute.For<IJsonDiffApplier>();
		viewApplier.ApplyDiff(Arg.Any<JArray>(), Arg.Any<IReadOnlyList<JArray>>(), Arg.Any<IReadOnlyList<JsonApplierOperationsOptions>>())
			.Returns(new JObject());
		IPageBundleBuilder builder = new PageBundleBuilder(() => viewApplier, () => Substitute.For<IJsonPathDiffApplier>());
		List<PageSchemaBundlePart> parts = [
			new(
				new PageDesignerHierarchySchema {
					UId = "u", Name = "UsrPage", PackageUId = "p", PackageName = "UsrPage", SchemaVersion = 1, Body = "b"
				},
				new PageParsedSchemaBody())
		];

		// Act
		Action act = () => builder.Build(parts);

		// Assert
		act.Should().Throw<JsonDiffApplierException>()
			.WithMessage("*view config*JArray*",
				because: "an unexpected view-config token must surface as the handled exception type naming the expected shape, not a bare InvalidCastException");
	}

	[Test]
	[Description("BuildConfig surfaces a JsonDiffApplierException when the path applier returns a non-object token for a config diff, keeping the guarded cast failure inside the handled type.")]
	public void Build_WhenPathApplierReturnsNonObject_ThrowsNamingExpectedType() {
		// Arrange — the view applier resolves fine (empty array) so execution reaches BuildConfig, where the path
		// applier returns a non-object for a non-empty view-model diff.
		IJsonDiffApplier viewApplier = Substitute.For<IJsonDiffApplier>();
		viewApplier.ApplyDiff(Arg.Any<JArray>(), Arg.Any<IReadOnlyList<JArray>>(), Arg.Any<IReadOnlyList<JsonApplierOperationsOptions>>())
			.Returns(new JArray());
		IJsonPathDiffApplier pathApplier = Substitute.For<IJsonPathDiffApplier>();
		pathApplier.Apply(Arg.Any<JToken>(), Arg.Any<JArray>(), Arg.Any<JsonApplierOperationsOptions>())
			.Returns(new JArray());
		IPageBundleBuilder builder = new PageBundleBuilder(() => viewApplier, () => pathApplier);
		List<PageSchemaBundlePart> parts = [
			new(
				new PageDesignerHierarchySchema {
					UId = "u", Name = "UsrPage", PackageUId = "p", PackageName = "UsrPage", SchemaVersion = 1, Body = "b"
				},
				new PageParsedSchemaBody {
					ViewModelConfigDiff = new JArray {
						new JObject { ["operation"] = "merge", ["path"] = new JArray(), ["values"] = new JObject() }
					}
				})
		];

		// Act
		Action act = () => builder.Build(parts);

		// Assert
		act.Should().Throw<JsonDiffApplierException>()
			.WithMessage("*config*JObject*",
				because: "an unexpected config token must surface as the handled exception type naming the expected shape");
	}

	private static IPageBundleBuilder CreateBuilder() {
		return new PageBundleBuilder(() => new JsonDiffApplier(), () => new JsonPathDiffApplier());
	}
}
