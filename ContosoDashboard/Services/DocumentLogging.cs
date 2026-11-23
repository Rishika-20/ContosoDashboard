namespace ContosoDashboard.Services;

public static class DocumentLogging
{
    public static void AuthorizationDenied(ILogger logger, int actorId, int? documentId, string operation)
        => logger.LogWarning("Document authorization denied: actor={ActorId} document={DocumentId} operation={Operation}", actorId, documentId, operation);
}
