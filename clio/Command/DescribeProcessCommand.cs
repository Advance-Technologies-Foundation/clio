using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using CommandLine;
using ErrorOr;

namespace Clio.Command;

/// <summary>
/// Options for reading an existing Creatio process into a structured graph ("read &amp; explain").
/// </summary>
[Verb("describe-process", Aliases = ["dp"], HelpText = "Read an existing Creatio process and return its structured graph (elements, flows, parameters).")]
public class DescribeProcessOptions : EnvironmentOptions {

	/// <summary>Process code (schema Name) as it appears in the process designer.</summary>
	[Option("process-code", Required = false, HelpText = "Process code (schema Name), e.g. UsrProcess_493d4c9.")]
	public string ProcessCode { get; set; }

	/// <summary>Process UId (GUID).</summary>
	[Option("process-uid", Required = false, HelpText = "Process UId (GUID).")]
	public string ProcessUid { get; set; }

	/// <summary>Process caption (display name).</summary>
	[Option("process-caption", Required = false, HelpText = "Process caption (display name).")]
	public string ProcessCaption { get; set; }

	/// <summary>Culture used to resolve localized captions.</summary>
	[Option("culture", Required = false, HelpText = "Culture used to resolve localized captions.", Default = "en-US")]
	public string Culture { get; set; } = "en-US";
}

/// <summary>
/// Reads an existing process's schema and prints a structured JSON graph (elements, flows, parameters)
/// so an AI agent can explain in plain language what the process does. The inverse of process generation.
/// </summary>
public class DescribeProcessCommand(IProcessSchemaReader schemaReader, IProcessGraphExtractor extractor, ILogger logger)
	: Command<DescribeProcessOptions> {

	private static readonly JsonSerializerOptions OutputOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// Reads the identified process and writes its structured description as JSON.
	/// </summary>
	/// <param name="options">Command options identifying the process and environment.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	public override int Execute(DescribeProcessOptions options) {
		int identityCount = 0;
		if (!string.IsNullOrWhiteSpace(options.ProcessCode)) { identityCount++; }
		if (!string.IsNullOrWhiteSpace(options.ProcessUid)) { identityCount++; }
		if (!string.IsNullOrWhiteSpace(options.ProcessCaption)) { identityCount++; }
		if (identityCount != 1) {
			logger.WriteError("Error: provide exactly one of --process-code, --process-uid, or --process-caption.");
			return 1;
		}

		ErrorOr<ProcessSchemaResponse> schema = schemaReader.Read(
			new ProcessIdentity(options.ProcessCode, options.ProcessUid, options.ProcessCaption));
		if (schema.IsError) {
			logger.WriteError($"Error: {schema.FirstError.Description}.");
			return 1;
		}

		ProcessDescription description = extractor.Extract(schema.Value, options.Culture);
		logger.WriteInfo(JsonSerializer.Serialize(description, OutputOptions));
		return 0;
	}
}
