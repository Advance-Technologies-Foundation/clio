using System;
using Clio.Common.ObjectRights;

namespace Clio.Common;

/// <summary>
/// Client for the native Creatio <c>SectionService</c>. Grants object operation and record permissions
/// for a section object and its connected (lookup) entities in one call — the same operation the Freedom
/// UI page designer performs through its "Update object permissions now" action.
/// </summary>
public interface ISectionServiceClient
{
	/// <summary>
	/// Turns on operation and record permissions for the object <paramref name="entitySchemaName"/> and
	/// operation permissions for every entity connected to it through a lookup column, granting the
	/// external (portal) audience access, via <c>rest/SectionService/SetConnectedEntitiesAdministratedByEntity</c>.
	/// Only the root object name is sent; the server resolves the connected entities from the root schema.
	/// The call is asynchronous on the server (it recalculates rights) and can take tens of seconds.
	/// </summary>
	/// <param name="entitySchemaName">The root object (entity schema) name whose access is being granted.</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <exception cref="InvalidOperationException">The service returned an empty or non-JSON response.</exception>
	void SetConnectedEntitiesAdministratedByEntity(string entitySchemaName, CreatioRequestOptions requestOptions);
}

public class CreatioSectionServiceClient : CreatioServiceClient, ISectionServiceClient
{
	public CreatioSectionServiceClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
	}

	public void SetConnectedEntitiesAdministratedByEntity(string entitySchemaName,
		CreatioRequestOptions requestOptions) {
		// The live response envelope is not verified (a live rights write cannot be performed here), so
		// parse tolerantly: any valid JSON body deserializes into the empty response and counts as success.
		PostAndDeserialize<SetConnectedEntitiesAdministratedResponse>(
			ServiceUrlBuilder.KnownRoute.SetConnectedEntitiesAdministratedByEntity,
			new SetConnectedEntitiesAdministratedRequest { EntitySchemaName = entitySchemaName },
			requestOptions);
	}
}
