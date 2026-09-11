using System;

namespace Clio.Command.Administration;

/// <summary>Contains a locally authored administration diagnosis that is safe to show to the operator.</summary>
/// <param name="message">A fixed diagnostic that contains no native response or secret value.</param>
public sealed class AdministrationStateException(string message) : InvalidOperationException(message);
