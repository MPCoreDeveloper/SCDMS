namespace Scdms.Services;

/// <summary>
/// Groups the session-scoped viewer services so Razor Page models do not need to
/// take each dependency individually (keeps constructor parameter counts within
/// SonarCloud's S107 limit).
/// </summary>
public sealed class ViewerSessionServices(
    IViewerConnectionService connectionService,
    IViewerTransactionService transactionService)
{
    public IViewerConnectionService ConnectionService { get; } = connectionService;

    public IViewerTransactionService TransactionService { get; } = transactionService;
}
