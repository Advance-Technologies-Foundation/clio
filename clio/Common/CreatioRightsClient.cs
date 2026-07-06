using System;
using System.Text.Json.Serialization;

namespace Clio.Common;

/// <summary>
/// Client for the native Creatio <c>RightsService</c> that reports whether the current user may execute a
/// named system operation. Mirrors the platform <c>RightsService.getCanExecuteOperation</c> contract.
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
}

/// <inheritdoc />
public class CreatioRightsClient : CreatioServiceClient, ICreatioRightsClient
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioRightsClient"/> class.
	/// </summary>
	public CreatioRightsClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
	}

	/// <inheritdoc />
	public bool GetCanExecuteOperation(string operationName, CreatioRequestOptions requestOptions) {
		CanExecuteOperationResponse response = PostAndDeserialize<CanExecuteOperationResponse>(
			ServiceUrlBuilder.KnownRoute.RightsGetCanExecuteOperation,
			new CanExecuteOperationRequest { Operation = operationName },
			requestOptions);
		return response?.Result ?? false;
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
}
