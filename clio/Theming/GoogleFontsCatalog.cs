using System;
using System.Net;
using System.Net.Http;

namespace Clio.Theming;

/// <summary>Whether a font family is published on Google Fonts, or could not be determined.</summary>
public enum GoogleFontAvailability {
	Unverified,
	InCatalog,
	NotInCatalog
}

/// <summary>Looks up whether a font family is published on Google Fonts.</summary>
public interface IGoogleFontsCatalog {
	/// <summary>Reports whether <paramref name="family"/> is published on Google Fonts.</summary>
	GoogleFontAvailability Lookup(string family);
}

/// <summary>Queries the Google Fonts family metadata endpoint, treating any inconclusive answer as unverified.</summary>
public sealed class GoogleFontsCatalog(HttpClient httpClient) : IGoogleFontsCatalog {

	private const string FamilyMetadataUrl = "https://fonts.google.com/metadata/fonts/";

	private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

	/// <inheritdoc />
	public GoogleFontAvailability Lookup(string family) {
		if (string.IsNullOrWhiteSpace(family)) {
			return GoogleFontAvailability.Unverified;
		}
		try {
			using HttpResponseMessage response = _httpClient
				.GetAsync(FamilyMetadataUrl + Uri.EscapeDataString(family.Trim()))
				.GetAwaiter()
				.GetResult();
			if (response.StatusCode == HttpStatusCode.NotFound) {
				return GoogleFontAvailability.NotInCatalog;
			}
			return response.IsSuccessStatusCode
				? GoogleFontAvailability.InCatalog
				: GoogleFontAvailability.Unverified;
		} catch (Exception) {
			return GoogleFontAvailability.Unverified;
		}
	}
}
