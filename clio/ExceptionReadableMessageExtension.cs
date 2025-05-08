using System;
using System.IO;
using Autofac.Core;

namespace Clio;

internal static class ExceptionReadableMessageExtension
{

    #region Methods: Public

    public static string GetReadableMessageException(this Exception exception, bool debug = false)
    {
        if (debug)
        {
            return exception.ToString();
        }
        return exception switch
               {
                   FileNotFoundException ex => $"{ex.Message}{ex.FileName}",
                   DependencyResolutionException ex => ex.InnerException?.Message ?? ex.Message,
                   var _ => exception.Message
               };
    }

    #endregion

}
