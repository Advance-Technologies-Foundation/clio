namespace Clio.Common.ScenarioHandlers;

public class BaseHandlerResponse
{
    public enum CompletionStatus
    {
        Success,
        Failure
    }

    public CompletionStatus Status { get; set; }

    public string Description { get; set; }
}
