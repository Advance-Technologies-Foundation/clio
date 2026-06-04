using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Common;

namespace Clio.Package;

/// <inheritdoc cref="IThemeArtifactBuilder"/>
public class ThemeArtifactBuilder : IThemeArtifactBuilder
{

	#region Constants: Private

	private const string CssClassPlaceholder = "<%themeCssClass%>";
	private const string ThemeSuffix = "-theme";
	private const int MaxIdLength = 100;
	private const int MaxCaptionLength = 250;
	private const int MaxCssClassNameLength = 100;

	#endregion

	#region Fields: Private

	// Resolves to tpl/themes/theme.css.tpl via ITemplateProvider.GetTemplate.
	private static readonly string ThemeCssTemplateName = Path.Combine("themes", "theme.css");
	private static readonly Regex CssClassNamePattern = new("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);
	private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

	private readonly ITemplateProvider _templateProvider;

	#endregion

	#region Constructors: Public

	public ThemeArtifactBuilder(ITemplateProvider templateProvider) {
		templateProvider.CheckArgumentNull(nameof(templateProvider));
		_templateProvider = templateProvider;
	}

	#endregion

	#region Methods: Private

	private static string DeriveCaption(string cssClassName) {
		if (string.IsNullOrWhiteSpace(cssClassName)) {
			return string.Empty;
		}
		string baseName = cssClassName.Trim();
		if (baseName.EndsWith(ThemeSuffix, StringComparison.OrdinalIgnoreCase)) {
			baseName = baseName.Substring(0, baseName.Length - ThemeSuffix.Length);
		}
		string spaced = string.Join(' ',
			baseName.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
		return spaced.Length == 0
			? string.Empty
			: CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
	}

	private static void ValidateValue(string value, string field, Regex pattern, int maxLength) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw new ArgumentException($"Theme {field} is required.");
		}
		if (value.Length > maxLength) {
			throw new ArgumentException($"Theme {field} must be at most {maxLength} characters. Received: '{value}'.");
		}
		if (!pattern.IsMatch(value)) {
			throw new ArgumentException($"Theme {field} '{value}' does not match the required pattern '{pattern}'.");
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public ThemeIdentifiers DeriveIdentifiers(string cssClassName, string caption = null, string id = null) {
		string normalizedCssClassName = cssClassName?.Trim();
		string resolvedId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id.Trim();
		string resolvedCaption = string.IsNullOrWhiteSpace(caption)
			? DeriveCaption(normalizedCssClassName)
			: caption.Trim();
		return new ThemeIdentifiers(resolvedId, resolvedCaption, normalizedCssClassName);
	}

	/// <inheritdoc/>
	public void Validate(ThemeIdentifiers identifiers) {
		identifiers.CheckArgumentNull(nameof(identifiers));
		ValidateValue(identifiers.CssClassName, "cssClassName", CssClassNamePattern, MaxCssClassNameLength);
		ValidateValue(identifiers.Id, "id", IdPattern, MaxIdLength);
		if (string.IsNullOrWhiteSpace(identifiers.Caption)) {
			throw new ArgumentException("Theme caption is required.");
		}
		if (identifiers.Caption.Length > MaxCaptionLength) {
			throw new ArgumentException($"Theme caption must be at most {MaxCaptionLength} characters.");
		}
	}

	/// <inheritdoc/>
	public string BuildThemeJson(ThemeIdentifiers identifiers) {
		identifiers.CheckArgumentNull(nameof(identifiers));
		var payload = new {
			id = identifiers.Id,
			caption = identifiers.Caption,
			cssClassName = identifiers.CssClassName
		};
		return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
	}

	/// <inheritdoc/>
	public string BuildThemeCss(ThemeIdentifiers identifiers) {
		identifiers.CheckArgumentNull(nameof(identifiers));
		string template = _templateProvider.GetTemplate(ThemeCssTemplateName);
		return template.Replace(CssClassPlaceholder, identifiers.CssClassName);
	}

	#endregion

}
