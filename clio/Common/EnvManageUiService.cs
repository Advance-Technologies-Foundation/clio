using System;
using System.Text.RegularExpressions;
using Clio.UserEnvironment;
using Spectre.Console;

namespace Clio.Common;

#region Interface: IEnvManageUiService

/// <summary>
/// Service layer for environment management UI business logic
/// </summary>
public interface IEnvManageUiService
{
	/// <summary>
	/// Validates environment name format and uniqueness
	/// </summary>
	ValidationResult ValidateEnvironmentName(string name, ISettingsRepository repository);
	
	/// <summary>
	/// Validates URL format
	/// </summary>
	ValidationResult ValidateUrl(string url);
	
	/// <summary>
	/// Masks sensitive data for display
	/// </summary>
	string MaskSensitiveData(string fieldName, string value);
	
	/// <summary>
	/// Creates a formatted details table
	/// </summary>
	Table CreateDetailsTable(string title);
}

#endregion

#region Class: EnvManageUiService

/// <summary>
/// Implementation of environment management UI service
/// </summary>
public class EnvManageUiService : IEnvManageUiService
{
	#region Fields: Private

	private static readonly string[] SensitiveFields = { "Password", "ClientSecret", "DBPassword", "Secret" };
	private static readonly Regex NamePattern = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

	#endregion

	#region Methods: Public

	/// <summary>
	/// Validates environment name format and uniqueness
	/// </summary>
	public ValidationResult ValidateEnvironmentName(string name, ISettingsRepository repository)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return ValidationResult.Error("[red]Name cannot be empty[/]");
		}
		
		if (name.Length > 50)
		{
			return ValidationResult.Error("[red]Name cannot exceed 50 characters[/]");
		}
		
		if (!NamePattern.IsMatch(name))
		{
			return ValidationResult.Error("[red]Name can only contain letters, numbers, underscores, and hyphens[/]");
		}
		
		if (repository.IsEnvironmentExists(name))
		{
			return ValidationResult.Error($"[red]Environment '{name}' already exists[/]");
		}
		
		return ValidationResult.Success();
	}
	
	/// <summary>
	/// Validates URL format
	/// </summary>
	public ValidationResult ValidateUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return ValidationResult.Error("[red]URL cannot be empty[/]");
		}
		
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
		{
			return ValidationResult.Error("[red]Invalid URL format. Example: https://myapp.creatio.com[/]");
		}
		
		if (uri.Scheme != "http" && uri.Scheme != "https")
		{
			return ValidationResult.Error("[red]URL must use http:// or https:// protocol[/]");
		}
		
		return ValidationResult.Success();
	}
	
	/// <summary>
	/// Masks sensitive data for display
	/// </summary>
	public string MaskSensitiveData(string fieldName, string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "[dim]not set[/]";
		}
		
		foreach (var sensitiveField in SensitiveFields)
		{
			if (fieldName.Contains(sensitiveField, StringComparison.OrdinalIgnoreCase))
			{
				return "[red]****[/]";
			}
		}
		
		return value;
	}
	
	/// <summary>
	/// Creates a formatted details table
	/// </summary>
	public Table CreateDetailsTable(string title)
	{
		return new Table()
			.Border(TableBorder.Rounded)
			.Title($"[yellow]{title}[/]")
			.AddColumn(new TableColumn("[bold]Property[/]").Width(25))
			.AddColumn(new TableColumn("[bold]Value[/]"));
	}

	#endregion
}

#endregion
