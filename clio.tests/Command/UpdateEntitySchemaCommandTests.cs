using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Property("Module", "Command")]
internal sealed class UpdateEntitySchemaCommandTests : BaseClioModuleTests
{
	private UpdateEntitySchemaCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateEntitySchemaCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Applies every structured operation in order by mapping them onto existing column mutation options.")]
	public void Execute_CallsColumnManager_ForEachOperation_WhenOptionsAreValid() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"add","column-name":"UsrStatus","type":"Lookup","title":"Status","reference-schema-name":"UsrVehicleStatus","required":true}""",
				"""{"action":"modify","column-name":"UsrDueDate","title":"Due date","default-value-source":"None"}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid batch requests should execute through the existing column mutation flow");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 2
			&& mutations.ElementAt(0).Environment == "dev"
			&& mutations.ElementAt(0).Package == "UsrPkg"
			&& mutations.ElementAt(0).SchemaName == "UsrVehicle"
			&& mutations.ElementAt(0).Action == "add"
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Type == "Lookup"
			&& mutations.ElementAt(0).Title == "Status"
			&& mutations.ElementAt(0).ReferenceSchemaName == "UsrVehicleStatus"
			&& mutations.ElementAt(0).Required == true
			&& mutations.ElementAt(1).Environment == "dev"
			&& mutations.ElementAt(1).Package == "UsrPkg"
			&& mutations.ElementAt(1).SchemaName == "UsrVehicle"
			&& mutations.ElementAt(1).Action == "modify"
			&& mutations.ElementAt(1).ColumnName == "UsrDueDate"
			&& mutations.ElementAt(1).Title == "Due date"
			&& mutations.ElementAt(1).DefaultValueSource == "None"));
		_logger.Received(1).WriteInfo("Done");
	}

	[Test]
	[Description("Rejects empty operation batches before any remote mutation is attempted.")]
	public void Execute_ReturnsFailure_WhenNoOperationsWereProvided() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = []
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a batch update without operations is invalid");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("At least one operation is required")));
	}

	[Test]
	[Description("Trims operation titles and maps whitespace-only values to null before forwarding batch mutations.")]
	public void Execute_MapsTrimmedAndWhitespaceTitles_WhenBuildingMutations() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"modify","column-name":"UsrStatus","title":"  Status  "}""",
				"""{"action":"add","column-name":"UsrPriority","type":"Text","title":"   "}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "title normalization should preserve successful execution for valid operations");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 2
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Title == "Status"
			&& mutations.ElementAt(1).ColumnName == "UsrPriority"
			&& mutations.ElementAt(1).Action == "add"
			&& mutations.ElementAt(1).Title == null));
	}

	[Test]
	[Description("Maps structured default-value-config payloads onto batch mutation options without flattening them into legacy shorthand fields.")]
	public void Execute_Maps_DefaultValueConfig_WhenBuildingMutations() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"modify","column-name":"UsrStartDate","default-value-config":{"source":"SystemValue","value-source":"CurrentDateTime"}}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "structured default value configs should remain valid batch operations");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1
			&& mutations.ElementAt(0).ColumnName == "UsrStartDate"
			&& mutations.ElementAt(0).DefaultValueConfig != null
			&& mutations.ElementAt(0).DefaultValueConfig!.Source == "SystemValue"
			&& mutations.ElementAt(0).DefaultValueConfig!.ValueSource == "CurrentDateTime"
			&& mutations.ElementAt(0).DefaultValueSource == null
			&& mutations.ElementAt(0).DefaultValue == null));
	}

	[Test]
	[Description("Derives the internal scalar title from title-localizations when batch mutations omit legacy title, without synthesizing additional cultures.")]
	public void Execute_DerivesTitle_FromTitleLocalizations_WithoutCultureSynthesis() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"add","column-name":"UsrStatus","type":"Lookup","title-localizations":{"en-US":"Status"},"reference-schema-name":"UsrVehicleStatus"}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "localized batch mutations should stay valid without public scalar title input");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Title == "Status"
			&& mutations.ElementAt(0).TitleLocalizations != null
			&& mutations.ElementAt(0).TitleLocalizations!.ContainsKey("en-US")
			&& mutations.ElementAt(0).TitleLocalizations!["en-US"] == "Status"
			&& mutations.ElementAt(0).TitleLocalizations!.Count == 1));
	}

	[Test]
	[Description("Forwards the usage-type field from a batch operation onto the mapped column mutation options.")]
	public void Execute_Maps_UsageType_WhenBuildingMutations() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"modify","column-name":"UsrStatus","usage-type":"Advanced"}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a batch operation carrying usage-type should remain a valid modification");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).UsageType == "Advanced"));
	}

	[Test]
	[Description("Rejects malformed JSON operation payloads with a clear error before executing any mutation.")]
	public void Execute_ReturnsFailure_WhenOperationJsonIsInvalid() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"add","column-name":"UsrStatus","type":"Lookup""" ]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "malformed operation payloads should fail validation early");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 is not valid JSON.")));
	}

	[Test]
	[Description("Rejects an operation field the command does not know, naming the field and the nearest known field, before any remote mutation (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationHasUnknownField() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","colum-name":"UsrStatus","title":"Status"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a misspelled field would otherwise be silently dropped and the operation would target no column");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 has unknown field 'colum-name'.")
			&& message.Contains("Did you mean 'column-name'?")));
	}

	[Test]
	[Description("Accepts 'name' as an alias of 'column-name' in operation JSON, matching the MCP tool and create-entity-schema (ENG-101526).")]
	public void Execute_MapsNameAlias_ToColumnName() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","name":"UsrStatus","title":"Status"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "'name' is an accepted alias of 'column-name'");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Title == "Status"));
	}

	[Test]
	[Description("Rejects an operation that sets both 'column-name' and its alias 'name' to different values, before any remote mutation (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenNameAliasConflictsWithColumnName() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","name":"UsrOwner","title":"Status"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "two different column names in one operation are ambiguous");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("'column-name' ('UsrStatus')") && message.Contains("'name' ('UsrOwner')")));
	}

	[Test]
	[Description("Reports the unknown field even when another field of the same operation has a value of the wrong type, because field names are checked before typed deserialization (ENG-101526).")]
	public void Execute_ReportsUnknownField_WhenAnotherFieldHasWrongType() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","colum-name":"UsrStatus","required":"yes"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the operation is invalid on two counts");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("has unknown field 'colum-name'.") && message.Contains("Did you mean 'column-name'?")));
		_logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("is not valid JSON")));
	}

	[Test]
	[Description("Malformed operation JSON still reports that the payload is not valid JSON after field validation moved ahead of deserialization (ENG-101526).")]
	public void Execute_ReportsInvalidJson_WhenOperationIsMalformed() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","colum-name":"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a payload that is not JSON cannot be applied");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 is not valid JSON.")));
	}

	[Test]
	[Description("Strips control characters such as a terminal escape sequence and a line break from an unknown field name before echoing it (ENG-101526).")]
	public void Execute_StripsControlCharacters_FromEchoedUnknownField() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","bad\u001b[2Jkey\nnext":"x"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the field is unknown");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("unknown field 'bad[2Jkeynext'")
			&& !message.Contains('\u001b')
			&& !message.Contains('\n')));
	}

	[Test]
	[Description("Cuts a very long unknown field name to 64 characters plus an ellipsis before echoing it (ENG-101526).")]
	public void Execute_TruncatesVeryLongEchoedUnknownField() {
		// Arrange
		string longKey = new('k', 500);
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [$$"""{"action":"modify","column-name":"UsrStatus","{{longKey}}":"x"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the field is unknown");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains($"unknown field '{new string('k', 64)}...'")
			&& !message.Contains(new string('k', 65))));
	}

	[Test]
	[Description("Strips invisible Unicode format characters (bidirectional override U+202E, zero-width space U+200B, isolates U+2066-U+2069) from an echoed name, so the message cannot hide or reorder what it shows (ENG-101526).")]
	public void SanitizeForMessage_RemovesUnicodeFormatCharacters() {
		// Arrange
		string value = "ab\u202Ecd\u200Bef\u2066gh\u2067ij\u2068kl\u2069mn";

		// Act
		string result = UpdateEntitySchemaCommand.SanitizeForMessage(value);

		// Assert
		result.Should().Be("abcdefghijklmn",
			because: "format characters are invisible in a terminal and would let a field name disguise itself");
	}

	[Test]
	[Description("Cuts a long echoed name before a surrogate pair that straddles the 64-character limit instead of splitting it into a lone high surrogate (ENG-101526).")]
	public void SanitizeForMessage_DoesNotSplitSurrogatePairAtCut() {
		// Arrange
		string value = new string('k', 63) + "\U0001F600" + new string('z', 10);

		// Act
		string result = UpdateEntitySchemaCommand.SanitizeForMessage(value);

		// Assert
		result.Should().Be(new string('k', 63) + "...",
			because: "the emoji occupies characters 64 and 65, so keeping only its high surrogate would emit invalid UTF-16");
		result.Any(char.IsSurrogate).Should().BeFalse(
			because: "no half of a surrogate pair may survive the cut");
	}

	[Test]
	[Description("Keeps a surrogate pair that ends exactly at the 64-character limit (ENG-101526).")]
	public void SanitizeForMessage_KeepsSurrogatePairEndingAtCut() {
		// Arrange
		string value = new string('k', 62) + "\U0001F600" + new string('z', 10);

		// Act
		string result = UpdateEntitySchemaCommand.SanitizeForMessage(value);

		// Assert
		result.Should().Be(new string('k', 62) + "\U0001F600...",
			because: "the pair fits entirely inside the first 64 characters, so it is kept whole");
	}

	[TestCase("[]")]
	[TestCase("42")]
	[TestCase("\"modify\"")]
	[Description("Rejects a well-formed operation payload that is not a JSON object with a message saying it must be an object, not that it is invalid JSON (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationIsNotJsonObject(string payload) {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [payload]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "only a JSON object describes an operation");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 must be a JSON object.")));
		_logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("is not valid JSON")));
	}

	[Test]
	[Description("Reports a well-formed operation whose field has a value of the wrong type as an invalid value at its JSON path, not as invalid JSON (ENG-101526).")]
	public void Execute_ReportsInvalidValueWithPath_WhenFieldHasWrongType() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","required":"yes"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "'required' takes a boolean, so the operation cannot be applied");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 has an invalid value at JSON path '$.required'.")));
		_logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("is not valid JSON")));
	}

	[Test]
	[Description("Accepts an operation that sets 'column-name' and its alias 'name' to the same value (ENG-101526).")]
	public void Execute_Succeeds_WhenNameAliasEqualsColumnName() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","name":"UsrStatus","title":"Status"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the same column named twice is not ambiguous");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1 && mutations.ElementAt(0).ColumnName == "UsrStatus"));
	}

	[Test]
	[Description("Preserves semicolons inside structured JSON --operation payloads so valid titles and defaults are not split by the command-line parser.")]
	public void Parse_Should_Preserve_Semicolons_In_Json_Operation_Payload() {
		// Arrange
		string jsonOperation = """{"action":"modify","column-name":"Status","title":"Needs;Review","default-value-source":"Const","default-value":"A;B"}""";
		string[] arguments = [
			"--package", "UsrPkg",
			"--schema-name", "UsrVehicle",
			"--operation", jsonOperation
		];
		UpdateEntitySchemaOptions? parsedOptions = null;

		// Act
		ParserResult<UpdateEntitySchemaOptions> parseResult = Parser.Default
			.ParseArguments<UpdateEntitySchemaOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed,
			because: "valid structured JSON operation payloads should remain intact during CLI parsing");
		parsedOptions.Should().NotBeNull(
			because: "a successful parse should produce update-entity-schema options");
		parsedOptions!.Operations.Should().BeEquivalentTo([jsonOperation],
			because: "semicolons inside a JSON title or default value are part of the operation payload, not separators");
	}

	private sealed class CultureScope : IDisposable {
		private readonly CultureInfo _originalCurrentCulture;
		private readonly CultureInfo _originalCurrentUiCulture;

		public CultureScope(string cultureName) {
			_originalCurrentCulture = CultureInfo.CurrentCulture;
			_originalCurrentUiCulture = CultureInfo.CurrentUICulture;
			CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;
		}

		public void Dispose() {
			CultureInfo.CurrentCulture = _originalCurrentCulture;
			CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
		}
	}
}
