using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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

	private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";

	internal static string WorkPath(params string[] segments) => Path.Combine([Root, "work", .. segments]);

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

	[Test]
	[Description("Strips non-BMP format characters such as the tag character U+E0041, which occupy a surrogate pair, from an echoed name (ENG-101526).")]
	public void SanitizeForMessage_RemovesNonBmpFormatCharacters() {
		// Arrange
		string value = "ab\U000E0041cd\U000E007Fef";

		// Act
		string result = UpdateEntitySchemaCommand.SanitizeForMessage(value);

		// Assert
		result.Should().Be("abcdef",
			because: "tag characters are invisible format characters even though each one takes two UTF-16 units");
	}

	[Test]
	[Description("Drops lone high and low surrogates from an echoed name while keeping a valid surrogate pair (ENG-101526).")]
	public void SanitizeForMessage_DropsLoneSurrogates() {
		// Arrange
		string value = "a\uD800b\uDC00c\U0001F600d\uD83D";

		// Act
		string result = UpdateEntitySchemaCommand.SanitizeForMessage(value);

		// Assert
		result.Should().Be("abc\U0001F600d",
			because: "a lone surrogate is invalid UTF-16 and must not be echoed, but a well-formed pair is a real character");
	}

	[Test]
	[Description("Reports an operation whose field name is an escaped lone surrogate as invalid JSON instead of leaking a serializer error (ENG-101526).")]
	public void Execute_ReportsInvalidJson_WhenFieldNameIsLoneSurrogate() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","\uD800":1}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a field name that is not valid UTF-16 cannot be read as text");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message == "Operation payload at index 0 is not valid JSON."));
	}

	[Test]
	[Description("Reports an operation whose string value is an escaped lone surrogate as an invalid value at its JSON path instead of leaking a serializer error (ENG-101526).")]
	public void Execute_ReportsInvalidValue_WhenFieldValueIsLoneSurrogate() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"Usr\uDC00Status"}"""]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a string value that is not valid UTF-16 cannot be read as text");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message == "Operation payload at index 0 has an invalid value at JSON path '$.column-name'."));
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

	[TestCase("""{"Action":"modify","COLUMN-NAME":"UsrStatus","Title":"Status"}""")]
	[TestCase("""{"ACTION":"modify","Name":"UsrStatus","TITLE":"Status"}""")]
	[Description("Known operation fields, including the 'name' alias, are matched case-insensitively by the unknown-field check, keeping the case-insensitive JSON contract (ENG-101526).")]
	public void Execute_AcceptsKnownFields_WrittenInMixedCase(string payload) {
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
		result.Should().Be(0, because: "a known field written in another case is not an unknown field");
		_logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("unknown field")));
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Title == "Status"));
	}

	[Test]
	[Description("An unknown field in a later operation fails the whole batch before anything is saved, even when an earlier operation is valid, and the error names the failing index (ENG-101526).")]
	public void Execute_FailsWholeBatch_WhenLaterOperationHasUnknownField() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"modify","column-name":"UsrStatus","title":"Status"}""",
				"""{"action":"modify","colum-name":"UsrOwner","title":"Owner"}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "one invalid operation fails the batch");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 1 has unknown field 'colum-name'.")));
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

	[Test]
	[Description("Reads a multi-line, BOM-prefixed JSON array from --operations-file and appends its operations after the --operation and --operations values, in that order (ENG-101526).")]
	public void Execute_AppendsOperationsFromFile_AfterOperationAndOperationsValues() {
		// Arrange
		string filePath = WorkPath("operations.json");
		FileSystem.AddFile(filePath, new System.IO.Abstractions.TestingHelpers.MockFileData(
			"\uFEFF[\r\n  {\"action\":\"add\",\"column-name\":\"UsrFromFile1\",\"type\":\"Text\",\"title\":\"First\"},\r\n"
			+ "  {\"action\":\"modify\",\"name\":\"UsrFromFile2\",\"title\":\"Second\"}\r\n]\r\n"));
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrFromOperation","title":"Op"}"""],
			OperationsJson = """[{"action":"modify","column-name":"UsrFromOperations","title":"Ops"}]""",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid operations file is an accepted operation source");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Select(mutation => mutation.ColumnName).SequenceEqual(new[] {
				"UsrFromOperation", "UsrFromOperations", "UsrFromFile1", "UsrFromFile2"
			})
			&& mutations.ElementAt(2).Action == "add"
			&& mutations.ElementAt(2).Type == "Text"
			&& mutations.ElementAt(3).Title == "Second"));
	}

	[Test]
	[Description("Uses --operations-file as the only operation source (ENG-101526).")]
	public void Execute_UsesOperationsFile_WhenItIsTheOnlySource() {
		// Arrange
		string filePath = WorkPath("only.json");
		FileSystem.AddFile(filePath, new System.IO.Abstractions.TestingHelpers.MockFileData(
			"[\n  {\"action\":\"remove\",\"column-name\":\"UsrObsolete\"}\n]"));
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a file alone satisfies the at-least-one-operation rule");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1 && mutations.Single().ColumnName == "UsrObsolete"
			&& mutations.Single().Action == "remove"));
	}

	[Test]
	[Description("A missing --operations-file fails before anything is saved, naming the option and the path (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsFileIsMissing() {
		// Arrange
		string filePath = WorkPath("missing.json");
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an operations file that does not exist cannot be applied");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--operations-file") && message.Contains(filePath)));
	}

	[TestCase("not json at all")]
	[TestCase("""{"action":"add","column-name":"UsrX","type":"Text"}""")]
	[TestCase("null")]
	[Description("Content that is not a JSON array fails before anything is saved, naming the option and the path (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsFileIsNotJsonArray(string content) {
		// Arrange
		string filePath = WorkPath("bad.json");
		FileSystem.AddFile(filePath, new System.IO.Abstractions.TestingHelpers.MockFileData(content));
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","title":"Status"}"""],
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the file must hold a JSON array of operation objects");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--operations-file") && message.Contains(filePath)));
	}

	[Test]
	[Description("A missing --operations-file is echoed without control or format characters, and a path longer than a schema name is still named in full (ENG-101526).")]
	public void Execute_SanitizesOperationsFilePathInError() {
		// Arrange
		string longFolder = new('d', 80);
		string filePath = WorkPath(longFolder, "ops" + "\u001b[31m\u202E" + "missing.json");
		string expectedPath = WorkPath(longFolder, "ops[31mmissing.json");
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an operations file that does not exist cannot be applied");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains($"'{expectedPath}'") && !message.Contains('\u001b') && !message.Contains('\u202E')));
	}

	[Test]
	[Description("The parser accepts --operations-file as an operation source (ENG-101526).")]
	public void Parse_Should_AcceptOperationsFile() {
		// Arrange
		string[] arguments = ["--package", "UsrPkg", "--schema-name", "UsrVehicle", "--operations-file", "ops.json"];
		UpdateEntitySchemaOptions? parsedOptions = null;

		// Act
		ParserResult<UpdateEntitySchemaOptions> parseResult = Parser.Default
			.ParseArguments<UpdateEntitySchemaOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed, because: "--operations-file is a declared option");
		parsedOptions!.OperationsFile.Should().Be("ops.json", because: "the path must reach the command unchanged");
	}

	[Test]
	[Description("The --operation help describes several values after one --operation instead of repeating the flag, which the parser rejects (ENG-101526).")]
	public void OperationHelpText_Should_DescribeSeveralValuesAfterOneOption() {
		// Arrange
		System.Reflection.PropertyInfo property = typeof(UpdateEntitySchemaOptions)
			.GetProperty(nameof(UpdateEntitySchemaOptions.Operations))!;

		// Act
		OptionAttribute attribute = (OptionAttribute)Attribute.GetCustomAttribute(property, typeof(OptionAttribute))!;

		// Assert
		attribute.HelpText.Should().NotContain("Repeat the option",
			because: "a repeated --operation fails with 'Option is defined multiple times'");
		attribute.HelpText.Should().Contain("after one --operation",
			because: "the accepted form is several values after a single --operation");
	}

	[Test]
	[Description("Several values after one --operation, the form the help now documents, parse in input order (ENG-101526).")]
	public void Parse_Should_AcceptSeveralValuesAfterOneOperation() {
		// Arrange
		string first = """{"action":"modify","column-name":"UsrA","title":"A"}""";
		string second = """{"action":"modify","column-name":"UsrB","title":"B"}""";
		string[] arguments = ["--package", "UsrPkg", "--schema-name", "UsrVehicle", "--operation", first, second];
		UpdateEntitySchemaOptions? parsedOptions = null;

		// Act
		ParserResult<UpdateEntitySchemaOptions> parseResult = Parser.Default
			.ParseArguments<UpdateEntitySchemaOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed, because: "several values after one --operation is the documented form");
		parsedOptions!.Operations.Should().Equal([first, second], because: "every value must reach the command in order");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("An operations file in an ANSI code page (cp1251, as Windows PowerShell 5.1 Set-Content writes it) fails before anything is saved instead of saving its non-ASCII caption as replacement characters, with or without a UTF-8 BOM (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsFileIsNotUtf8(bool withUtf8Bom) {
		// Arrange
		string filePath = WorkPath("cp1251.json");
		byte[] prefix = Encoding.ASCII.GetBytes("[{\"action\":\"add\",\"column-name\":\"UsrStatus\",\"type\":\"Text\",\"title\":\"");
		byte[] statusInCp1251 = [0xD1, 0xF2, 0xE0, 0xF2, 0xF3, 0xF1];
		byte[] suffix = Encoding.ASCII.GetBytes("\"}]");
		byte[] bom = withUtf8Bom ? [0xEF, 0xBB, 0xBF] : [];
		FileSystem.AddFile(filePath, new MockFileData([.. bom, .. prefix, .. statusInCp1251, .. suffix]));
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "bytes that are not UTF-8 must not be decoded into replacement characters and saved");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError($"--operations-file '{filePath}' is not valid UTF-8.");
	}

	[Test]
	[Description("A UTF-16 LE file with a byte order mark (Windows PowerShell 5.1 Out-File default) is decoded as UTF-16 and keeps its non-ASCII caption (ENG-101526).")]
	public void Execute_ReadsUtf16LittleEndianOperationsFileWithBom() {
		// Arrange
		string filePath = WorkPath("utf16.json");
		const string json = "[\r\n  {\"action\":\"add\",\"column-name\":\"UsrStatus\",\"type\":\"Text\",\"title\":\"Статус\"}\r\n]";
		FileSystem.AddFile(filePath, new MockFileData([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(json)]));
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a UTF-16 file announced by its BOM is a readable operations file");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 1
			&& mutations.Single().ColumnName == "UsrStatus"
			&& mutations.Single().Title == "Статус"));
	}

	[TestCase("")]
	[TestCase("   \r\n\t ")]
	[Description("An empty or whitespace-only operations file fails before anything is saved, naming the option and the path (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsFileIsEmpty(string content) {
		// Arrange
		string filePath = WorkPath("empty.json");
		FileSystem.AddFile(filePath, new MockFileData(content));
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a file without a JSON array holds no operations");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError($"--operations-file '{filePath}' is not a valid JSON array of operations.");
	}

	[Test]
	[Description("An operations file holding an array of non-objects fails before anything is saved, naming the option, the path and the index within the file (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsFileArrayHoldsNonObjects() {
		// Arrange
		string filePath = WorkPath("numbers.json");
		FileSystem.AddFile(filePath, new MockFileData("[1,2]"));
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"modify","column-name":"UsrStatus","title":"Status"}"""],
			OperationsFile = filePath
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "every operation must be a JSON object");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError($"--operations-file '{filePath}' item at index 0 is not a JSON object.");
	}

	[TestCase("not json at all")]
	[TestCase("""{"action":"add","column-name":"UsrX","type":"Text"}""")]
	[Description("An --operations value that is not a JSON array fails before anything is saved, naming the option (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsValueIsNotJsonArray(string value) {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsJson = value
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "--operations must hold a JSON array of operation objects");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError("--operations value is not a valid JSON array of operations.");
	}

	[Test]
	[Description("Repeating --operation is rejected by the parser, which is why the help documents several values after one --operation (ENG-101526).")]
	public void Parse_Should_RejectRepeatedOperationOption() {
		// Arrange
		string[] arguments = [
			"--package", "UsrPkg", "--schema-name", "UsrVehicle",
			"--operation", """{"action":"modify","column-name":"UsrA","title":"A"}""",
			"--operation", """{"action":"modify","column-name":"UsrB","title":"B"}"""
		];
		using Parser parser = new(settings => settings.HelpWriter = null);

		// Act
		ParserResult<UpdateEntitySchemaOptions> parseResult = parser.ParseArguments<UpdateEntitySchemaOptions>(arguments);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.NotParsed,
			because: "the parser does not allow the same option twice");
		((NotParsed<UpdateEntitySchemaOptions>)parseResult).Errors.Should()
			.Contain(error => error.Tag == ErrorType.RepeatedOptionError,
				because: "the failure is the repeated option, not a missing value");
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

[TestFixture]
[NonParallelizable]
[Property("Module", "Command")]
internal sealed class UpdateEntitySchemaCommandUnreadableFileTests : BaseClioModuleTests
{
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;
	private Clio.Common.IFileSystem _fileSystem;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => _fileSystem);
	}

	[TestCase(typeof(IOException))]
	[TestCase(typeof(UnauthorizedAccessException))]
	[Description("A read failure of an existing operations file (locked file, denied access) fails before anything is saved, naming the option and the sanitized path instead of the raw .NET message (ENG-101526).")]
	public void Execute_ReturnsFailure_WhenOperationsFileCannotBeRead(Type exceptionType) {
		// Arrange
		string filePath = UpdateEntitySchemaCommandTests.WorkPath("locked\u001b.json");
		_fileSystem.ExistsFile(filePath).Returns(true);
		_fileSystem.ReadAllBytes(filePath).Throws((Exception)Activator.CreateInstance(exceptionType, "raw failure")!);
		UpdateEntitySchemaCommand command = Container.GetRequiredService<UpdateEntitySchemaCommand>();
		UpdateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			OperationsFile = filePath
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an unreadable operations file cannot be applied");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(
			$"--operations-file '{UpdateEntitySchemaCommandTests.WorkPath("locked.json")}' could not be read.");
	}
}
