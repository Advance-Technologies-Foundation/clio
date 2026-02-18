using System;
using System.IO;
using Autofac.Core;

namespace Clio;

internal static class ExceptionReadableMessageExtension
{
	public static string GetReadableMessageException(this Exception exception, bool debug = false)
	{
		if (debug) return exception.ToString();
		return exception switch
		{
			FileNotFoundException ex => $"{ex.Message}{ex.FileName}",
			DependencyResolutionException ex => ex.InnerException?.Message ?? ex.Message,
			InvalidOperationException ex => ex.InnerException?.Message ?? ex.Message,
			_ => exception.Message
		};
	}
}
