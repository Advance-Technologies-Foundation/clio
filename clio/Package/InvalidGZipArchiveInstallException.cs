namespace Clio.Package
{
	using System;

	/// <summary>
	/// Represents an installation failure caused by an invalid or corrupted GZip archive.
	/// </summary>
	public sealed class InvalidGZipArchiveInstallException : Exception
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="InvalidGZipArchiveInstallException"/> class.
		/// </summary>
		/// <param name="message">The Creatio error message describing the invalid archive.</param>
		public InvalidGZipArchiveInstallException(string message) : base(message) { }
	}
}
