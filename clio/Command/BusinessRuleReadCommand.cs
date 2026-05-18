using System;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Environment-scoped options for MCP business-rule read operations.
/// </summary>
public sealed class BusinessRuleReadOptions : EnvironmentNameOptions {
	/// <summary>
	/// Gets or sets target package name.
	/// </summary>
	public string PackageName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets target scope type: entity or page.
	/// </summary>
	public string ScopeType { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets target entity or page schema name.
	/// </summary>
	public string SchemaName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets selected business-rule platform name.
	/// </summary>
	public string RuleName { get; set; } = string.Empty;
}

/// <summary>
/// Reads existing business rules from a Creatio environment.
/// </summary>
public sealed class BusinessRuleReadCommand(
	IBusinessRuleReadService businessRuleReadService,
	ILogger logger)
	: Command<BusinessRuleReadOptions> {

	/// <summary>
	/// Lists normalized business rules for the requested scope.
	/// </summary>
	/// <param name="options">Read options.</param>
	/// <returns>Business-rule list response.</returns>
	public BusinessRuleListResponse List(BusinessRuleReadOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		return businessRuleReadService.List(new BusinessRuleReadRequest(
			options.PackageName,
			options.ScopeType,
			options.SchemaName));
	}

	/// <summary>
	/// Gets one normalized business rule for the requested scope.
	/// </summary>
	/// <param name="options">Read options with the selected business-rule platform name.</param>
	/// <returns>Business-rule get response.</returns>
	public BusinessRuleGetResponse Get(BusinessRuleReadOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		return businessRuleReadService.Get(new BusinessRuleGetRequest(
			options.PackageName,
			options.ScopeType,
			options.SchemaName,
			options.RuleName));
	}

	public override int Execute(BusinessRuleReadOptions options) {
		BusinessRuleListResponse response = List(options);
		logger.WriteInfo(JsonSerializer.Serialize(response, BusinessRuleConstants.JsonOptions));
		return response.Success ? 0 : 1;
	}
}
