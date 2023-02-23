using System;
using System.Collections.Specialized;

namespace Clio.Requests
{
	public abstract class BaseExternalLinkHandler
	{
		/// <summary>
		/// Request Uri
		/// </summary>
		protected Uri _clioUri;
		protected NameValueCollection clioParams => System.Web.HttpUtility.ParseQueryString(_clioUri.Query);

		/// <summary>
		/// Check if Uri is valid
		/// </summary>
		/// <param name="content">URI to validate</param>
		/// <returns>true when Uri parses correctly, otherwise false</returns>
		protected virtual bool IsLinkValid(string content)
		{
			if (Uri.TryCreate(content, UriKind.Absolute, out _clioUri))
			{
				if (_clioUri.Scheme != "clio")
				{
					Console.Error.WriteLine("ERROR (UriScheme) - Not a clio URI");
					return false;
				}
			}
			else
			{
				Console.Error.WriteLine("ERROR (Uri) - Clio URI cannot be empty");
				return false;
			}
			return true;
		}


		/// <summary>
		/// Prints all arguments passed in the clio link
		/// </summary>
		protected virtual void PrintArguments()
		{
			Console.WriteLine("clio was called with:");
			for (var i = 0; i < clioParams.Count; i++)
			{
				var key = clioParams.Keys[i];
				var value = clioParams.GetValues(i)?[0];
				Console.WriteLine($"\t{key} - {value}");
			}
		}

	}
}
