namespace Clio.Common.ScenarioHandlers {
    public class BaseHandlerResponse {

        public CompletionStatus Status { get; set; }
        public string Description { get; set; }
        public enum CompletionStatus {
            Success, Failure
        }
    }
}
