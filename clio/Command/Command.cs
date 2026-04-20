using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Common;
using FluentValidation.Results;

namespace Clio.Command
{
	public abstract class Command<TEnvironmentOptions>
	{
		public abstract int Execute(TEnvironmentOptions options);

		protected static void PrintErrors(IEnumerable<ValidationFailure> errors) {
			PrintErrors(errors, ConsoleLogger.Instance);
		}

		protected static void PrintErrors(IEnumerable<ValidationFailure> errors, ILogger logger) {
			errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
				  .ToList().ForEach(e => logger.WriteLine(
					  $"{e.Severity.ToString().ToUpper(CultureInfo.InvariantCulture)} ({e.ErrorCode}) - {e.ErrorMessage}"));
		}
	}
}
