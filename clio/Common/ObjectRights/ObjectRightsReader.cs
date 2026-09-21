using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Package;

namespace Clio.Common.ObjectRights;

/// <summary>
/// The external (portal) audience's object operation rights, mirroring the object-permissions grid row for
/// the fixed <c>All external users</c> role.
/// </summary>
public sealed record ExternalUsersOperationRights(bool CanRead, bool CanCreate, bool CanEdit, bool CanDelete);

/// <summary>
/// The result of reading one object's external-access state: whether the object was found, whether it is
/// administered by operation permissions at all, and (when it is) the external audience's rights on it.
/// </summary>
public sealed record ObjectRightsInfo(
	bool Found,
	string Name,
	string Caption,
	bool AdministratedByOperations,
	ExternalUsersOperationRights ExternalUsers,
	string ReadError = null);

/// <summary>
/// Reads whether the external (portal) audience has object operation access to a given object, using the
/// native Creatio <c>RightManagementService.svc/GetAdministratedObject</c> service — the same service the
/// System Designer "Object permissions" section uses. The read sibling of <see cref="ISectionServiceClient"/>.
/// </summary>
public interface IObjectRightsReader
{
	/// <summary>
	/// Reads the external-users object operation rights for the entity schema <paramref name="schemaName"/>.
	/// Resolves the schema UId (DataService SelectQuery over <c>SysSchema</c>) then calls
	/// <c>GetAdministratedObject</c>. When the schema cannot be resolved the result's <c>Found</c> is false;
	/// when the object is not administered by operation permissions it is treated as available to everyone
	/// (<c>AdministratedByOperations</c> = false, <c>ExternalUsers</c> = null).
	/// </summary>
	/// <param name="schemaName">The entity schema name to read.</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <returns>The object's external-access state.</returns>
	ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions);
}

/// <summary>
/// Client for the native Creatio <c>RightManagementService</c>. Resolves an entity schema name to its UId
/// via DataService (the clio name→UId convention) and reads the object's per-role operation rights.
/// </summary>
public class RightManagementServiceClient : CreatioServiceClient, IObjectRightsReader
{
	// Fixed platform role id for the portal audience; verified on stand and stable across Creatio installs.
	private static readonly Guid AllExternalUsersRoleId = new("720b771c-e7a7-4f31-9cfb-52cd21c3739f");

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	public RightManagementServiceClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	public ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions) {
		IReadOnlyList<Guid> candidateUIds;
		try {
			candidateUIds = ResolveEntitySchemaUIds(schemaName, requestOptions);
		}
		catch (Exception ex) {
			// A failed name→UId read for ONE object must not abort a whole fan-out; report it and move on.
			return new ObjectRightsInfo(true, schemaName, null, false, null, ReadError: ex.Message);
		}
		if (candidateUIds.Count == 0) {
			return new ObjectRightsInfo(false, schemaName, null, false, null);
		}

		// A granted/extended schema has more than one SysSchema (EntitySchemaManager) row — a base UId and a
		// replacing-schema UId — and GetAdministratedObject accepts only the administrable one, faulting with a
		// non-JSON "Request Error" page on the other (the order the rows come back in is not deterministic). So
		// try each candidate: the first that DESCRIBES an administered object wins; if none does but at least one
		// answered cleanly the object is simply not administered (available); only if every candidate faulted is
		// it a genuine read failure.
		bool anyCleanAnswer = false;
		string lastError = null;
		foreach (Guid candidate in candidateUIds) {
			GetAdministratedObjectResponse response;
			try {
				response = PostAndDeserialize<GetAdministratedObjectResponse>(
					ServiceUrlBuilder.KnownRoute.GetAdministratedObject,
					new GetAdministratedObjectRequest { SchemaUId = candidate },
					requestOptions);
			}
			catch (Exception ex) {
				lastError = ex.Message;
				continue;
			}
			anyCleanAnswer = true;
			AdministratedObjectData obj = response?.AdministratedObject;
			if (response is { Success: true } && obj is not null && obj.AdministratedByOperations) {
				AdministratedObjectOperationRight externalRow = obj.EntitySchemaOperationsRights?
					.FirstOrDefault(row => row.SysAdminUnit != null && row.SysAdminUnit.Id == AllExternalUsersRoleId);
				ExternalUsersOperationRights external = externalRow is null
					? null
					: new ExternalUsersOperationRights(externalRow.CanRead, externalRow.CanAppend, externalRow.CanEdit,
						externalRow.CanDelete);
				return new ObjectRightsInfo(true, obj.Name ?? schemaName, obj.Caption, true, external);
			}
		}

		// No candidate described an administered object: either every candidate faulted (read failure), or at
		// least one answered cleanly and the object is not administered by operation permissions (available).
		return anyCleanAnswer
			? new ObjectRightsInfo(true, schemaName, null, false, null)
			: new ObjectRightsInfo(true, schemaName, null, false, null, ReadError: lastError);
	}

	// Resolves an entity schema name to its candidate UId(s) via a DataService SelectQuery over SysSchema,
	// filtered to the EntitySchemaManager layer (the read-only name→UId convention used across clio). A
	// granted/extended schema has more than one row (base + replacing schema); all are returned as candidates.
	private IReadOnlyList<Guid> ResolveEntitySchemaUIds(string schemaName, CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery(
			"SysSchema",
			new[] { new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId") },
			new[] {
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "EntitySchemaManager", SelectQueryHelper.TextDataValueType)
			},
			20);

		SchemaUIdSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<SchemaUIdSelectResponse>(
			_applicationClient, _urlBuilder, query, requestOptions.TimeOut, requestOptions.MaxAttempts,
			requestOptions.RetryDelay);

		return response.Rows is null
			? Array.Empty<Guid>()
			: response.Rows.Select(row => row.UId).Where(uId => uId != Guid.Empty).Distinct().ToList();
	}

	private sealed class SchemaUIdSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")]
		public List<SchemaUIdRow> Rows { get; set; }
	}

	private sealed class SchemaUIdRow
	{
		[JsonPropertyName("UId")]
		public Guid UId { get; set; }
	}
}
