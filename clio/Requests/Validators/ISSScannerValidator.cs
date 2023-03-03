using FluentValidation;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Clio.Requests.Validators
{
	internal class ISSScannerValidator : AbstractValidator<IISScannerRequest>
	{
		public ISSScannerValidator()
		{
			RuleFor(r => r.Content).Cascade(CascadeMode.Stop).
			Custom((value, context) =>
			{
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					context.AddFailure(new FluentValidation.Results.ValidationFailure()
					{
						ErrorCode = "OS001",
						ErrorMessage = $"Not supported OS Platform - IIS Scanner is only supported on {OSPlatform.Windows}",
						Severity = Severity.Error,
						AttemptedValue = value,
					});
				}
			}).
			Custom((value, context) =>
			{
				Uri.TryCreate(value, UriKind.Absolute, out Uri uri);
				NameValueCollection nvc = System.Web.HttpUtility.ParseQueryString(uri.Query);

				string returnType = nvc["return"];

				if (string.IsNullOrEmpty(returnType) || string.IsNullOrWhiteSpace(returnType))
				{
					context.AddFailure(new FluentValidation.Results.ValidationFailure()
					{
						ErrorCode = "ARG001",
						ErrorMessage = "Return type cannot be empty",
						Severity = Severity.Error,
						AttemptedValue = value,
					});

				}
			}).
			Custom((value, context) =>
			{
				Uri.TryCreate(value, UriKind.Absolute, out Uri uri);
				NameValueCollection nvc = System.Web.HttpUtility.ParseQueryString(uri.Query);

				string returnType = nvc["return"].ToLower(CultureInfo.InvariantCulture);

				string[] allowedValues = new[] { "count", "details", "registerall", "remote" };
				if (Array.IndexOf(allowedValues, returnType) < 0)
				{
					context.AddFailure(new FluentValidation.Results.ValidationFailure()
					{
						ErrorCode = "ARG002",
						ErrorMessage = $"Return type must be one of {string.Join(", ", allowedValues)}",
						Severity = Severity.Error,
						AttemptedValue = value,
					});
				}
			});
		}
	}

}
