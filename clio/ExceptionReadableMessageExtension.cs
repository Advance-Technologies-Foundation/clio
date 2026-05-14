using System;
using System.IO;
using System.Net;

namespace Clio;

internal static class ExceptionReadableMessageExtension
{
	public static string GetReadableMessageException(this Exception exception, bool debug = false)
	{
		if (debug) return exception.ToString();
		return exception switch
		{
			AggregateException ex when ex.InnerException != null
				=> ex.InnerException.GetReadableMessageException(debug),
			WebException ex when ex.Status == WebExceptionStatus.ConnectFailure
				=> $"Cannot connect to the application: {ex.Message}. Make sure the site is running and accessible.",
			FileNotFoundException ex => $"{ex.Message}{ex.FileName}",
			InvalidOperationException ex => ex.InnerException?.Message ?? ex.Message,
			_ => exception.Message
		};
	}
}
