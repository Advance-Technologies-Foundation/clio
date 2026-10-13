using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Clio.Package;

namespace Clio.Common;

/// <summary>
/// Reads the newest rows of the environment's <c>CompilationHistory</c>, with the time each one was written in UTC.
/// </summary>
/// <remarks>
/// <para>
/// A compile writes one row per project it builds, when that project's build ends, so a row's time is when that part
/// of a compile FINISHED. That is what <see cref="ICompilationResultReader"/> lacks: its verdict carries no time, so a
/// caller cannot tell the build it started from an earlier one. These rows can be matched to a compile by time.
/// </para>
/// <para>
/// <b>The time is converted with the session's own offset, not taken from OData (ENG-102333).</b> DataService
/// returns <c>CreatedOn</c> in the session user's time zone with no offset marker, and the same session's
/// <c>ApplicationInfoService.GetApplicationInfo</c> reports that zone's offset (<c>userTimezoneOffset</c>, from
/// <c>CurrentUser.GetTimeZoneOffset()</c>). OData looked like the simpler source, but the platform configures it with
/// <c>SetTimeZoneInfo(TimeZoneInfo.Utc)</c>, which LABELS whatever the entity layer returns as UTC: measured on one
/// stand, the same row came back as true UTC and, half an hour and an application restart later, as the session's
/// local time with a <c>Z</c> (see <c>docs/knowledge/platform/dataservice-returns-datetimes-in-the-session-zone.md</c>).
/// </para>
/// <para>
/// The offset is the session's CURRENT one, so a row written before a daylight-saving change is off by that change.
/// The rows a caller matches against a compile it just started are minutes old. The offset and the rows have to
/// come from the same session, which is why this reads through one <see cref="IApplicationClient"/> rather than
/// the ATF data provider <see cref="ICompilationHistoryPoller"/> uses.
/// </para>
/// </remarks>
public interface ICompilationHistoryReader {

	/// <summary>
	/// Reads the newest <paramref name="count"/> rows, newest first.
	/// </summary>
	/// <param name="count">How many rows to read.</param>
	/// <param name="timeoutMs">The timeout of each of the two requests the read makes.</param>
	/// <returns>The rows, and this host's UTC clock right after they were read.</returns>
	/// <exception cref="InvalidOperationException">
	/// The environment answered without what the read needs - an HTML login page, a failed query, a missing session
	/// offset or a row without a time all count. The message is clio's own and quotes nothing from the server.
	/// </exception>
	/// <remarks>A transport failure (the environment could not be reached) passes through as the client throws it.</remarks>
	CompilationHistoryReading ReadLatest(int count, int timeoutMs);

}

/// <summary>
/// The newest compilation-history rows, and when they were read.
/// </summary>
/// <param name="CheckedUtc">This host's UTC clock right after the rows were read.</param>
/// <param name="Rows">The rows, newest first.</param>
public sealed record CompilationHistoryReading(DateTime CheckedUtc, IReadOnlyList<CompilationHistoryRow> Rows);

/// <summary>
/// One compilation-history row: one project of one build.
/// </summary>
/// <param name="ProjectName">The project the row is for, for example <c>Terrasoft.Configuration.Dev.csproj</c>.</param>
/// <param name="FinishedUtc">When Creatio wrote the row, which is when that project's build ended (UTC).</param>
/// <param name="DurationSeconds">How long that project's build took.</param>
/// <param name="Succeeded">The row's own <c>Result</c>.</param>
/// <param name="Diagnostics">The row's <c>ErrorsWarnings</c>, warnings included; empty when there are none.</param>
public sealed record CompilationHistoryRow(string ProjectName, DateTime FinishedUtc, int DurationSeconds, bool Succeeded,
	IReadOnlyList<PackageBuildDiagnostic> Diagnostics);

/// <inheritdoc cref="ICompilationHistoryReader"/>
public sealed class CompilationHistoryReader : ICompilationHistoryReader {

	#region Constants: Private

	private const string UnreadableAnswer =
		"The environment did not answer with compilation-history rows.";

	private const string UnreadableOffset =
		"The environment did not report the session's time-zone offset.";

