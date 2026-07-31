using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clio.Common.RecordRights;

namespace Clio.Common;

/// <summary>
/// Client for the native Creatio <c>RightsService</c> that reports whether the current user may execute a
/// named system operation, reads record-level access rights, and applies a merged rights set. Mirrors the
/// platform <c>RightsService</c> contract.
/// </summary>
public interface ICreatioRightsClient
{
	/// <summary>
	/// Returns whether the current user can execute the named system operation via
	/// <c>rest/RightsService/GetCanExecuteOperation</c>.
	/// </summary>
	/// <param name="operationName">The system operation name (for example <c>CanManageThemes</c>).</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <returns><c>true</c> when the operation is permitted; otherwise <c>false</c>.</returns>
	/// <exception cref="InvalidOperationException">The service returned an empty or non-JSON response.</exception>
	bool GetCanExecuteOperation(string operationName, CreatioRequestOptions requestOptions);

	/// <summary>
	/// Reads the record-level access rights rows for a single record via
	/// <c>rest/RightsService/GetRecordRights</c>.
	/// </summary>
	/// <param name="tableName">The physical rights table name (for example <c>SysContactRight</c>).</param>
	/// <param name="recordId">The primary column value (record id) whose rights are read.</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <returns>The rights rows for the record; an empty list when the service returns none.</returns>
	/// <exception cref="InvalidOperationException">The service returned an empty or non-JSON response.</exception>
	IReadOnlyList<RecordRightRow> GetRecordRights(string tableName, string recordId,
		CreatioRequestOptions requestOptions);

	/// <summary>
	/// Applies record-level access rights changes for a single record via <c>rest/RightsService/ApplyChanges</c>.
	/// The server acts only on the rows it is sent (an <c>isNew</c> row is inserted, an <c>isDeleted</c> row is
	/// deleted, any other row is updated) and never touches absent rows, so the caller sends only the changed
	/// rows — not the full current set.
	/// </summary>
	/// <param name="record">The entity/record the rights belong to.</param>
	/// <param name="recordRights">The changed rights rows to persist.</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <exception cref="InvalidOperationException">The service returned an empty or non-JSON response.</exception>
	void ApplyChanges(ApplyChangesRecordRef record, IReadOnlyList<RecordRightRow> recordRights,
		CreatioRequestOptions requestOptions);
}

public class CreatioRightsClient : CreatioServiceClient, ICreatioRightsClient
{
	public CreatioRightsClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
	}

	public bool GetCanExecuteOperation(string operationName, CreatioRequestOptions requestOptions) {
		CanExecuteOperationResponse response = PostAndDeserialize<CanExecuteOperationResponse>(
			ServiceUrlBuilder.KnownRoute.RightsGetCanExecuteOperation,
			new CanExecuteOperationRequest { Operation = operationName },
			requestOptions);
		return response?.Result ?? false;
	}

	public IReadOnlyList<RecordRightRow> GetRecordRights(string tableName, string recordId,
		CreatioRequestOptions requestOptions) {
		GetRecordRightsResponse response = PostAndDeserialize<GetRecordRightsResponse>(
			ServiceUrlBuilder.KnownRoute.RightsGetRecordRights,
			new GetRecordRightsRequest { TableName = tableName, RecordId = recordId },
			requestOptions);
		return response?.Rows ?? Array.Empty<RecordRightRow>();
	}

	public void ApplyChanges(ApplyChangesRecordRef record, IReadOnlyList<RecordRightRow> recordRights,
		CreatioRequestOptions requestOptions) {
		// The live ApplyChanges response envelope is not verified (a live write cannot be performed here),
		// so parse tolerantly: any valid JSON body deserializes into the empty response and counts as success.
		PostAndDeserialize<ApplyChangesResponse>(
			ServiceUrlBuilder.KnownRoute.RightsApplyChanges,
			new ApplyChangesRequest { Record = record, RecordRights = recordRights },
			requestOptions);
	}

	private sealed record CanExecuteOperationRequest
	{
		[JsonPropertyName("operation")]
		public string Operation { get; init; }
	}

	private sealed record CanExecuteOperationResponse
	{
		[JsonPropertyName("GetCanExecuteOperationResult")]
		public bool? Result { get; init; }
	}

	private sealed record ApplyChangesResponse
	{
	}
}
