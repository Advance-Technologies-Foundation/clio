namespace Clio.Command.Localization;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

/// <summary>
/// A <c>SysCulture</c> row: the culture name in its canonical <c>ll-CC</c> spelling and whether users can
/// select it.
/// </summary>
/// <param name="Name">Culture name as stored in <c>SysCulture.Name</c>, for example <c>es-ES</c>.</param>
/// <param name="Active">Whether the culture is active in the Languages section.</param>
public sealed record CreatioCulture(string Name, bool Active);

/// <summary>
/// Result of looking a culture name up in the environment's <c>SysCulture</c> table.
/// </summary>
/// <param name="Culture">The matching row with its canonical name, or <see langword="null"/> when the environment has no such culture.</param>
/// <param name="Available">Every culture of the environment, in the order the server returned them.</param>
public sealed record CultureLookupResult(CreatioCulture Culture, IReadOnlyList<CreatioCulture> Available) {

	/// <summary>Gets a value indicating whether the environment has the requested culture.</summary>
	public bool Found => Culture is not null;
}

/// <summary>
/// Reads the cultures an environment knows (<c>SysCulture</c>). A designer save silently drops a
/// localizable value whose culture is not a <c>SysCulture</c> row and still answers <c>success:true</c>,
/// so a command that writes a value in a given culture checks it here first.
/// </summary>
public interface ICreatioCultureCatalog {

	/// <summary>
	/// Reads every <c>SysCulture</c> row (<c>Name</c>, <c>Active</c>) through DataService <c>SelectQuery</c>.
	/// </summary>
	/// <returns>The environment's cultures.</returns>
	/// <exception cref="InvalidOperationException">The query failed; the message carries the server's reason.
	/// A failed read is never reported as an empty list.</exception>
	IReadOnlyList<CreatioCulture> GetCultures();

	/// <summary>
	/// Finds a culture by name, case-insensitively, and returns it with its canonical <c>SysCulture.Name</c>
	/// spelling (<c>es-es</c> → <c>es-ES</c>).
	/// </summary>
	/// <param name="name">Culture name supplied by the caller.</param>
	/// <returns>The lookup result, carrying the available cultures for an error message.</returns>
	/// <exception cref="InvalidOperationException">The <c>SysCulture</c> read failed.</exception>
	CultureLookupResult Find(string name);
}

/// <inheritdoc />
public sealed class CreatioCultureCatalog : ICreatioCultureCatalog {

	private const string SysCultureSchemaName = "SysCulture";
	private const string NameColumn = "Name";
	private const string ActiveColumn = "Active";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioCultureCatalog"/> class.
	/// </summary>
	/// <param name="applicationClient">Authenticated client of the target environment.</param>
	/// <param name="serviceUrlBuilder">URL builder of the target environment.</param>
	public CreatioCultureCatalog(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public IReadOnlyList<CreatioCulture> GetCultures() {
		SysCultureResponse response = SelectQueryHelper.ExecuteSelectQuery<SysCultureResponse>(
			_applicationClient,
			_serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				SysCultureSchemaName,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition(NameColumn, NameColumn),
					new SelectQueryHelper.SelectQueryColumnDefinition(ActiveColumn, ActiveColumn)
				],
				[]));
		return response.Rows
			.Where(row => !string.IsNullOrWhiteSpace(row.Name))
			.Select(row => new CreatioCulture(row.Name, row.Active))
			.ToList();
	}

	/// <inheritdoc />
	public CultureLookupResult Find(string name) {
		IReadOnlyList<CreatioCulture> cultures = GetCultures();
		string trimmed = name?.Trim();
		CreatioCulture match = cultures.FirstOrDefault(culture =>
			string.Equals(culture.Name, trimmed, StringComparison.OrdinalIgnoreCase));
		return new CultureLookupResult(match, cultures);
	}

	private sealed class SysCultureResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<SysCultureRow> Rows { get; init; } = [];
	}

	private sealed class SysCultureRow {
		[JsonPropertyName(NameColumn)]
		public string Name { get; init; }

		[JsonPropertyName(ActiveColumn)]
		public bool Active { get; init; }
	}
}
