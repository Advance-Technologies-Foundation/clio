using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.RecordRights;
using CommandLine;

namespace Clio.Command.RecordRights;

[Verb("get-record-rights", HelpText = "Read the record-level access rights of a single Creatio record")]
public class GetRecordRightsOptions : RemoteCommandOptions {

	[Option("entity", Required = true, HelpText = "Entity schema name of the target record (use with --record-id)")]
	public string Entity { get; set; }

	[Option("record-id", Required = true, HelpText = "Primary column value (record id) of the target record (use with --entity)")]
	public string RecordId { get; set; }
}

public class GetRecordRightsCommand : Command<GetRecordRightsOptions> {

	private readonly ICreatioRightsClient _rightsClient;
	private readonly ILogger _logger;

	public GetRecordRightsCommand(ICreatioRightsClient rightsClient, ILogger logger) {
		_rightsClient = rightsClient;
		_logger = logger;
	}

	public override int Execute(GetRecordRightsOptions options) {
		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		string tableName = RightsTableName(options.Entity);

		try {
			IReadOnlyList<RecordRightRow> rows = _rightsClient.GetRecordRights(
				tableName, options.RecordId, requestOptions);
			if (rows.Count == 0) {
				_logger.WriteInfo($"No record rights found for {options.Entity} '{options.RecordId}'.");
				return 0;
			}
			_logger.WriteInfo($"Record rights for {options.Entity} '{options.RecordId}':");
			foreach (RecordRightRow row in rows) {
				string grantee = row.SysAdminUnit?.DisplayValue ?? "(unknown)";
				string granteeValue = row.SysAdminUnit?.Value ?? "(unknown)";
				_logger.WriteInfo(
					$"  {RecordRightsConverter.OperationToName(row.Operation)} / {RecordRightsConverter.LevelToName(row.RightLevel)}"
					+ $" -> {grantee} ({granteeValue})");
			}
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// Resolves the physical record-rights table using the verified Sys<Entity>Right convention:
	// a Sys-prefixed entity becomes <Entity>Right; any other entity becomes Sys<Entity>Right.
	private static string RightsTableName(string entitySchemaName) =>
		entitySchemaName.StartsWith("Sys", StringComparison.Ordinal)
			? entitySchemaName + "Right"
			: "Sys" + entitySchemaName + "Right";
}
