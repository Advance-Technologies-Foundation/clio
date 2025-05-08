using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Web;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Requests.Validators;

internal class ISSScannerValidator : AbstractValidator<IISScannerRequest>
{
    public ISSScannerValidator() =>
        RuleFor(r => r.Content).Cascade(CascadeMode.Stop).Custom((value, context) =>
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                context.AddFailure(new ValidationFailure
                {
                    ErrorCode = "OS001",
                    ErrorMessage =
                        $"Not supported OS Platform - IIS Scanner is only supported on {OSPlatform.Windows}",
                    Severity = Severity.Error,
                    AttemptedValue = value
                });
            }
        }).Custom((value, context) =>
        {
            Uri.TryCreate(value, UriKind.Absolute, out Uri uri);
            NameValueCollection nvc = HttpUtility.ParseQueryString(uri.Query);

            string returnType = nvc["return"];

            if (string.IsNullOrEmpty(returnType) || string.IsNullOrWhiteSpace(returnType))
            {
                context.AddFailure(new ValidationFailure
                {
                    ErrorCode = "ARG001",
                    ErrorMessage = "Return type cannot be empty",
                    Severity = Severity.Error,
                    AttemptedValue = value
                });
            }
        }).Custom((value, context) =>
        {
            Uri.TryCreate(value, UriKind.Absolute, out Uri uri);
            NameValueCollection nvc = HttpUtility.ParseQueryString(uri.Query);

            string returnType = nvc["return"].ToLower(CultureInfo.InvariantCulture);

            string[] allowedValues = ["count", "details", "registerall", "remote"];
            if (Array.IndexOf(allowedValues, returnType) < 0)
            {
                context.AddFailure(new ValidationFailure
                {
                    ErrorCode = "ARG002",
                    ErrorMessage = $"Return type must be one of {string.Join(", ", allowedValues)}",
                    Severity = Severity.Error,
                    AttemptedValue = value
                });
            }
        });
}
