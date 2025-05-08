namespace Clio.Common.ScenarioHandlers;

public class BaseHandlerResponse
{

    #region Enum: Public

    public enum CompletionStatus
    {

        Success,
        Failure

    }

    #endregion

    #region Properties: Public

    public string Description { get; set; }

    public CompletionStatus Status { get; set; }

    #endregion

}
