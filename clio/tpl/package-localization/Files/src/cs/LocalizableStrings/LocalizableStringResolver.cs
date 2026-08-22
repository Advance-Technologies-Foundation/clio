using System;
using System.Globalization;
using Terrasoft.Common;
using Terrasoft.Core;

namespace #RootNameSpace#.LocalizableStrings {

	/// <summary>
	/// Resolves schema-owned Creatio localizable strings without exposing the concrete
	/// <see cref="LocalizableString"/> dependency to consumers.
	/// </summary>
	public interface ILocalizableStringResolver {

		/// <summary>Resolves a schema resource using the current culture and Creatio fallback behavior.</summary>
		/// <param name="resourceSchemaName">Name of the schema that owns the resource.</param>
		/// <param name="resourceItemName">Exact persisted resource item name.</param>
		/// <returns>The value for the current culture, including configured fallback, or <c>null</c>
		/// when the resource manager is unavailable.</returns>
		string GetValue(string resourceSchemaName, string resourceItemName);

		/// <summary>Resolves a schema resource strictly for one culture.</summary>
		/// <param name="resourceSchemaName">Name of the schema that owns the resource.</param>
		/// <param name="resourceItemName">Exact persisted resource item name.</param>
		/// <param name="culture">Culture to resolve.</param>
		/// <returns>The culture-specific value, or <c>null</c> when the culture has no resource.</returns>
		string GetCultureValue(string resourceSchemaName, string resourceItemName, CultureInfo culture);

		/// <summary>Resolves a schema resource for one culture using Creatio fallback behavior.</summary>
		/// <param name="resourceSchemaName">Name of the schema that owns the resource.</param>
		/// <param name="resourceItemName">Exact persisted resource item name.</param>
		/// <param name="culture">Culture to resolve.</param>
		/// <returns>The culture-specific or fallback value, or <c>null</c> when the resource is unavailable.</returns>
		string GetCultureValueWithFallback(string resourceSchemaName, string resourceItemName,
			CultureInfo culture);
	}

	/// <summary>
	/// Adapts Creatio Core's concrete <see cref="LocalizableString"/> to the injectable
	/// <see cref="ILocalizableStringResolver"/> contract.
	/// </summary>
	public sealed class LocalizableStringResolver : ILocalizableStringResolver {

		private readonly Func<UserConnection> _getUserConnection;

		/// <summary>Initializes a resolver using the platform-owned current user connection.</summary>
		/// <param name="getUserConnection">Accessor for the current platform-owned connection.</param>
		public LocalizableStringResolver(Func<UserConnection> getUserConnection) {
			_getUserConnection = getUserConnection ?? throw new ArgumentNullException(nameof(getUserConnection));
		}

		/// <inheritdoc />
		public string GetValue(string resourceSchemaName, string resourceItemName) {
			LocalizableString localizableString = Create(resourceSchemaName, resourceItemName);
			string value = localizableString.Value;
			return value;
		}

		/// <inheritdoc />
		public string GetCultureValue(string resourceSchemaName, string resourceItemName,
			CultureInfo culture) {
			LocalizableString localizableString = Create(resourceSchemaName, resourceItemName);
			string value = localizableString.GetCultureValue(culture, throwIfNoManager: false);
			return value;
		}

		/// <inheritdoc />
		public string GetCultureValueWithFallback(string resourceSchemaName, string resourceItemName,
			CultureInfo culture) {
			LocalizableString localizableString = Create(resourceSchemaName, resourceItemName);
			string value = localizableString.GetCultureValueWithFallback(culture,
				throwIfNoManager: false);
			return value;
		}

		private LocalizableString Create(string resourceSchemaName, string resourceItemName) {
			UserConnection userConnection = _getUserConnection();
			LocalizableString localizableString = new LocalizableString(userConnection.Workspace.ResourceStorage,
				resourceSchemaName, resourceItemName);
			return localizableString;
		}
	}
}
