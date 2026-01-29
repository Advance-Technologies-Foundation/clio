using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using Spectre.Console;

namespace Clio.Command;

#region Class: EnvManageUiOptions

[Verb("env-ui", Aliases = ["gui", "far"], HelpText = "Interactive console UI for environment management")]
public class EnvManageUiOptions
{
	// Fully interactive - no command line options needed
}

#endregion

#region Interface: IEnvManageUiCommand

/// <summary>
/// Interface for interactive environment management command
/// </summary>
public interface IEnvManageUiCommand
{
	/// <summary>
	/// Execute the interactive UI command
	/// </summary>
	int Execute(EnvManageUiOptions options);
}

#endregion

#region Class: EnvManageUiCommand

/// <summary>
/// Interactive console UI for managing Creatio environments
/// </summary>
public class EnvManageUiCommand : Command<EnvManageUiOptions>, IEnvManageUiCommand
{
	#region Fields: Private

	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;
	private readonly IEnvManageUiService _service;

	#endregion

	#region Constructors: Public

	public EnvManageUiCommand(ISettingsRepository settingsRepository, ILogger logger, IEnvManageUiService service)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_service = service ?? throw new ArgumentNullException(nameof(service));
	}

	#endregion

	#region Methods: Public

	public override int Execute(EnvManageUiOptions options)
	{
		try
		{
			while (true)
			{
				Console.Clear();
				ShowHeader();
				ListEnvironments();
				
				var choice = ShowMainMenu();
				
				try
				{
					var result = choice switch
					{
						MenuChoice.ViewDetails => ViewEnvironmentDetails(),
						MenuChoice.Create => CreateEnvironment(),
						MenuChoice.Edit => EditEnvironment(),
						MenuChoice.Delete => DeleteEnvironment(),
						MenuChoice.SetActive => SetActiveEnvironment(),
						MenuChoice.Refresh => 0, // Just refresh the list
						MenuChoice.Exit => -1, // Exit signal
						_ => 1
					};
					
					if (result == -1)
						return 0; // Exit successfully
						
					if (result != 0 && choice != MenuChoice.Refresh)
					{
						AnsiConsole.WriteLine();
						AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
						Console.ReadKey(true);
					}
				}
				catch (KeyNotFoundException ex)
				{
					_logger.WriteError($"Environment not found: {ex.Message}");
					AnsiConsole.WriteLine();
					AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
					Console.ReadKey(true);
				}
				catch (Exception ex)
				{
					_logger.WriteError($"Error: {ex.Message}");
					AnsiConsole.WriteLine();
					AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
					Console.ReadKey(true);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Unexpected error: {ex.Message}");
			return 1;
		}
	}

	#endregion

	#region Methods: Private

	private void ShowHeader()
	{
		var panel = new Panel(
			Align.Center(
				new Markup($"[bold cyan]Clio Environment Manager[/]\n[dim]{_settingsRepository.AppSettingsFilePath}[/]"),
				VerticalAlignment.Middle
			)
		)
		{
			Border = BoxBorder.Double,
			Padding = new Padding(2, 0, 2, 0)
		};
		
		AnsiConsole.Write(panel);
		AnsiConsole.WriteLine();
	}

	private int ListEnvironments()
	{
		var environments = _settingsRepository.GetAllEnvironments();
		var activeEnv = _settingsRepository.GetDefaultEnvironmentName();
		
		if (!environments.Any())
		{
			AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
			return 0;
		}
		
		var table = new Table()
			.Border(TableBorder.Rounded)
			.AddColumn(new TableColumn("[bold]#[/]").Centered())
			.AddColumn(new TableColumn("[bold]Name[/]"))
			.AddColumn(new TableColumn("[bold]URL[/]"))
			.AddColumn(new TableColumn("[bold]Login[/]"))
			.AddColumn(new TableColumn("[bold]IsNetCore[/]").Centered());
		
		int index = 1;
		foreach (var env in environments)
		{
			var isActive = env.Key == activeEnv;
			var marker = isActive ? "*" : " ";
			var nameDisplay = isActive ? $"[green bold]{env.Key}[/]" : env.Key;
			
			table.AddRow(
				$"{index}{marker}",
				nameDisplay,
				env.Value.Uri ?? "[dim]not set[/]",
				env.Value.Login ?? "[dim]not set[/]",
				env.Value.IsNetCore ? "[green]✓[/]" : "[red]✗[/]"
			);
			
			index++;
		}
		
		AnsiConsole.Write(table);
		AnsiConsole.MarkupLine("\n[dim]* - Active Environment[/]");
		AnsiConsole.WriteLine();
		
		return 0;
	}

	private MenuChoice ShowMainMenu()
	{
		return AnsiConsole.Prompt(
			new SelectionPrompt<MenuChoice>()
				.Title("[green]What would you like to do?[/]")
				.AddChoices(Enum.GetValues<MenuChoice>())
				.UseConverter(choice => choice switch
				{
					MenuChoice.ViewDetails => "View Environment Details",
					MenuChoice.Create => "Create New Environment",
					MenuChoice.Edit => "Edit Environment",
					MenuChoice.Delete => "Delete Environment",
					MenuChoice.SetActive => "Set Active Environment",
					MenuChoice.Refresh => "Refresh List",
					MenuChoice.Exit => "Exit",
					_ => choice.ToString()
				})
		);
	}

	private int ViewEnvironmentDetails()
	{
		Console.Clear();
		
		var environments = _settingsRepository.GetAllEnvironments();
		
		if (!environments.Any())
		{
			AnsiConsole.Write(new Panel("[bold]View Environment Details[/]")
			{
				Border = BoxBorder.Double
			});
			AnsiConsole.WriteLine();
			AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
			return 0;
		}
		
		AnsiConsole.Write(new Panel("[bold]View Environment Details[/]")
		{
			Border = BoxBorder.Double
		});
		AnsiConsole.WriteLine();
		
		var envName = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("[green]Select environment to view:[/]")
				.AddChoices(environments.Keys)
		);
		
		var env = environments[envName];
		Console.Clear();
		
		AnsiConsole.Write(new Panel($"[bold]Environment Details: {envName}[/]")
		{
			Border = BoxBorder.Double
		});
		
		AnsiConsole.WriteLine();
		
		// Basic Configuration
		var basicTable = _service.CreateDetailsTable("Basic Configuration");
		basicTable.AddRow("Name", envName);
		basicTable.AddRow("URL", env.Uri ?? "[dim]not set[/]");
		basicTable.AddRow("Login", env.Login ?? "[dim]not set[/]");
		basicTable.AddRow("Password", _service.MaskSensitiveData("Password", env.Password));
		basicTable.AddRow("Maintainer", env.Maintainer ?? "[dim]not set[/]");
		basicTable.AddRow("IsNetCore", env.IsNetCore ? "[green]Yes[/]" : "[red]No[/]");
		AnsiConsole.Write(basicTable);
		AnsiConsole.WriteLine();
		
		// Authentication
		if (!string.IsNullOrEmpty(env.ClientId))
		{
			var authTable = _service.CreateDetailsTable("Authentication");
			authTable.AddRow("ClientId", env.ClientId);
			authTable.AddRow("ClientSecret", _service.MaskSensitiveData("ClientSecret", env.ClientSecret));
			authTable.AddRow("AuthAppUri", env.AuthAppUri ?? "[dim]not set[/]");
			authTable.AddRow("SimpleLoginUri", env.SimpleloginUri ?? "[dim]not set[/]");
			AnsiConsole.Write(authTable);
			AnsiConsole.WriteLine();
		}
		
		// Advanced Settings
		var advancedTable = _service.CreateDetailsTable("Advanced Settings");
		advancedTable.AddRow("Safe Mode", (env.Safe ?? false) ? "[green]Yes[/]" : "[red]No[/]");
		advancedTable.AddRow("Developer Mode", (env.DeveloperModeEnabled ?? false) ? "[green]Yes[/]" : "[red]No[/]");
		advancedTable.AddRow("Is Dev Mode", env.IsDevMode ? "[green]Yes[/]" : "[red]No[/]");
		advancedTable.AddRow("Workspace Paths", env.WorkspacePathes ?? "[dim]not set[/]");
		advancedTable.AddRow("Environment Path", env.EnvironmentPath ?? "[dim]not set[/]");
		advancedTable.AddRow("DB Server Key", env.DbServerKey ?? "[dim]not set[/]");
		AnsiConsole.Write(advancedTable);
		AnsiConsole.WriteLine();
		
		// Database Configuration
		if (env.DbServer != null)
		{
			var dbTable = _service.CreateDetailsTable("Database Configuration");
			dbTable.AddRow("DB Name", env.DbName ?? "[dim]not set[/]");
			dbTable.AddRow("DB Server", env.DbServer.Uri?.ToString() ?? "[dim]not set[/]");
			dbTable.AddRow("DB Login", env.DbServer.Login ?? "[dim]not set[/]");
			dbTable.AddRow("DB Password", _service.MaskSensitiveData("Password", env.DbServer.Password));
			dbTable.AddRow("Working Folder", env.DbServer.WorkingFolder ?? "[dim]not set[/]");
			AnsiConsole.Write(dbTable);
			AnsiConsole.WriteLine();
		}
		
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
		Console.ReadKey(true);
		
		return 0;
	}

	private int CreateEnvironment()
	{
		Console.Clear();
		AnsiConsole.Write(new Panel("[bold]Create New Environment[/]")
		{
			Border = BoxBorder.Double
		});
		AnsiConsole.WriteLine();
		
		try
		{
			// Create new environment with default values
			var name = "";
			var env = new EnvironmentSettings
			{
				Uri = "",
				Login = "",
				Password = "",
				IsNetCore = true,
				Safe = false,
				DeveloperModeEnabled = false
			};
			
			bool keepEditing = true;
			string lastSelectedField = ""; // Track last selected field for cursor positioning
			
			while (keepEditing)
			{
				Console.Clear();
				AnsiConsole.Write(new Panel("[bold]Create New Environment[/]")
				{
					Border = BoxBorder.Double
				});
				AnsiConsole.WriteLine();
				
				// Show current values
				var currentValuesTable = new Table()
					.Border(TableBorder.Rounded)
					.AddColumn(new TableColumn("[bold]Field[/]"))
					.AddColumn(new TableColumn("[bold]Current Value[/]"));
				
				currentValuesTable.AddRow("0. Name", string.IsNullOrEmpty(name) ? "[dim]not set[/]" : name);
				currentValuesTable.AddRow("1. URL", string.IsNullOrEmpty(env.Uri) ? "[dim]not set[/]" : env.Uri);
				currentValuesTable.AddRow("2. Login", string.IsNullOrEmpty(env.Login) ? "[dim]not set[/]" : env.Login);
				currentValuesTable.AddRow("3. Password", _service.MaskSensitiveData("Password", env.Password));
				currentValuesTable.AddRow("4. IsNetCore", env.IsNetCore ? "[green]Yes[/]" : "[red]No[/]");
				currentValuesTable.AddRow("5. Maintainer", string.IsNullOrEmpty(env.Maintainer) ? "[dim]not set[/]" : env.Maintainer);
				currentValuesTable.AddRow("6. Client ID", string.IsNullOrEmpty(env.ClientId) ? "[dim]not set[/]" : env.ClientId);
				currentValuesTable.AddRow("7. Client Secret", _service.MaskSensitiveData("ClientSecret", env.ClientSecret));
				currentValuesTable.AddRow("8. Safe Mode", (env.Safe ?? false) ? "[green]Yes[/]" : "[red]No[/]");
				currentValuesTable.AddRow("9. Developer Mode", (env.DeveloperModeEnabled ?? false) ? "[green]Yes[/]" : "[red]No[/]");
				currentValuesTable.AddRow("10. Workspace Paths", string.IsNullOrEmpty(env.WorkspacePathes) ? "[dim]not set[/]" : env.WorkspacePathes);
				
				AnsiConsole.Write(currentValuesTable);
				AnsiConsole.WriteLine();
				
				// Build field choices and reorder to position cursor on last selected field
				var fieldChoices = new List<string>
				{
					"0. Name",
					"1. URL",
					"2. Login",
					"3. Password",
					"4. IsNetCore",
					"5. Maintainer",
					"6. Client ID",
					"7. Client Secret",
					"8. Safe Mode",
					"9. Developer Mode",
					"10. Workspace Paths"
				};
				
				// Move last selected field to top for better UX (cursor will start there)
				if (!string.IsNullOrEmpty(lastSelectedField) && fieldChoices.Contains(lastSelectedField))
				{
					fieldChoices.Remove(lastSelectedField);
					fieldChoices.Insert(0, lastSelectedField);
				}
				
				var allChoices = new List<string>();
				allChoices.AddRange(fieldChoices);
				allChoices.Add("───────────────────");
				allChoices.Add("💾 Save & Create");
				allChoices.Add("❌ Cancel");
				
				var choice = AnsiConsole.Prompt(
					new SelectionPrompt<string>()
						.Title("[green]Select field to edit or action:[/]")
						.PageSize(15)
						.AddChoices(allChoices)
				);
				
				// Remember last selected field (but not separators or actions)
				if (choice.Contains(".") && !choice.Contains("💾") && !choice.Contains("❌") && !choice.StartsWith("───"))
				{
					lastSelectedField = choice;
				}
				
				if (choice == "💾 Save & Create")
				{
					// Validate required fields
					var nameValidation = _service.ValidateEnvironmentName(name, _settingsRepository);
					if (!nameValidation.Successful)
					{
						AnsiConsole.MarkupLine($"[red]✗ Name validation failed: {nameValidation.Message}[/]");
						AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
						Console.ReadKey(true);
						continue;
					}
					
					var urlValidation = _service.ValidateUrl(env.Uri);
					if (!urlValidation.Successful)
					{
						AnsiConsole.MarkupLine($"[red]✗ URL validation failed: {urlValidation.Message}[/]");
						AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
						Console.ReadKey(true);
						continue;
					}
					
					if (string.IsNullOrWhiteSpace(env.Login))
					{
						AnsiConsole.MarkupLine("[red]✗ Login cannot be empty[/]");
						AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
						Console.ReadKey(true);
						continue;
					}
					
					// Save
					_settingsRepository.ConfigureEnvironment(name, env);
					AnsiConsole.MarkupLine($"\n[green]✓[/] Environment '[cyan]{name}[/]' created successfully!");
					
					// Set as active?
					if (AnsiConsole.Confirm($"Set '[cyan]{name}[/]' as active environment?", defaultValue: false))
					{
						_settingsRepository.SetActiveEnvironment(name);
						AnsiConsole.MarkupLine($"[green]✓[/] '[cyan]{name}[/]' is now the active environment");
					}
					
					keepEditing = false;
				}
				else if (choice == "❌ Cancel")
				{
					AnsiConsole.MarkupLine("[yellow]Environment creation cancelled[/]");
					keepEditing = false;
				}
				else if (choice == "0. Name")
				{
					name = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Environment name:[/]")
							.DefaultValue(name ?? "")
							.AllowEmpty()
					);
				}
				else if (choice == "1. URL")
				{
					env.Uri = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]URL:[/]")
							.DefaultValue(env.Uri ?? "")
							.AllowEmpty()
					).TrimEnd('/');
				}
				else if (choice == "2. Login")
				{
					env.Login = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Login:[/]")
							.DefaultValue(env.Login ?? "")
							.AllowEmpty()
					);
				}
				else if (choice == "3. Password")
				{
					env.Password = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Password:[/]")
							.Secret()
							.AllowEmpty()
					);
				}
				else if (choice == "4. IsNetCore")
				{
					env.IsNetCore = AnsiConsole.Confirm("[green]Is this a .NET Core environment?[/]", defaultValue: env.IsNetCore);
				}
				else if (choice == "5. Maintainer")
				{
					env.Maintainer = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Maintainer:[/]")
							.DefaultValue(env.Maintainer ?? "")
							.AllowEmpty()
					);
				}
				else if (choice == "6. Client ID")
				{
					env.ClientId = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Client ID:[/]")
							.DefaultValue(env.ClientId ?? "")
							.AllowEmpty()
					);
				}
				else if (choice == "7. Client Secret")
				{
					env.ClientSecret = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Client Secret:[/]")
							.Secret()
							.AllowEmpty()
					);
				}
				else if (choice == "8. Safe Mode")
				{
					env.Safe = AnsiConsole.Confirm("[green]Enable Safe Mode?[/]", defaultValue: env.Safe ?? false);
				}
				else if (choice == "9. Developer Mode")
				{
					env.DeveloperModeEnabled = AnsiConsole.Confirm("[green]Enable Developer Mode?[/]", defaultValue: env.DeveloperModeEnabled ?? false);
				}
				else if (choice == "10. Workspace Paths")
				{
					env.WorkspacePathes = AnsiConsole.Prompt(
						new TextPrompt<string>("[green]Workspace Paths:[/]")
							.DefaultValue(env.WorkspacePathes ?? "")
							.AllowEmpty()
					);
				}
			} // end while (keepEditing)
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to create environment: {ex.Message}");
			return 1;
		}
		
		return 0;
	}

	private void ConfigureAdvancedSettings(EnvironmentSettings env)
	{
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine("[yellow]Advanced Settings:[/]");
		AnsiConsole.WriteLine();
		
		env.Maintainer = AnsiConsole.Ask<string>("[green]Maintainer (optional):[/]", string.Empty);
		
		env.ClientId = AnsiConsole.Ask<string>("[green]Client ID (optional):[/]", string.Empty);
		
		if (!string.IsNullOrEmpty(env.ClientId))
		{
			env.ClientSecret = AnsiConsole.Prompt(
				new TextPrompt<string>("[green]Client Secret:[/]")
					.Secret()
					.AllowEmpty()
			);
			
			env.AuthAppUri = AnsiConsole.Ask<string>("[green]Auth App URI (optional):[/]", string.Empty);
		}
		
		env.Safe = AnsiConsole.Confirm("[green]Enable Safe Mode?[/]", defaultValue: false);
		env.DeveloperModeEnabled = AnsiConsole.Confirm("[green]Enable Developer Mode?[/]", defaultValue: false);
		
		env.WorkspacePathes = AnsiConsole.Ask<string>("[green]Workspace Paths (optional):[/]", string.Empty);
	}

	private int EditEnvironment()
	{
		var environments = _settingsRepository.GetAllEnvironments();
		
		if (!environments.Any())
		{
			AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
			return 0;
		}
		
		var envName = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("[green]Select environment to edit:[/]")
				.AddChoices(environments.Keys)
		);
		
		var currentEnv = environments[envName];
		
		Console.Clear();
		AnsiConsole.Write(new Panel($"[bold]Edit Environment: {envName}[/]")
		{
			Border = BoxBorder.Double
		});
		AnsiConsole.WriteLine();
		
		try
		{
			// Create a copy for editing
			var editedName = envName;
			var editedEnv = new EnvironmentSettings
			{
				Uri = currentEnv.Uri,
				Login = currentEnv.Login,
				Password = currentEnv.Password,
				IsNetCore = currentEnv.IsNetCore,
				Maintainer = currentEnv.Maintainer,
				ClientId = currentEnv.ClientId,
				ClientSecret = currentEnv.ClientSecret,
				Safe = currentEnv.Safe,
				DeveloperModeEnabled = currentEnv.DeveloperModeEnabled,
				WorkspacePathes = currentEnv.WorkspacePathes,
				AuthAppUri = currentEnv.AuthAppUri
			};
			
			bool keepEditing = true;
			string lastSelectedField = ""; // Track last selected field for cursor positioning
			while (keepEditing)
			{
				Console.Clear();
				AnsiConsole.Write(new Panel($"[bold]Edit Environment: {envName}[/]")
				{
					Border = BoxBorder.Double
				});
				AnsiConsole.WriteLine();
				
				// Show current values
				var currentValuesTable = new Table()
					.Border(TableBorder.Rounded)
					.AddColumn(new TableColumn("[bold]Field[/]"))
					.AddColumn(new TableColumn("[bold]Current Value[/]"));
				
				currentValuesTable.AddRow("0. Name", editedName);
				currentValuesTable.AddRow("1. URL", editedEnv.Uri ?? "[dim]not set[/]");
				currentValuesTable.AddRow("2. Login", editedEnv.Login ?? "[dim]not set[/]");
				currentValuesTable.AddRow("3. Password", _service.MaskSensitiveData("Password", editedEnv.Password));
				currentValuesTable.AddRow("4. IsNetCore", editedEnv.IsNetCore ? "[green]Yes[/]" : "[red]No[/]");
				currentValuesTable.AddRow("5. Maintainer", editedEnv.Maintainer ?? "[dim]not set[/]");
				currentValuesTable.AddRow("6. Client ID", editedEnv.ClientId ?? "[dim]not set[/]");
				currentValuesTable.AddRow("7. Client Secret", _service.MaskSensitiveData("ClientSecret", editedEnv.ClientSecret));
				currentValuesTable.AddRow("8. Safe Mode", (editedEnv.Safe ?? false) ? "[green]Yes[/]" : "[red]No[/]");
				currentValuesTable.AddRow("9. Developer Mode", (editedEnv.DeveloperModeEnabled ?? false) ? "[green]Yes[/]" : "[red]No[/]");
				currentValuesTable.AddRow("10. Workspace Paths", editedEnv.WorkspacePathes ?? "[dim]not set[/]");
				
				AnsiConsole.Write(currentValuesTable);
				AnsiConsole.WriteLine();
				
				// Build field choices and reorder to position cursor on last selected field
				var fieldChoices = new List<string>
				{
					"0. Name",
					"1. URL",
					"2. Login",
					"3. Password",
					"4. IsNetCore",
					"5. Maintainer",
					"6. Client ID",
					"7. Client Secret",
					"8. Safe Mode",
					"9. Developer Mode",
					"10. Workspace Paths"
				};
				
				// Move last selected field to top for better UX (cursor will start there)
				if (!string.IsNullOrEmpty(lastSelectedField) && fieldChoices.Contains(lastSelectedField))
				{
					fieldChoices.Remove(lastSelectedField);
					fieldChoices.Insert(0, lastSelectedField);
				}
				
				var allChoices = new List<string>();
				allChoices.AddRange(fieldChoices);
				allChoices.Add("───────────────────");
				allChoices.Add("💾 Save Changes");
				allChoices.Add("❌ Cancel (discard changes)");
				
				// Field selection with arrow navigation
				var action = AnsiConsole.Prompt(
					new SelectionPrompt<string>()
						.Title("[green]Select field to edit or action:[/]")
						.PageSize(15)
						.AddChoices(allChoices)
				);
				
				// Remember last selected field (but not separators or actions)
				if (action.Contains(".") && !action.Contains("💾") && !action.Contains("❌") && !action.StartsWith("───"))
				{
					lastSelectedField = action;
				}
				
				// Handle action
				if (action.StartsWith("💾"))
				{
					// If name changed, delete old and create new
					if (editedName != envName)
					{
						var isActive = _settingsRepository.GetDefaultEnvironmentName() == envName;
						_settingsRepository.RemoveEnvironment(envName);
						_settingsRepository.ConfigureEnvironment(editedName, editedEnv);
						if (isActive)
						{
							_settingsRepository.SetActiveEnvironment(editedName);
						}
						AnsiConsole.MarkupLine($"\n[green]✓[/] Environment renamed from '[cyan]{envName}[/]' to '[cyan]{editedName}[/]' and updated successfully!");
					}
					else
					{
						_settingsRepository.ConfigureEnvironment(envName, editedEnv);
						AnsiConsole.MarkupLine($"\n[green]✓[/] Environment '[cyan]{envName}[/]' updated successfully!");
					}
					keepEditing = false;
				}
				else if (action.StartsWith("❌"))
				{
					AnsiConsole.MarkupLine("[yellow]Changes discarded.[/]");
					return 0;
				}
				else if (action.StartsWith("───"))
				{
					// Skip separator
					continue;
				}
				else
				{
					// Edit selected field
					AnsiConsole.WriteLine();
					
					if (action.StartsWith("0."))
					{
						var newName = AnsiConsole.Prompt(
							new TextPrompt<string>("[green]Environment name:[/]")
								.DefaultValue(editedName)
						);
						
						// Validate new name if it changed
						if (newName != envName)
						{
							var validation = _service.ValidateEnvironmentName(newName, _settingsRepository);
							if (!validation.Successful)
							{
								AnsiConsole.MarkupLine($"[red]✗ Name validation failed: {validation.Message}[/]");
								AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
								Console.ReadKey(true);
							}
							else
							{
								editedName = newName;
							}
						}
					}
					else if (action.StartsWith("1."))
					{
						var urlPrompt = new TextPrompt<string>("[green]URL:[/]")
							.DefaultValue(editedEnv.Uri ?? string.Empty)
							.Validate(url => 
							{
								if (string.IsNullOrWhiteSpace(url)) 
									return ValidationResult.Error("[red]URL cannot be empty[/]");
								return _service.ValidateUrl(url);
							});
						editedEnv.Uri = AnsiConsole.Prompt(urlPrompt).TrimEnd('/');
					}
					else if (action.StartsWith("2."))
					{
						editedEnv.Login = AnsiConsole.Ask("[green]Login:[/]", editedEnv.Login ?? string.Empty);
					}
					else if (action.StartsWith("3."))
					{
						editedEnv.Password = AnsiConsole.Prompt(
							new TextPrompt<string>("[green]Password:[/]")
								.Secret()
								.AllowEmpty()
								.DefaultValue(editedEnv.Password ?? string.Empty)
						);
					}
					else if (action.StartsWith("4."))
					{
						editedEnv.IsNetCore = AnsiConsole.Confirm(
							"[green]Is this a .NET Core environment?[/]", 
							defaultValue: editedEnv.IsNetCore
						);
					}
					else if (action.StartsWith("5."))
					{
						editedEnv.Maintainer = AnsiConsole.Ask("[green]Maintainer:[/]", editedEnv.Maintainer ?? string.Empty);
					}
					else if (action.StartsWith("6."))
					{
						editedEnv.ClientId = AnsiConsole.Ask("[green]Client ID:[/]", editedEnv.ClientId ?? string.Empty);
					}
					else if (action.StartsWith("7."))
					{
						editedEnv.ClientSecret = AnsiConsole.Prompt(
							new TextPrompt<string>("[green]Client Secret:[/]")
								.Secret()
								.AllowEmpty()
								.DefaultValue(editedEnv.ClientSecret ?? string.Empty)
						);
					}
					else if (action.StartsWith("8."))
					{
						editedEnv.Safe = AnsiConsole.Confirm(
							"[green]Enable Safe Mode?[/]", 
							defaultValue: editedEnv.Safe ?? false
						);
					}
					else if (action.StartsWith("9."))
					{
						editedEnv.DeveloperModeEnabled = AnsiConsole.Confirm(
							"[green]Enable Developer Mode?[/]", 
							defaultValue: editedEnv.DeveloperModeEnabled ?? false
						);
					}
					else if (action.StartsWith("10."))
					{
						editedEnv.WorkspacePathes = AnsiConsole.Ask("[green]Workspace Paths:[/]", editedEnv.WorkspacePathes ?? string.Empty);
					}
				}
			}
			
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to edit environment: {ex.Message}");
			return 1;
		}
	}

	private int DeleteEnvironment()
	{
		var environments = _settingsRepository.GetAllEnvironments();
		
		if (!environments.Any())
		{
			AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
			return 0;
		}
		
		var envName = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("[green]Select environment to delete:[/]")
				.AddChoices(environments.Keys)
		);
		
		var env = environments[envName];
		var activeEnv = _settingsRepository.GetDefaultEnvironmentName();
		var isActive = envName == activeEnv;
		
		Console.Clear();
		
		// Warning panel
		var warningText = $"You are about to delete environment: [red bold]{envName}[/]\n" +
		                  $"URL: {env.Uri}\n\n";
		
		if (isActive)
		{
			warningText += "[yellow bold]⚠ This is the ACTIVE environment![/]\n\n";
		}
		
		warningText += "[red]This action cannot be undone.[/]";
		
		var warningPanel = new Panel(warningText)
		{
			Header = new PanelHeader("[red bold]⚠ WARNING ⚠[/]", Justify.Center),
			Border = BoxBorder.Double,
			BorderStyle = new Style(Color.Red)
		};
		
		AnsiConsole.Write(warningPanel);
		
		if (isActive)
		{
			AnsiConsole.MarkupLine("\n[yellow]Note: You will need to set a new active environment after deletion.[/]");
		}
		
		var confirmed = AnsiConsole.Confirm(
			$"\n[red]Are you absolutely sure you want to delete '{envName}'?[/]", 
			defaultValue: false
		);
		
		if (!confirmed)
		{
			AnsiConsole.MarkupLine("[yellow]Deletion cancelled.[/]");
			return 0;
		}
		
		try
		{
			_settingsRepository.RemoveEnvironment(envName);
			AnsiConsole.MarkupLine($"\n[green]✓[/] Environment '[cyan]{envName}[/]' deleted successfully!");
			
			// If deleted active environment, prompt to set new one
			if (isActive)
			{
				var remaining = _settingsRepository.GetAllEnvironments();
				if (remaining.Any())
				{
					var newActive = AnsiConsole.Prompt(
						new SelectionPrompt<string>()
							.Title("[yellow]Select new active environment:[/]")
							.AddChoices(remaining.Keys)
					);
					
					_settingsRepository.SetActiveEnvironment(newActive);
					AnsiConsole.MarkupLine($"[green]✓[/] '[cyan]{newActive}[/]' is now the active environment");
				}
				else
				{
					AnsiConsole.MarkupLine("[yellow]No environments remaining. Create a new one to continue.[/]");
				}
			}
			
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to delete environment: {ex.Message}");
			return 1;
		}
	}

	private int SetActiveEnvironment()
	{
		var environments = _settingsRepository.GetAllEnvironments();
		var currentActive = _settingsRepository.GetDefaultEnvironmentName();
		
		if (!environments.Any())
		{
			AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
			return 0;
		}
		
		var choices = environments.Keys.ToList();
		var prompt = new SelectionPrompt<string>()
			.Title("[green]Select active environment:[/]")
			.AddChoices(choices)
			.UseConverter(env => env == currentActive ? $"{env} [dim](current)[/]" : env);
		
		var selected = AnsiConsole.Prompt(prompt);
		
		if (selected == currentActive)
		{
			AnsiConsole.MarkupLine($"[yellow]'{selected}' is already the active environment.[/]");
			return 0;
		}
		
		try
		{
			_settingsRepository.SetActiveEnvironment(selected);
			AnsiConsole.MarkupLine($"[green]✓[/] '[cyan]{selected}[/]' is now the active environment");
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to set active environment: {ex.Message}");
			return 1;
		}
	}

	#endregion

	#region Enums: Private

	private enum MenuChoice
	{
		ViewDetails,
		Create,
		Edit,
		Delete,
		SetActive,
		Refresh,
		Exit
	}

	#endregion
}

#endregion
