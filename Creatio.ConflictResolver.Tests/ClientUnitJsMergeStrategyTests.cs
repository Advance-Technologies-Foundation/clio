	using System.Text.Json.Nodes;
	using Creatio.ConflictResolver.Tests.TestSupport;

	namespace Creatio.ConflictResolver.Tests;

	[TestFixture, Category("Unit")]
	public class ClientUnitJsMergeStrategyTests
	{
		[Test]
		[Description("Rejects a malformed remote semantic section instead of substituting the base section.")]
		public void ClientUnitMerge_MalformedRemoteSection_ReturnsInvalidInput()
		{
			// Arrange
			string baseContent = BuildClientUnit("[]");
			string remoteContent = baseContent.Replace(
				"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
				"/**SCHEMA_HANDLERS*/BROKEN/**SCHEMA_HANDLERS*/",
				StringComparison.Ordinal);

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				baseContent,
				remoteContent));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
				"a malformed branch section must fail closed rather than disappear from verified output");
		}

		[Test]
		[Description("Rejects more than one marker pair for the same semantic ClientUnit section.")]
		public void ClientUnitMerge_AmbiguousMarkerPairs_ReturnsInvalidInput()
		{
			// Arrange
			string baseContent = BuildClientUnit("[]");
			string remoteContent = baseContent.Replace(
				"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
				"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
				StringComparison.Ordinal);

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				baseContent,
				remoteContent));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
				"ambiguous repeated marker pairs must not cause later branch sections to be ignored");
		}

		[Test]
		[Description("Fails closed when the remote branch adds a semantic section absent from base and local.")]
		public void ClientUnitMerge_RemoteSectionAddition_ReturnsWholeFileConflict()
		{
			// Arrange
			string baseContent = "define('X', [], () => ({ viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ }));";
			string remoteContent = "define('X', [], () => ({ viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[{ request: 'x', handler: () => true }]/**SCHEMA_HANDLERS*/ }));";

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				baseContent,
				remoteContent,
				null,
				global::Creatio.ConflictResolver.MergeMode.Automerge));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict),
				"a branch-only section must remain visible instead of being skipped by a local-driven merge");
			Assert.That(result.MergedContent, Does.Contain("SCHEMA_HANDLERS"),
				"the complete remote section must be preserved for the human decision");
			Assert.That(result.MergedContent, Does.Contain(">>>>>>> Remote"),
				"whole-file alternatives are the smallest safe fallback for a structural section change");
		}

		[Test]
		[Description("Fails closed when one branch removes a semantic section while the other changes it.")]
		public void ClientUnitMerge_SectionDeleteVersusChange_ReturnsWholeFileConflict()
		{
			// Arrange
			string baseContent = "define('X', [], () => ({ viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{ name: 'A', value: 1 }]/**SCHEMA_VIEW_CONFIG_DIFF*/ }));";
			string localContent = baseContent.Replace("value: 1", "value: 2", StringComparison.Ordinal);
			string remoteContent = "define('X', [], () => ({}));";

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent,
				null,
				global::Creatio.ConflictResolver.MergeMode.Automerge));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict),
				"delete-versus-change must require a human choice rather than silently retaining local");
			Assert.That(result.MergedContent, Does.Contain("value: 2"),
				"the changed local section must remain available");
			Assert.That(result.MergedContent, Does.Contain("define('X', [], () => ({}))"),
				"the complete remote deletion alternative must remain available");
		}

		[Test]
		[Description("Preserves repeated semantic patch operations during an identical three-way merge.")]
		public void ClientUnitMerge_RepeatedSemanticKeys_PreservesEveryOperation()
		{
			// Arrange
			const string viewConfig = """
			[
			  { "operation": "merge", "name": "Same", "values": { "a": 1 } },
			  { "operation": "merge", "name": "Same", "values": { "b": 2 } }
			]
			""";
			string content = BuildClientUnit(viewConfig);

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				content,
				content,
				content));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved),
				"an identical merge must resolve without changing the semantic operation sequence");
			var entries = JsonNode.Parse(ExtractClientUnitSectionJson(result.MergedContent!, "SCHEMA_VIEW_CONFIG_DIFF"))!.AsArray();
			Assert.That(entries, Has.Count.EqualTo(2),
				"repeated operation and name pairs can be sequential Creatio patches and must not be deduplicated");
		}

		[Test]
		[Description("Uses complete AST spans when formatting a conflict that contains a regular expression bracket.")]
		public void ClientUnitMerge_RegexBracketConflict_PreservesBothCompleteAlternatives()
		{
			// Arrange
			string baseContent = BuildClientUnit("[]").Replace(
				"/**SCHEMA_HANDLERS*/[]",
				"/**SCHEMA_HANDLERS*/[{ request: \"x\", handler: () => /]/.test(\"x\"), value: 1 }]",
				StringComparison.Ordinal);
			string localContent = baseContent.Replace("value: 1", "value: 2", StringComparison.Ordinal);
			string remoteContent = baseContent.Replace("value: 1", "value: 3", StringComparison.Ordinal);

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent,
				null,
				global::Creatio.ConflictResolver.MergeMode.Automerge));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts),
				"different values on both branches require a human choice");
			Assert.That(result.MergedContent, Does.Contain("value: 2"),
				"the complete local alternative must remain available to the agent");
			Assert.That(result.MergedContent, Does.Contain("value: 3"),
				"the complete remote alternative must remain available to the agent");
		}

		[Test]
		[Description("Rejects a ClientUnit section whose JSON token count exceeds the resolver budget.")]
		public void ClientUnitMerge_ExcessiveJsonComplexity_ReturnsInvalidInput()
		{
			// Arrange
			string values = string.Join(",", Enumerable.Repeat("0", 25_001));
			string content = BuildClientUnit($"[{values}]");

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				content,
				content,
				content));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
				"excessive JSON complexity must be rejected before JsonNode cloning can amplify memory use");
			Assert.That(result.ErrorMessage, Does.Contain("complexity limit"),
				"the diagnostic must identify the bounded complexity rule");
		}

		[Test]
		[Description("Rejects ClientUnit indentation that would amplify a bounded section beyond the output limit.")]
		public void ClientUnitMerge_ExcessiveEmbeddedIndentation_ReturnsInvalidInput()
		{
			// Arrange
			string entries = string.Join(",", Enumerable.Range(0, 200).Select(index =>
				$"{{\"operation\":\"insert\",\"name\":\"Item{index}\",\"parentName\":\"Root\"}}"));
			string indent = new(' ', 4096);
			string content = $$"""
				define("UsrProof", [], function() {
					return {
						viewConfigDiff:
				{{indent}}/**SCHEMA_VIEW_CONFIG_DIFF*/[{{entries}}]/**SCHEMA_VIEW_CONFIG_DIFF*/
					};
				});
				""";

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				content,
				content,
				content));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
				"projected indentation must be bounded before the expanded string is materialized");
			Assert.That(result.ErrorCode, Is.EqualTo("MergeOutputLimitExceeded"),
				"the caller must receive the stable output-bound failure code");
		}

		[Test]
		[Description("Rejects marker expressions that continue after an array instead of truncating them at a regex bracket.")]
		public void ClientUnitMerge_ExpressionAfterArrayWithRegexBracket_FailsClosed()
		{
			// Arrange
			string content = BuildClientUnit("[{ \"name\": \"SafePrefix\" }].filter(() => /]/.test(\"x\"))");

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				content,
				content,
				content));

			// Assert
			Assert.That(result.Status, Is.Not.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved),
				"the resolver must not accept only the JSON-looking prefix of a longer JavaScript expression");
			Assert.That(result.MergedContent, Is.Null,
				"truncated expression content must never be exposed as a successful semantic merge");
		}

		[Test]
		[Description("Normalizes overlapping large layout spans without allocating one entry per occupied grid cell.")]
		public void ClientUnitMerge_LargeLayoutSpans_ShiftsByRectangle()
		{
			// Arrange
			const string viewConfig = """
			[
			  { "operation": "insert", "name": "First", "parentName": "Root", "index": 0,
			    "values": { "layoutConfig": { "column": 0, "row": 0, "colSpan": 2000000, "rowSpan": 2000000 } } },
			  { "operation": "insert", "name": "Second", "parentName": "Root", "index": 1,
			    "values": { "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 } } }
			]
			""";
			string content = BuildClientUnit(viewConfig);

			// Act
			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				content,
				content,
				content));

			// Assert
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved),
				"large spans should remain ordinary semantic layout input rather than a memory hazard");
			var entries = JsonNode.Parse(ExtractClientUnitSectionJson(result.MergedContent!, "SCHEMA_VIEW_CONFIG_DIFF"))!.AsArray();
			Assert.That(entries[1]?["values"]?["layoutConfig"]?["row"]?.GetValue<long>(), Is.EqualTo(2000000),
				"the second rectangle should jump directly below the first rectangle");
		}

		[Test]
		public void ClientUnitMerge_FixtureCase1_MatchesExpectedResolvedFile()
		{
			var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase1", "baseFormPage.js"));
			var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase1", "localFormPage.js"));
			var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase1", "remoteFormPage.js"));
			var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase1", "resolvedFormPage.js"));

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			var normalized = ResolverTestSupport.NormalizeLineEndings(result.MergedContent!);
			Assert.That(normalized, Does.Contain("viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[\n\t\t\t{"));
			Assert.That(
				ResolverTestSupport.GetClientUnitCanonicalArraySectionEntries(result.MergedContent!, "SCHEMA_VIEW_CONFIG_DIFF", "name"),
				Is.EqualTo(ResolverTestSupport.GetClientUnitCanonicalArraySectionEntries(expected, "SCHEMA_VIEW_CONFIG_DIFF", "name")));
			Assert.That(
				ResolverTestSupport.GetClientUnitViewModelAttributePaths(result.MergedContent!),
				Is.EqualTo(ResolverTestSupport.GetClientUnitViewModelAttributePaths(expected)));
			Assert.That(
				ResolverTestSupport.GetClientUnitCanonicalArraySectionEntries(result.MergedContent!, "SCHEMA_MODEL_CONFIG_DIFF"),
				Is.EqualTo(ResolverTestSupport.GetClientUnitCanonicalArraySectionEntries(expected, "SCHEMA_MODEL_CONFIG_DIFF")));
			Assert.That(
				ResolverTestSupport.GetClientUnitCanonicalArraySectionEntries(result.MergedContent!, "SCHEMA_HANDLERS", "name"),
				Is.EqualTo(ResolverTestSupport.GetClientUnitCanonicalArraySectionEntries(expected, "SCHEMA_HANDLERS", "name")));
			Assert.That(
				ResolverTestSupport.GetClientUnitCanonicalObjectSection(result.MergedContent!, "SCHEMA_CONVERTERS"),
				Is.EqualTo(ResolverTestSupport.GetClientUnitCanonicalObjectSection(expected, "SCHEMA_CONVERTERS")));
			Assert.That(
				ResolverTestSupport.GetClientUnitCanonicalObjectSection(result.MergedContent!, "SCHEMA_VALIDATORS"),
				Is.EqualTo(ResolverTestSupport.GetClientUnitCanonicalObjectSection(expected, "SCHEMA_VALIDATORS")));
		}

		[Test]
		public void ClientUnitMerge_FixtureCase2_MatchesExpectedResolvedFile()
		{
			var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase2", "baseFormPage.js"));
			var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase2", "localFormPage.js"));
			var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase2", "remoteFormPage.js"));
			var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase2", "resolvedFormPage.js"));

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));
			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			Assert.That(
				ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
				Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expected)));
		}

		[Test]
		public void ClientUnitMerge_FixtureCase3_MatchesExpectedResolvedFile()
		{
			var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase3", "base.js"));
			var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase3", "local.js"));
			var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase3", "remote.js"));
			var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase3", "resolved.js"));

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			Assert.That(
				ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
				Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expected)));
		}

		[Test]
		public void ClientUnitMerge_FixtureCase4_MatchesExpectedResolvedFile()
		{
			var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase4", "base.js"));
			var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase4", "local.js"));
			var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase4", "remote.js"));
			var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase4", "resolved.js"));

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			Assert.That(
				ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
				Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expected)));
		}

		[Test]
		public void ClientUnitMerge_FixtureCase5_MatchesExpectedResolvedFile()
		{
			var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase5", "base.js"));
			var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase5", "local.js"));
			var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase5", "remote.js"));
			var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ClientUnitCase5", "resolved.js"));

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent,
				"",
				Mode: MergeMode.Automerge));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			Assert.That(
				ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
				Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expected)));
		}


		[Test]
		public void ClientUnitMerge_UnionAndDeletion_LocalWinsConflict()
		{
			var baseContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{ "operation": "insert", "name": "A", "values": { "caption": "BaseA" } },
							{ "operation": "insert", "name": "B", "values": { "caption": "BaseB" } }
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
							{ "operation": "merge", "path": ["attributes"], "values": { "AttrBase": { "modelConfig": { "path": "PDS.AttrBase" } } } }
						]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
							{ "operation": "merge", "path": ["dataSources"], "values": { "BaseDs": { "type": "crt.EntityDataSource" } } }
						]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[
							{ "request": "crt.Save", "name": "HandleSave" }
						]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var localContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{ "operation": "insert", "name": "A", "values": { "caption": "LocalA" } },
							{ "operation": "insert", "name": "C", "values": { "caption": "LocalC" } }
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
							{ "operation": "merge", "path": ["attributes"], "values": { "AttrBase": { "modelConfig": { "path": "PDS.AttrBase" } }, "AttrLocal": { "modelConfig": { "path": "PDS.AttrLocal" } } } }
						]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
							{ "operation": "merge", "path": ["dataSources"], "values": { "BaseDs": { "type": "crt.EntityDataSource" }, "LocalDs": { "type": "crt.EntityDataSource" } } }
						]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[
							{ "request": "crt.Save", "name": "HandleSave" }
						]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var remoteContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{ "operation": "insert", "name": "A", "values": { "caption": "RemoteA" } },
							{ "operation": "insert", "name": "B", "values": { "caption": "BaseB" } },
							{ "operation": "insert", "name": "D", "values": { "caption": "RemoteD" } }
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
							{ "operation": "merge", "path": ["attributes"], "values": { "AttrBase": { "modelConfig": { "path": "PDS.AttrBase" } }, "AttrRemote": { "modelConfig": { "path": "PDS.AttrRemote" } } } }
						]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
							{ "operation": "merge", "path": ["dataSources"], "values": { "BaseDs": { "type": "crt.EntityDataSource" }, "RemoteDs": { "type": "crt.EntityDataSource" } } }
						]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[
							{ "request": "crt.Save", "name": "HandleSave" }
						]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			Assert.That(result.MergedContent, Does.Contain("\"name\": \"A\""));
			Assert.That(result.MergedContent, Does.Contain("\"name\": \"C\""));
			Assert.That(result.MergedContent, Does.Contain("\"name\": \"D\""));
			Assert.That(result.MergedContent, Does.Not.Contain("\"name\": \"B\""));
			Assert.That(result.MergedContent, Does.Contain("\"caption\": \"LocalA\""));
			var mergedAttributes = ResolverTestSupport.GetClientUnitViewModelAttributePaths(result.MergedContent!);
			Assert.That(mergedAttributes.Keys, Does.Contain("AttrLocal"));
			Assert.That(mergedAttributes.Keys, Does.Contain("AttrRemote"));
		Assert.That(result.MergedContent, Does.Contain("\"LocalDs\""));
		Assert.That(result.MergedContent, Does.Contain("\"RemoteDs\""));
	}

	[Test]
	public void ClientUnitMerge_AutomergeMode_EmitsConflictMarkersForPatchArrayItem()
	{
		var baseContent = """
			define("TestSchema", [], function() {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
						{ "operation": "insert", "name": "A", "values": { "caption": "BaseA" } }
					]/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";

		var localContent = """
			define("TestSchema", [], function() {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
						{ "operation": "insert", "name": "A", "values": { "caption": "LocalA" } },
						{ "operation": "insert", "name": "C", "values": { "caption": "LocalC" } }
					]/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";

		var remoteContent = """
			define("TestSchema", [], function() {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
						{ "operation": "insert", "name": "A", "values": { "caption": "RemoteA" } },
						{ "operation": "insert", "name": "D", "values": { "caption": "RemoteD" } }
					]/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
			baseContent,
			localContent,
			remoteContent,
			@"Pkg\Schemas\TestSchema\TestSchema.js",
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		var normalized = ResolverTestSupport.NormalizeLineEndings(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(normalized, Does.Contain("<<<<<<< Local"));
		Assert.That(normalized, Does.Contain("\"caption\": \"LocalA\""));
		Assert.That(normalized, Does.Contain("\"caption\": \"RemoteA\""));
		Assert.That(normalized, Does.Not.Contain("\n\t\t<<<<<<< Local\n"));
		Assert.That(normalized, Does.Contain("\"name\": \"C\""));
		Assert.That(normalized, Does.Contain("\"name\": \"D\""));
	}

	[Test]
	public void ClientUnitMerge_AutomergeMode_EmitsConflictMarkersFromFixture()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitJs", "base.js"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitJs", "local.js"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitJs", "remote.js"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitJs", "resolved.js"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
			baseContent,
			localContent,
			remoteContent,
			@"Pkg\Schemas\TestSchema\TestSchema.js",
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);

		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}
	
	[Test]
	public void ClientUnitMerge_AutomergeMode_EmitsConflictMarkersFromFixture2()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\NewCaseClientModule", "base.js"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\NewCaseClientModule", "local.js"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\NewCaseClientModule", "remote.js"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\NewCaseClientModule", "resolved.js"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
			baseContent,
			localContent,
			remoteContent,
			@"Pkg\Schemas\TestSchema\TestSchema.js",
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);

		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void ClientUnitMerge_ReindexesInsertIndexesByParentName()
	{
			var baseContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{ "operation": "insert", "name": "BaseItems", "parentName": "Container", "propertyName": "items", "index": 0, "values": { "caption": "BaseItems" } }
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var localContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{ "operation": "insert", "name": "BaseItems", "parentName": "Container", "propertyName": "items", "index": 0, "values": { "caption": "BaseItems" } },
							{ "operation": "insert", "name": "LocalActions", "parentName": "Container", "propertyName": "actions", "index": 0, "values": { "caption": "LocalActions" } }
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var remoteContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{ "operation": "insert", "name": "BaseItems", "parentName": "Container", "propertyName": "items", "index": 0, "values": { "caption": "BaseItems" } },
							{ "operation": "insert", "name": "RemoteItems", "parentName": "Container", "propertyName": "items", "index": 0, "values": { "caption": "RemoteItems" } }
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		
			var entries = ResolverTestSupport
				.GetClientUnitCanonicalArraySectionEntries(result.MergedContent!, "SCHEMA_VIEW_CONFIG_DIFF")
				.Select(static x => JsonNode.Parse(x)?.AsObject())
				.Where(static x => x is not null)
				.Select(static x => x!)
				.Where(static x =>
					string.Equals(x["operation"]?.GetValue<string>(), "insert", StringComparison.Ordinal) &&
					string.Equals(x["parentName"]?.GetValue<string>(), "Container", StringComparison.Ordinal))
				.ToArray();

			Assert.That(entries.Length, Is.EqualTo(3));

			var indexes = entries
				.Select(static x => x["index"]?.GetValue<int>() ?? -1)
				.OrderBy(static x => x)
				.ToArray();

			Assert.That(indexes, Is.EqualTo(new[] { 0, 1, 2 }));
		}

		[Test]
		public void ClientUnitMerge_ReindexesAndShiftsLayoutRowsInsideParent()
		{
			var baseContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Name",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name",
									"control": "$Name",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var localContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Name",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name",
									"control": "$Name",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							},
							{
								"operation": "insert",
								"name": "Name2",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name2",
									"control": "$Name2",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";
			var remoteContent = baseContent;

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);

			var byName = ResolverTestSupport
				.GetClientUnitCanonicalArraySectionEntries(result.MergedContent!, "SCHEMA_VIEW_CONFIG_DIFF")
				.Select(static x => JsonNode.Parse(x)?.AsObject())
				.Where(static x => x is not null)
				.Select(static x => x!)
				.Where(static x => string.Equals(x["operation"]?.GetValue<string>(), "insert", StringComparison.Ordinal))
				.ToDictionary(
					static x => x["name"]?.GetValue<string>() ?? string.Empty,
					static x => x,
					StringComparer.Ordinal);

			var name = byName["Name"];
			var name2 = byName["Name2"];

			Assert.That(name["index"]?.GetValue<int>(), Is.EqualTo(0));
			Assert.That(name2["index"]?.GetValue<int>(), Is.EqualTo(1));
			Assert.That(name["values"]?["layoutConfig"]?["row"]?.GetValue<int>(), Is.EqualTo(1));
			Assert.That(name2["values"]?["layoutConfig"]?["row"]?.GetValue<int>(), Is.EqualTo(2));
		}

		[Test]
		public void ClientUnitMerge_ReindexesAndShiftsLayoutRowsInsideParent_AddsLocal()
		{
			var baseContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Name",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name",
									"control": "$Name",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var localContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Name",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name",
									"control": "$Name",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							},
							{
								"operation": "insert",
								"name": "Name2",
								"values": {
									"layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name2",
									"control": "$Name2",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 1
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var remoteContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Name",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Name",
									"control": "$Name",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							},
							{
								"operation": "insert",
								"name": "Colour",
								"values": {
									"layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Colour",
									"control": "$Colour",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 1
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";
			
			var expectedResult = """
			define("TestSchema", [], function() {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
						{
							"operation": "insert",
							"name": "Name",
							"values": {
								"layoutConfig": {
									"column": 1,
									"row": 1,
									"colSpan": 1,
									"rowSpan": 1
								},
								"type": "crt.Input",
								"label": "$Resources.Strings.Name",
								"control": "$Name",
								"labelPosition": "auto"
							},
							"parentName": "SideAreaProfileContainer",
							"propertyName": "items",
							"index": 0
						},
						{
							"operation": "insert",
							"name": "Colour",
							"values": {
								"layoutConfig": {
									"column": 1,
									"row": 2,
									"colSpan": 1,
									"rowSpan": 1
								},
								"type": "crt.Input",
								"label": "$Resources.Strings.Colour",
								"control": "$Colour",
								"labelPosition": "auto"
							},
							"parentName": "SideAreaProfileContainer",
							"propertyName": "items",
							"index": 1
						},
						{
							"operation": "insert",
							"name": "Name2",
							"values": {
								"layoutConfig": {
									"column": 1,
									"row": 3,
									"colSpan": 1,
									"rowSpan": 1
								},
								"type": "crt.Input",
								"label": "$Resources.Strings.Name2",
								"control": "$Name2",
								"labelPosition": "auto"
							},
							"parentName": "SideAreaProfileContainer",
							"propertyName": "items",
							"index": 2
						}
					]/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
			Assert.That(result.MergedContent, Is.EqualTo(expectedResult));
		}


		[Test]
		public void ClientUnitMerge_UsesRemoteAsOrderBaseline_WhenIndexesCollide()
		{
			var baseContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Z_RemoteBase",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Z_RemoteBase",
									"control": "$Z_RemoteBase",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var localContent = """
				define("TestSchema", [], function() {
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
							{
								"operation": "insert",
								"name": "Z_RemoteBase",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.Z_RemoteBase",
									"control": "$Z_RemoteBase",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							},
							{
								"operation": "insert",
								"name": "A_LocalNew",
								"values": {
									"layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 },
									"type": "crt.Input",
									"label": "$Resources.Strings.A_LocalNew",
									"control": "$A_LocalNew",
									"labelPosition": "auto"
								},
								"parentName": "SideAreaProfileContainer",
								"propertyName": "items",
								"index": 0
							}
						]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				});
				""";

			var remoteContent = baseContent;

			var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
				global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs,
				baseContent,
				localContent,
				remoteContent));

			Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);

			var arrayJson = ExtractClientUnitSectionJson(result.MergedContent!, "SCHEMA_VIEW_CONFIG_DIFF");
			var array = JsonNode.Parse(arrayJson)?.AsArray() ?? throw new InvalidOperationException("Expected view config array");
			var inserts = array
				.OfType<JsonObject>()
				.Where(static x => string.Equals(x["operation"]?.GetValue<string>(), "insert", StringComparison.Ordinal))
				.Where(static x => string.Equals(x["parentName"]?.GetValue<string>(), "SideAreaProfileContainer", StringComparison.Ordinal))
				.ToArray();

			Assert.That(inserts.Length, Is.EqualTo(2));
			Assert.That(inserts[0]["name"]?.GetValue<string>(), Is.EqualTo("Z_RemoteBase"));
			Assert.That(inserts[0]["index"]?.GetValue<int>(), Is.EqualTo(0));
			Assert.That(inserts[0]["values"]?["layoutConfig"]?["row"]?.GetValue<int>(), Is.EqualTo(1));
			Assert.That(inserts[1]["name"]?.GetValue<string>(), Is.EqualTo("A_LocalNew"));
			Assert.That(inserts[1]["index"]?.GetValue<int>(), Is.EqualTo(1));
			Assert.That(inserts[1]["values"]?["layoutConfig"]?["row"]?.GetValue<int>(), Is.EqualTo(2));
		}

		private static string ExtractClientUnitSectionJson(string content, string marker)
		{
			var token = $"/**{marker}*/";
			var firstMarker = content.IndexOf(token, StringComparison.Ordinal);
			if (firstMarker < 0)
			{
				throw new InvalidOperationException($"Marker '{marker}' not found.");
			}

			var current = firstMarker + token.Length;
			while (current < content.Length && char.IsWhiteSpace(content[current]))
			{
				current++;
			}

			if (current >= content.Length)
			{
				throw new InvalidOperationException($"Marker '{marker}' does not contain section content.");
			}

			var openChar = content[current];
			var closeChar = openChar switch
			{
				'[' => ']',
				'{' => '}',
				_ => throw new InvalidOperationException($"Marker '{marker}' is not followed by array/object.")
			};

			var closeIndex = FindMatchingBracket(content, current, openChar, closeChar);
			if (closeIndex < 0)
			{
				throw new InvalidOperationException($"Cannot find matching bracket for marker '{marker}'.");
			}

			return content.Substring(current, closeIndex - current + 1);
		}

		private static string BuildClientUnit(string viewConfig) => $$"""
			define("UsrProof", [], function() {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfig}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";

		private static int FindMatchingBracket(string source, int startIndex, char openChar, char closeChar)
		{
			var depth = 0;
			var inString = false;
			var stringDelimiter = '\0';
			var escaped = false;
			var inLineComment = false;
			var inBlockComment = false;

			for (var i = startIndex; i < source.Length; i++)
			{
				var ch = source[i];
				var next = i + 1 < source.Length ? source[i + 1] : '\0';

				if (inLineComment)
				{
					if (ch == '\n')
					{
						inLineComment = false;
					}

					continue;
				}

				if (inBlockComment)
				{
					if (ch == '*' && next == '/')
					{
						inBlockComment = false;
						i++;
					}

					continue;
				}

				if (inString)
				{
					if (escaped)
					{
						escaped = false;
						continue;
					}

					if (ch == '\\')
					{
						escaped = true;
						continue;
					}

					if (ch == stringDelimiter)
					{
						inString = false;
					}

					continue;
				}

				if (ch == '/' && next == '/')
				{
					inLineComment = true;
					i++;
					continue;
				}

				if (ch == '/' && next == '*')
				{
					inBlockComment = true;
					i++;
					continue;
				}

				if (ch is '\'' or '"' or '`')
				{
					inString = true;
					stringDelimiter = ch;
					continue;
				}

				if (ch == openChar)
				{
					depth++;
					continue;
				}

				if (ch == closeChar)
				{
					depth--;
					if (depth == 0)
					{
						return i;
					}
				}
			}

			return -1;
		}
	}
