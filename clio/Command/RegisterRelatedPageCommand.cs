using System;
using Clio.Command.RelatedPages;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Environment-scoped arguments for registering an entity's related page (web or mobile).
/// </summary>
public sealed class RegisterRelatedPageOptions : EnvironmentNameOptions {
	public string PackageName { get; set; } = string.Empty;

	public string EntitySchemaName { get; set; } = string.Empty;

	public string PageSchemaName { get; set; } = string.Empty;

	public RelatedPageSchemaType SchemaType { get; set; } = RelatedPageSchemaType.Mobile;

	public bool IsDefault { get; set; } = true;
}

/// <summary>
/// Registers a page as a related page of an entity (web <c>RelatedPage</c> or <c>MobileRelatedPage</c> add-on),
/// optionally as the default page.
/// </summary>
public sealed class RegisterRelatedPageCommand(
	IRelatedPageService service,
	ILogger logger)
	: Command<RegisterRelatedPageOptions> {

	public override int Execute(RegisterRelatedPageOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		try {
			Validate(options);
			RelatedPageResult result = service.Register(new RelatedPageRegistration(
				options.PackageName,
				options.EntitySchemaName,
				options.PageSchemaName,
				options.SchemaType,
				options.IsDefault));
			string role = result.IsDefault ? "default " : string.Empty;
			logger.WriteInfo(
				$"Registered '{result.PageSchemaName}' as a {role}{result.AddonName} for '{result.EntitySchemaName}' " +
				$"(page UId {result.PageSchemaUId}).");
			logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(RegisterRelatedPageOptions options) {
		if (string.IsNullOrWhiteSpace(options.Environment)) {
			throw new ArgumentException("environment-name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.PageSchemaName)) {
			throw new ArgumentException("page-schema-name is required.");
		}
	}
}
