using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using CommandLine;
using ErrorOr;

namespace Clio.Command;

// NOTE: process reading is delegated to the server-side ProcessDesignService package (universal element
// typing incl. user-task schema names and parameter value sources). Requires the clioprocessbuilder package
// on the target environment.

/// <summary>
/// Options for reading an existing Creatio process into a structured graph ("read &amp; explain").
/// </summary>
[Verb("describe-business-process", Aliases = ["dp", "describe-process"], HelpText = "Read an existing Creatio process and return its structured graph (elements, flows, parameters).")]
[RequiresPackage("clioprocessbuilder", Hint = "This experimental feature requires the clioprocessbuilder package on the target environment.")]
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
/// Reads an existing process into a structured JSON graph (elements, flows, parameters) so an AI agent can
/// explain in plain language what the process does. The inverse of process generation. Delegates the read to
/// the server-side <c>ProcessDesignService</c> package, which types elements from the real object model
/// (including the specific user-task schema name and parameter value sources).
/// </summary>
public class DescribeProcessCommand(IProcessDescriber describer, ILogger logger)
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

		ErrorOr<DescribeProcessResult> description = describer.Describe(
			new ProcessIdentity(options.ProcessCode, options.ProcessUid, options.ProcessCaption), options.Culture);
		if (description.IsError) {
			logger.WriteError($"Error: {description.FirstError.Description}.");
			return 1;
		}

		logger.WriteInfo(JsonSerializer.Serialize(description.Value, OutputOptions));
		return 0;
	}
}
