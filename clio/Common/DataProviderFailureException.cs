using System;

namespace Clio.Common;

/// <summary>
/// A failure that <see cref="ClassifyingDataProvider"/> raised because an ATF response was unsuccessful
/// and nothing in it named a rejected credential.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so every existing handler keeps working unchanged
/// - in particular <c>SysSettingsCommand.CategorizeFailure</c>'s <c>InvalidOperationException invEx =&gt;
/// invEx.Message</c> arm, which is what surfaces the composed diagnostic.
/// <para>
/// The distinct type exists so a consumer can tell "the data provider failed, and its message is the only
/// diagnosis available" apart from an ordinary <see cref="InvalidOperationException"/> raised by clio's own
/// argument or state checks. <c>SchemaNamePrefixTool</c> needs exactly that distinction: it surfaces this
/// message verbatim, while keeping the deliberately generic "Failed to read SchemaNamePrefix." label for
/// everything else (an unregistered environment name, for instance, must not have its resolver text
/// promoted into the tool's error field).
/// </para>
/// </remarks>
public sealed class DataProviderFailureException : InvalidOperationException {

	/// <summary>Creates the failure with a composed diagnostic.</summary>
	/// <param name="message">The diagnostic to surface to the caller.</param>
	public DataProviderFailureException(string message) : base(message) { }

	/// <summary>Creates the failure with a composed diagnostic and the underlying fault.</summary>
	/// <param name="message">The diagnostic to surface to the caller.</param>
	/// <param name="innerException">The failure the provider reported.</param>
	public DataProviderFailureException(string message, Exception innerException)
		: base(message, innerException) { }
}
