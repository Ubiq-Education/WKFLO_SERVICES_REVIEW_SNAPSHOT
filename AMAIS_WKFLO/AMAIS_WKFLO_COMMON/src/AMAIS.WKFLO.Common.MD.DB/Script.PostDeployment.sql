/*
Post-Deployment Script
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.
 SqlPackage/DacFx runs it AFTER the generated schema-sync statements, so it can rely on
 every column added by this deployment already existing.
 Every statement here must be idempotent: this script runs on every deployment.
--------------------------------------------------------------------------------------
*/

-- US 7507 (MAISY conversations list): backfill [Conversation].[PreviewLine] for conversations created
-- before the column existed. New rows get the first user message (300 chars) from
-- UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler; [Name] held exactly that value until AI
-- title generation started replacing it, so it is the best available preview for existing rows.
-- Without this, every pre-existing conversation would show "New conversation" in the list.
--
-- Safe to run on every deployment:
--   * Idempotent: only rows with [PreviewLine] IS NULL AND [Name] IS NOT NULL are touched, so after the
--     first run it updates nothing (or only the odd row that still matches).
--   * Batched: it walks the clustered primary key in ranges of @PreviewLineBatchSize ids, so each UPDATE
--     is a short range seek that locks at most that many rows (below the lock escalation threshold) and
--     never re-reads rows an earlier batch already handled. Rows inserted while it runs are above
--     @PreviewLineMaxId and already get their PreviewLine from the application.
DECLARE @PreviewLineBatchSize INT = 4000;
DECLARE @PreviewLineFromId INT;
DECLARE @PreviewLineMaxId INT;

SELECT @PreviewLineFromId = MIN([Id]), @PreviewLineMaxId = MAX([Id]) FROM [dbo].[Conversation];

WHILE @PreviewLineFromId IS NOT NULL AND @PreviewLineFromId <= @PreviewLineMaxId
BEGIN
    UPDATE [dbo].[Conversation]
    SET [PreviewLine] = [Name]
    WHERE [Id] >= @PreviewLineFromId
      AND [Id] < @PreviewLineFromId + @PreviewLineBatchSize
      AND [PreviewLine] IS NULL
      AND [Name] IS NOT NULL;

    SET @PreviewLineFromId = @PreviewLineFromId + @PreviewLineBatchSize;
END;
GO