	#endregion

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly TimeProvider _timeProvider;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="CompilationHistoryReader"/> class.
	/// </summary>
	/// <param name="applicationClient">Client for the target environment; both requests share its session.</param>
	/// <param name="serviceUrlBuilder">Builds the endpoint URLs for the target environment.</param>
	/// <param name="timeProvider">The clock <see cref="CompilationHistoryReading.CheckedUtc"/> is read from.</param>
	public CompilationHistoryReader(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
		TimeProvider timeProvider) {
		applicationClient.CheckArgumentNull(nameof(applicationClient));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		timeProvider.CheckArgumentNull(nameof(timeProvider));
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_timeProvider = timeProvider;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public CompilationHistoryReading ReadLatest(int count, int timeoutMs) {
		// Both requests go through the same client, so they run in the same session and so in the same time zone.
		TimeSpan sessionOffset = ParseSessionOffset(_applicationClient.ExecutePostRequest(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetApplicationInfo), "{}", timeoutMs));
		IReadOnlyList<CompilationHistoryRow> rows = ParseRows(_applicationClient.ExecutePostRequest(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select), BuildSelectQuery(count), timeoutMs),
			sessionOffset);
		return new CompilationHistoryReading(_timeProvider.GetUtcNow().UtcDateTime, rows);
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Builds the DataService query for the newest <paramref name="count"/> rows.
	/// </summary>
	/// <param name="count">How many rows to read.</param>
	/// <returns>The <c>SelectQuery</c> body.</returns>
	internal static string BuildSelectQuery(int count) =>
		JsonSerializer.Serialize(SelectQueryHelper.BuildSelectQuery("CompilationHistory", [
			// Descending (2) by time puts the newest rows - the ones a caller matches to its compile - first.
			new SelectQueryHelper.SelectQueryColumnDefinition("CreatedOn", "CreatedOn", OrderDirection: 2, OrderPosition: 0),
			new SelectQueryHelper.SelectQueryColumnDefinition("ProjectName", "ProjectName"),
			new SelectQueryHelper.SelectQueryColumnDefinition("Result", "Result"),
			new SelectQueryHelper.SelectQueryColumnDefinition("DurationInSeconds", "DurationInSeconds"),
			new SelectQueryHelper.SelectQueryColumnDefinition("ErrorsWarnings", "ErrorsWarnings")
		], [], count));

	/// <summary>
	/// Reads the session's time-zone offset out of a <c>GetApplicationInfo</c> answer.
	/// </summary>
	/// <param name="body">The response body.</param>
	/// <returns>The offset of the session's zone from UTC.</returns>
	/// <exception cref="InvalidOperationException">The body carries no <c>applicationInfo.sysValues.userTimezoneOffset</c>.</exception>
	internal static TimeSpan ParseSessionOffset(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			throw new InvalidOperationException(UnreadableOffset);
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			if (document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.TryGetProperty("applicationInfo", out JsonElement info)
				&& info.ValueKind == JsonValueKind.Object
				&& info.TryGetProperty("sysValues", out JsonElement sysValues)
				&& sysValues.ValueKind == JsonValueKind.Object
				&& sysValues.TryGetProperty("userTimezoneOffset", out JsonElement offset)
				&& offset.ValueKind == JsonValueKind.Number
				&& offset.TryGetInt32(out int minutes)) {
				return TimeSpan.FromMinutes(minutes);
			}
		} catch (JsonException) {
			// Falls through to the same refusal as a body without the offset.
		}
		throw new InvalidOperationException(UnreadableOffset);
	}

	/// <summary>
	/// Parses a DataService <c>CompilationHistory</c> answer into rows with UTC times.
	/// </summary>
	/// <param name="body">The response body.</param>
	/// <param name="sessionOffset">The offset of the session's zone, which the answer's times are in.</param>
	/// <returns>The rows, in the order the answer listed them.</returns>
	/// <exception cref="InvalidOperationException">The body is not a successful answer of rows that carry a time.</exception>
	internal static IReadOnlyList<CompilationHistoryRow> ParseRows(string body, TimeSpan sessionOffset) {
		if (string.IsNullOrWhiteSpace(body)) {
			throw new InvalidOperationException(UnreadableAnswer);
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| (root.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.False)
				|| !root.TryGetProperty("rows", out JsonElement rows)
				|| rows.ValueKind != JsonValueKind.Array) {
				throw new InvalidOperationException(UnreadableAnswer);
			}
			return rows.EnumerateArray().Select(row => ToRow(row, sessionOffset)).ToList();
		} catch (JsonException) {
			throw new InvalidOperationException(UnreadableAnswer);
		}
	}

	#endregion

	#region Methods: Private

	private static CompilationHistoryRow ToRow(JsonElement row, TimeSpan sessionOffset) {
		// A row without a time is useless to every caller - the time is what this reader exists for - so it makes
		// the whole answer unreadable rather than being skipped, which would hide a newer row's absence. A time that
		// carries its own zone is refused too: DataService has never been seen to send one, and subtracting the
		// session offset from it would silently shift it.
		if (row.ValueKind != JsonValueKind.Object
			|| !row.TryGetProperty("CreatedOn", out JsonElement createdOn)
			|| createdOn.ValueKind != JsonValueKind.String
			|| !DateTime.TryParse(createdOn.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None,
				out DateTime sessionTime)
			|| sessionTime.Kind != DateTimeKind.Unspecified) {
			throw new InvalidOperationException(UnreadableAnswer);
		}
		// A time at the edge of DateTime's range cannot take the offset; it is no compile's time either.
		if (sessionTime < DateTime.MinValue + sessionOffset.Duration()
			|| sessionTime > DateTime.MaxValue - sessionOffset.Duration()) {
			throw new InvalidOperationException(UnreadableAnswer);
		}
		return new CompilationHistoryRow(
			ReadString(row, "ProjectName"),
			DateTime.SpecifyKind(sessionTime - sessionOffset, DateTimeKind.Utc),
			row.TryGetProperty("DurationInSeconds", out JsonElement duration)
				&& duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out int seconds)
				? seconds
				: 0,
			row.TryGetProperty("Result", out JsonElement result) && result.ValueKind == JsonValueKind.True,
			PackageBuildResultParser.ParseHistoryDiagnostics(ReadString(row, "ErrorsWarnings")));
	}

	private static string ReadString(JsonElement row, string name) =>
		row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	#endregion

}
