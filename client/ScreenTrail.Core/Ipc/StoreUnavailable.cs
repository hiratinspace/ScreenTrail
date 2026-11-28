using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Ipc;

/// <summary>How a time in the <c>store_unavailable</c> state ended.</summary>
public enum StoreUnavailableOutcome
{
    /// <summary>The technician asked to try the store again (<c>retry_store</c>).</summary>
    Retry,

    /// <summary>"Delete everything" was confirmed and the erasure is written down as owed (INV-12).</summary>
    Erase,
}

/// <summary>
/// What the service does when its store will not open (F-10, contract v19). It used to throw at every start
/// and exit, so the tray said only "not connected" and Settings and "Delete everything", which both go
/// through the pipe, were out of reach.
///
/// Now it serves the pipe in the <c>store_unavailable</c> state, which captures nothing and says why, and
/// accepts two commands: <c>retry_store</c>, because a key that will not open today can come back (a
/// password restored), and "Delete everything" with its typed confirmation, because the erase is final.
/// Everything else is refused in words. The key file is never replaced here: only the erasure takes it.
/// </summary>
public sealed class StoreUnavailable : IIpcCommandHandler
{
    /// <summary>What every other command is answered with.</summary>
    public const string Refusal = "ScreenTrail cannot open its saved sessions, so nothing is being captured. Try again, or delete everything to start afresh.";

    /// <summary>The answer to a "Delete everything" whose confirmation was not one this service issued.</summary>
    public const string NotConfirmed = "Nothing was deleted: the confirmation was not accepted.";

    /// <summary>
    /// The pipe's audit log while the store is closed: the log is kept in the store, so there is nowhere to
    /// write it, and nothing in this state is captured, published or deleted but by the erasure.
    /// </summary>
    public static IAuditLog NoAudit { get; } = new NowhereToWrite();

    private readonly string _reason;
    private readonly Func<CapabilitiesReported> _capabilities;
    private readonly Action _markErasureOwed;
    private readonly Confirmations _confirmations;
    private readonly TaskCompletionSource<StoreUnavailableOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="reason">One of <see cref="StoreUnavailableReasons"/>.</param>
    /// <param name="capabilities">What Windows allows, as the pipe reports it in every state.</param>
    /// <param name="markErasureOwed">
    /// Writes down that everything is to go (<c>LocalDataEraser.MarkPending</c>), before the UI is told yes.
    /// The erasure itself runs once the pipe is closed, and a start that finds it owed finishes it.
    /// </param>
    /// <param name="time">The clock the confirmations expire by; the system's when null.</param>
    public StoreUnavailable(string reason, Func<CapabilitiesReported> capabilities, Action markErasureOwed, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _reason = reason;
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _markErasureOwed = markErasureOwed ?? throw new ArgumentNullException(nameof(markErasureOwed));
        _confirmations = new Confirmations(time);
    }

    /// <summary>Completes when the technician asks to retry or confirms "Delete everything".</summary>
    public Task<StoreUnavailableOutcome> Outcome => _outcome.Task;

    public CaptureStateSnapshot CurrentState => new() { State = CaptureStates.StoreUnavailable, Reason = _reason };

    public CapabilitiesReported CurrentCapabilities => _capabilities();

    public Task<IpcEvent?> ReplyToAsync(IpcCommand command, Guid caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The one question answered: the token for "Delete everything", with the phrase the service expects,
        // as the running service issues it. Every other question gets no reply and is refused below.
        IpcEvent? reply = command is RequestConfirmationCommand { Action: "erase_everything", SessionIds: null }
            ? new ConfirmationIssued { Token = _confirmations.Issue(DestructiveAction.EraseEverything, caller), Phrase = "DELETE EVERYTHING" }
            : null;
        return Task.FromResult(reply);
    }

    public Task<CommandResult> HandleAsync(IpcCommand command, Guid caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        string? error;
        switch (command)
        {
            case RetryStoreCommand:
                error = null;
                _ = _outcome.TrySetResult(StoreUnavailableOutcome.Retry);
                break;

            case EraseAllLocalDataCommand erase when !_outcome.Task.IsCompleted:
                if (_confirmations.Spend(erase.Confirmation, DestructiveAction.EraseEverything, caller))
                {
                    _markErasureOwed();
                    error = null;
                    _ = _outcome.TrySetResult(StoreUnavailableOutcome.Erase);
                }
                else
                {
                    error = NotConfirmed;
                }

                break;

            default:
                error = Refusal;
                break;
        }

        return Task.FromResult(new CommandResult { RequestId = command.RequestId, Ok = error is null, Error = error });
    }

    /// <summary>
    /// Opens the store, and while it will not open with its key, serves <paramref name="serve"/> until the
    /// technician retries or erases. Null after an erase: the caller stops, and the erasure runs then.
    /// </summary>
    /// <param name="open">Opens the store; throws <see cref="StoreKeyException"/> when the key will not do.</param>
    /// <param name="serve">Serves the pipe in the unavailable state, for the reason given, until it ends.</param>
    /// <param name="ct">Stops the service.</param>
    public static async Task<TStore?> OpenOrServeAsync<TStore>(
        Func<CancellationToken, Task<TStore>> open,
        Func<string, CancellationToken, Task<StoreUnavailableOutcome>> serve,
        CancellationToken ct)
        where TStore : class
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(serve);
        while (true)
        {
            string reason;
            try
            {
                return await open(ct).ConfigureAwait(false);
            }
            catch (StoreKeyException refused)
            {
                reason = refused.Reason;
            }

            if (await serve(reason, ct).ConfigureAwait(false) == StoreUnavailableOutcome.Erase)
            {
                return null;
            }
        }
    }

    private sealed class NowhereToWrite : IAuditLog
    {
        public Task RecordAsync(string type, string? sessionId = null, long? count = null, string? detail = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
