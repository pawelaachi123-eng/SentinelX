namespace SentinelX
{
    public sealed class ActionExecutionResult
    {
        public bool Success { get; init; }

        public bool Verified { get; init; }

        public bool IsCancelled { get; init; }

        public string Status => IsCancelled ? "CANCELLED" : !Success ? "FAILED" : Verified ? "VERIFIED" : "UNVERIFIED";

        public string RecoveryAdvice { get; init; } = string.Empty;

        public string Message { get; init; } =
            string.Empty;

        public string Evidence { get; init; } =
            string.Empty;


        public static ActionExecutionResult VerifiedSuccess(
            string message,
            string evidence)
        {
            return new ActionExecutionResult
            {
                Success = true,
                Verified = true,
                Message = message,
                Evidence = evidence
            };
        }


        public static ActionExecutionResult UnverifiedSuccess(
            string message,
            string evidence)
        {
            return new ActionExecutionResult
            {
                Success = true,
                Verified = false,
                Message = message,
                Evidence = evidence
            };
        }


        public static ActionExecutionResult Failure(
            string message,
            string evidence = "",
            string recoveryAdvice = "")
        {
            return new ActionExecutionResult
            {
                Success = false,
                Verified = false,
                Message = message,
                Evidence = evidence,
                RecoveryAdvice = recoveryAdvice
            };
        }

        public static ActionExecutionResult Cancelled(string evidence = "Przerwano oczekiwanie i dalsze kroki. Wcześniej wykonane kroki nie są automatycznie cofane.") => new()
        {
            IsCancelled = true,
            Message = "Przerwano zadanie.",
            Evidence = evidence
        };
    }


    public sealed class ToolboxCommandResult
    {
        public bool Handled { get; init; }

        public string Response { get; init; } =
            string.Empty;


        public static ToolboxCommandResult NotHandled()
        {
            return new ToolboxCommandResult
            {
                Handled = false
            };
        }


        public static ToolboxCommandResult HandledWith(
            string response)
        {
            return new ToolboxCommandResult
            {
                Handled = true,
                Response = response
            };
        }
    }
}
