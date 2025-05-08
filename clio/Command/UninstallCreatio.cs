using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

public class UninstallCreatioCommandOptionsValidator : AbstractValidator<UninstallCreatioCommandOptions>
{
    public UninstallCreatioCommandOptionsValidator(IFileSystem fileSystem)
    {
        RuleFor(o => string.IsNullOrWhiteSpace(o.PhysicalPath) && string.IsNullOrWhiteSpace(o.EnvironmentName))
            .Cascade(CascadeMode.Stop)
            .Custom((value, context) =>
            {
                if (value)
                {
                    context.AddFailure(new ValidationFailure
                    {
                        ErrorCode = "ArgumentParse.Error",
                        ErrorMessage = "Either path to creatio directory or environment name must be provided",
                        Severity = Severity.Error
                    });
                }
            });

        RuleFor(o => !string.IsNullOrWhiteSpace(o.PhysicalPath) && !string.IsNullOrWhiteSpace(o.EnvironmentName))
            .Cascade(CascadeMode.Stop)
            .Custom((value, context) =>
            {
                if (value)
                {
                    context.AddFailure(new ValidationFailure
                    {
                        ErrorCode = "ArgumentParse.Error",
                        ErrorMessage
                            = "Either environment name or path to creatio directory must be provided, not both",
                        Severity = Severity.Error
                    });
                }
            });

        RuleFor(o => o.PhysicalPath)
            .Cascade(CascadeMode.Stop)
            .Custom((value, context) =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                bool isUri = Uri.TryCreate(value, UriKind.Absolute, out Uri dirPath);
                if (!isUri || dirPath.Scheme != Uri.UriSchemeFile)
                {
                    context.AddFailure(new ValidationFailure
                    {
                        ErrorCode = "ArgumentParse.Error",
                        ErrorMessage = "PhysicalPath must be a valid directory path",
                        Severity = Severity.Error,
                        AttemptedValue = value
                    });
                }
            })
            .Custom((value, context) =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                bool isUri = Uri.TryCreate(value, UriKind.Absolute, out Uri dirPath);
                if (!isUri || dirPath.Scheme != Uri.UriSchemeFile)
                {
                    return;
                }

                bool dirExists = fileSystem.ExistsDirectory(value);
                if (!dirExists)
                {
                    context.AddFailure(new ValidationFailure
                    {
                        ErrorCode = "ArgumentParse.Error",
                        ErrorMessage = "PhysicalPath must be a valid directory path to an Existing directory",
                        Severity = Severity.Error,
                        AttemptedValue = value
                    });
                }
            });
    }
}

[Verb("uninstall-creatio", Aliases = new[] { "uc" }, HelpText = "Uninstall local instance of creatio")]
public class UninstallCreatioCommandOptions : EnvironmentNameOptions
{
    [Option('d', "physicalPath", Required = false, HelpText = "Path to applications")]
    public string PhysicalPath { get; set; }
}

public class UninstallCreatioCommand(
    IValidator<UninstallCreatioCommandOptions> validator,
    ILogger logger,
    ICreatioUninstaller creatioUninstaller) : Command<UninstallCreatioCommandOptions>
{
    private readonly ICreatioUninstaller _creatioUninstaller = creatioUninstaller;
    private readonly ILogger _logger = logger;
    private readonly IValidator<UninstallCreatioCommandOptions> _validator = validator;

    private int PrintDoneAndExit(UninstallCreatioCommandOptions options)
    {
        if (options.EnvironmentName is not null)
        {
            _logger.WriteInfo($"Done removing Creatio instance by name: {options.EnvironmentName}");
        }

        if (options.PhysicalPath is not null)
        {
            _logger.WriteInfo($"Done removing Creatio instance by PhysicalPath: {options.PhysicalPath}");
        }

        return 0;
    }

    private int PrintErrorsAndExit(List<ValidationFailure> errors)
    {
        _logger.PrintValidationFailureErrors(errors);
        return 1;
    }

    public override int Execute(UninstallCreatioCommandOptions options)
    {
        ValidationResult validationResult = _validator.Validate(options);
        if (!validationResult.IsValid)
        {
            return PrintErrorsAndExit(validationResult.Errors);
        }

        Action act = options switch
        {
            _ when options.PhysicalPath is not null => () => _creatioUninstaller.UninstallByPath(options.PhysicalPath),
            _ when options.EnvironmentName is not null => () =>
                _creatioUninstaller.UninstallByEnvironmentName(options.EnvironmentName),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                "Either PhysicalPath or EnvironmentName must be provided")
        };
        act.Invoke();

        return PrintDoneAndExit(options);
    }
}
