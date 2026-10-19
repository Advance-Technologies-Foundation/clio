using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Package;

namespace Clio.Common.ObjectRights;

/// <summary>Counts the records of an object, the fact a user needs before deciding to apply record rules to them.</summary>
public interface IObjectRecordCounter {
	/// <summary>Counts the records of <paramref name="schemaName"/> that the caller's account can read.</summary>
	/// <param name="schemaName">The entity schema name.</param>
	/// <param name="requestOptions">The deadline, and a timeout the count is further capped below; it is sent once.</param>
	/// <returns>The number of records.</returns>
	/// <exception cref="InvalidOperationException">The query failed or its answer could not be read.</exception>
	long CountRecords(string schemaName, CreatioRequestOptions requestOptions);
}

/// <summary>Counts records with one DataService <c>COUNT(Id)</c> query, under the caller's rights.</summary>
public sealed class ObjectRecordCounter : IObjectRecordCounter {

	/// <summary>The most a record count may take, in milliseconds: the count is only informational.</summary>
	internal const int CountTimeOutMilliseconds = 10_000;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>Creates the counter.</summary>
	public ObjectRecordCounter(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	public long CountRecords(string schemaName, CreatioRequestOptions requestOptions) {
		// Best effort: one short attempt. The count is informational, and a COUNT that timed out once (the SQL keeps
		// running server-side) would most likely time out again, holding the call for minutes.
		CreatioRequestOptions nextOptions = requestOptions.ForNextRequest();
		CreatioRequestOptions sendOptions = nextOptions with {
			MaxAttempts = 1, TimeOut = Math.Min(nextOptions.TimeOut, CountTimeOutMilliseconds)
		};
		CountSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<CountSelectResponse>(_applicationClient,
			_urlBuilder, SelectQueryHelper.BuildCountQuery(schemaName, "Count"), sendOptions.TimeOut,
			sendOptions.MaxAttempts, sendOptions.RetryDelay);
		return response.Rows?.FirstOrDefault()?.Count
			?? throw new InvalidOperationException($"The record count of '{schemaName}' returned no row.");
	}

	private sealed class CountSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<CountRow> Rows { get; set; }
	}

	private sealed class CountRow {
		[JsonPropertyName("Count")]
		public long Count { get; set; }
	}
}
