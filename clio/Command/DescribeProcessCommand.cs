using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;

namespace Clio.Command;

// NOTE: process reading is delegated to the server-side ProcessDesignService package (universal element
// typing incl. user-task schema names and parameter value sources). Requires the CrtProcessBuilder package
// on the target environment.

/// <summary>
/// Options for reading an existing Creatio process into a structured graph ("read &amp; explain").
/// Consumed by the MCP <c>describe-business-process</c> tool, which sets these properties directly.
/// </summary>
[RequiresPackage(BundledPackages.ProcessBuilderPackageName,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public class DescribeProcessOptions : EnvironmentOptions {

	/// <summary>Process code (schema Name) as it appears in the process designer.</summary>
	public string ProcessName { get; set; }

	/// <summary>Process UId (GUID).</summary>
	public string ProcessUid { get; set; }

	/// <summary>Process caption (display name).</summary>
	public string ProcessCaption { get; set; }

	/// <summary>Culture used to resolve localized captions.</summary>
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

	/// <summary>
	/// The options this command's JSON output is written with — <c>internal</c> so tests can assert against the
	/// REAL object rather than a copy.
	/// <para>Not a formality: two tests claimed to re-serialize "with the SAME options DescribeProcessCommand
	/// uses" via a hand copy, and deleting <c>DefaultIgnoreCondition</c> here left the whole unit suite green
	/// while every plain flow began shipping <c>"condition": null</c> to the caller — which is precisely what
	/// those tests exist to prevent. A copy of a value cannot pin the value.</para>
	/// <para>COMPACT and with the relaxed encoder on purpose (ENG-99970). The description travels to an agent
	/// as a STRING inside the command result, so it is JSON encoded a second time: indentation, line breaks
	/// and every escaped character are paid twice. Indented, the graph of a ten-element process reached
	/// 53-62 thousand characters of result text - over Claude Code's inline limit, so every describe of the
	/// measured process builds was spilled to a file and grepped back, 14-26 Grep calls per run. Compact
	/// JSON is the same value, and this command has no CLI verb whose human reader it would inconvenience.
	/// The MCP result encoder keeps a non-ASCII caption as its characters instead of a <c>\uXXXX</c> sequence
	/// whose backslash the outer encoding then doubles, and still escapes invisible Format characters.</para>
	/// </summary>
	internal static readonly JsonSerializerOptions OutputOptions = new() {
		WriteIndented = false,
		Encoder = Clio.Command.McpServer.McpResultJsonEncoder.Instance,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// Reads the identified process and writes its structured description as JSON.
	/// </summary>
	/// <param name="options">Command options identifying the process and environment.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	public override int Execute(DescribeProcessOptions options) {
		int identityCount = 0;
		if (!string.IsNullOrWhiteSpace(options.ProcessName)) { identityCount++; }
		if (!string.IsNullOrWhiteSpace(options.ProcessUid)) { identityCount++; }
		if (!string.IsNullOrWhiteSpace(options.ProcessCaption)) { identityCount++; }
		if (identityCount != 1) {
			logger.WriteError("Error: provide exactly one of --process-name, --process-uid, or --process-caption.");
			return 1;
		}

		ErrorOr<DescribeProcessResult> description = describer.Describe(
			new ProcessIdentity(options.ProcessName, options.ProcessUid, options.ProcessCaption), options.Culture);
		if (description.IsError) {
			logger.WriteError($"Error: {description.FirstError.Description}.");
			return 1;
		}

		logger.WriteInfo(JsonSerializer.Serialize(OmitDecodedFilterPayloads(description.Value), OutputOptions));
		return 0;
	}

	/// <summary>
	/// The note written in place of a raw filter value that <see cref="DescribedElement.Filter"/> reports decoded.
	/// </summary>
	internal const string DecodedFilterNote = "decoded into the element's filter";

	// The data-element parameter whose stored value is the raw platform filter the server decodes into Filter. A
	// signal start's EntityFilters is a design-mode property, not a parameter, so it never reaches this list.
	private const string DataSourceFiltersParameterName = "DataSourceFilters";

	/// <summary>
	/// Leaves out the raw platform filter of every element whose decoded <see cref="DescribedElement.Filter"/> the
	/// server judged COMPLETE (<see cref="DescribedElement.FilterDecodedCompletely"/>), and says so on the parameter
	/// (ENG-99970).
	/// </summary>
	/// <remarks>
	/// The raw value is the platform's serialized FilterGroup, a JSON string nested inside another: about 1 900
	/// characters for one Read data element, 16-27% of a measured describe result, and - when the decode is
	/// complete - a duplicate of the ~300-character <c>filter</c> beside it. No measured agent read it.
	/// <para>A non-null <c>Filter</c> is NOT enough: the server's reader is best-effort and returns a filter while
	/// reading an Exists or Between leaf as <c>equal</c>, keeping one value of a multi-value lookup, or reading a
	/// disabled condition as active - and the raw value is then the only full record, which an agent resending
	/// <c>filter</c> to <c>setFilter</c> would otherwise overwrite without knowing. So the value is left out only on
	/// the server's <c>true</c>; <c>false</c>, and the absent flag of an older <c>CrtProcessBuilder</c>, keep it, as
	/// does a null <c>Filter</c> (a legacy filter describe cannot decode at all). Changed on the result being
	/// written, which this command owns; the describer returns a fresh model per call and clio's own read-backs
	/// (create, modify, <c>AccessRightsBlockExpectation</c>) run their own describe, so none of them sees it.</para>
	/// </remarks>
	internal static DescribeProcessResult OmitDecodedFilterPayloads(DescribeProcessResult result) {
		IEnumerable<DescribedParameter> rawFilters = (result?.Elements ?? [])
			.Where(element => element?.Filter is not null && element.FilterDecodedCompletely == true
				&& element.Parameters is not null)
			.SelectMany(element => element.Parameters)
			.Where(parameter => parameter is not null
				&& !string.IsNullOrEmpty(parameter.Value)
				&& string.Equals(parameter.Name, DataSourceFiltersParameterName, StringComparison.OrdinalIgnoreCase));
		foreach (DescribedParameter parameter in rawFilters) {
			parameter.Value = null;
			parameter.ValueOmitted = DecodedFilterNote;
		}
		return result;
	}
}
