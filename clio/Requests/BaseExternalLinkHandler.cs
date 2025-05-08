using System;
using System.Collections.Specialized;

namespace Clio.Requests;

internal class BaseExternalLinkHandler
{
    /// <summary>
    /// Request Uri.
    /// </summary>
    protected Uri _clioUri;

    /// <summary>
    /// Gets collection of Query parameters.
    /// </summary>
    protected NameValueCollection ClioParams => System.Web.HttpUtility.ParseQueryString(_clioUri.Query);
}
