namespace InstallerClean.Services;

/// <summary>
/// Thrown inside a delete or move batch where the host's question asked before each file is
/// acted on answers no (the mayAct of <see cref="IDeleteFilesService.DeleteFilesAsync"/> and
/// <see cref="IMoveFilesService.MoveFilesAsync"/>). The batch ends as a cancelled one does, with
/// what it did so far, and that file untouched.
/// </summary>
internal sealed class BatchStoppedException : OperationCanceledException;

/// <summary>What ends a delete or move batch, rather than costing one file an error.</summary>
internal static class BatchStop
{
    /// <summary>
    /// Whether <paramref name="ex"/>, thrown while one file was being handled, ends the batch:
    /// a cancellation of the batch's own <paramref name="token"/>, or the host's question
    /// answering no. Any other exception, a cancellation from elsewhere included, costs that
    /// file an error and the batch goes on.
    /// </summary>
    internal static bool EndsBatch(Exception ex, CancellationToken token) =>
        ex is BatchStoppedException || (ex is OperationCanceledException && token.IsCancellationRequested);
}
