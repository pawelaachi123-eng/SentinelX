using System;
using System.Threading.Tasks;

namespace SentinelX
{
    public sealed class PendingPermissionAction
    {
        public string ActionId { get; init; } =
            string.Empty;

        public string ActionType { get; init; } =
            string.Empty;

        public string OriginalCommand { get; init; } =
            string.Empty;

        public string Description { get; init; } =
            string.Empty;

        public string RiskLevel { get; init; } =
            "MEDIUM";

        public DateTime CreatedAt { get; init; } =
            DateTime.Now;

        internal Func<Task<ActionExecutionResult>> Executor
        {
            get;
            init;
        } =
            () =>
                Task.FromResult(
                    ActionExecutionResult.Failure(
                        "Brak wykonawcy akcji."));
    }


    public sealed class PermissionExecutionResult
    {
        public bool HadPendingAction { get; init; }

        public PendingPermissionAction? Action { get; init; }

        public ActionExecutionResult? Result { get; init; }
    }


    public sealed class PermissionCenterService
    {
        private readonly object syncRoot =
            new object();

        private PendingPermissionAction? pendingAction;


        public bool HasPendingAction
        {
            get
            {
                lock (syncRoot)
                {
                    return pendingAction != null;
                }
            }
        }


        public bool TryRequest(
            PendingPermissionAction action,
            out string response)
        {
            lock (syncRoot)
            {
                if (pendingAction != null)
                {
                    response =
                        $"""
                        Masz już akcję oczekującą na decyzję:

                        {pendingAction.ActionId}
                        {pendingAction.Description}

                        Wpisz w polu komendy albo kliknij przycisk:
                        potwierdz
                        albo:
                        anuluj
                        """;


                    return false;
                }


                pendingAction =
                    action;


                response =
                    $"""
                    PERMISSION CENTER

                    Action ID:
                    {action.ActionId}

                    Ryzyko:
                    {action.RiskLevel}

                    Akcja:
                    {action.Description}

                    Wpisz w polu komendy albo kliknij przycisk:
                    potwierdz

                    albo:
                    anuluj
                    """;


                return true;
            }
        }


        public string GetPendingSummary()
        {
            lock (syncRoot)
            {
                if (pendingAction == null)
                {
                    return
                        "Brak akcji oczekujących na potwierdzenie.";
                }


                return
                    $"""
                    OCZEKUJĄCA AKCJA

                    {pendingAction.ActionId}

                    {pendingAction.Description}

                    Ryzyko:
                    {pendingAction.RiskLevel}
                    """;
            }
        }


        public async Task<PermissionExecutionResult>
            ConfirmAsync()
        {
            PendingPermissionAction? action;


            lock (syncRoot)
            {
                action =
                    pendingAction;


                pendingAction =
                    null;
            }


            if (action == null)
            {
                return new PermissionExecutionResult
                {
                    HadPendingAction =
                        false
                };
            }


            if (DateTime.Now -
                action.CreatedAt >
                TimeSpan.FromMinutes(10))
            {
                return new PermissionExecutionResult
                {
                    HadPendingAction =
                        true,

                    Action =
                        action,

                    Result =
                        ActionExecutionResult.Failure(
                            "Potwierdzenie wygasło.",
                            "Akcja oczekiwała dłużej niż 10 minut.")
                };
            }


            ActionExecutionResult result;


            try
            {
                result =
                    await action.Executor();
            }
            catch (Exception ex)
            {
                result =
                    ActionExecutionResult.Failure(
                        "Wykonanie akcji zakończyło się błędem.",
                        ex.Message);
            }


            return new PermissionExecutionResult
            {
                HadPendingAction =
                    true,

                Action =
                    action,

                Result =
                    result
            };
        }


        public PendingPermissionAction? Cancel()
        {
            lock (syncRoot)
            {
                PendingPermissionAction? action =
                    pendingAction;


                pendingAction =
                    null;


                return action;
            }
        }
    }
}
