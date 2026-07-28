namespace Clio.Command.PackageCommand
{
	using System;

	/// <summary>
	/// Thrown when a Creatio license service call (upload or user distribution) reports a failure.
	/// </summary>
	public class LicenseInstallationException : Exception
	{
		public LicenseInstallationException(string message) : base(message) { }
	}
}
