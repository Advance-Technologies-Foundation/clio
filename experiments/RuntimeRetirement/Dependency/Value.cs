namespace ProbeDependency;
/// <summary>A private dependency deliberately invoked only after the loading probe starts.</summary>
public static class Value { public static string Read() => "private-dependency-ok"; }
