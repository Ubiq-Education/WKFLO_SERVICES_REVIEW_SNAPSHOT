using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Worker.Application.Jobs;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Worker.WebAPI.EndPoints.Dapr
{
    [ApiController]
    public class WorkerSubscriber : ControllerBase
    {
        [Topic(CommonJobPubSubConsts.PubSubName, CommonJobPubSubConsts.JobQueuedTopic)]
        [HttpPost($"/wkflo-classification-job-queued")]
        public async Task<ActionResult> ProcessClassificationJob(
            CloudEvent<ClassificationJobQueuedMessage> cloudEvent,
            [FromServices] IMasterDataDbContext db,
            [FromServices] IEnumerable<IJobHandler> jobHandlers,
            [FromServices] IDateTimeProvider dateTimeProvider,
            [FromServices] ILogger<WorkerSubscriber> logger)
        {
            var jobId = cloudEvent.Data?.JobId ?? 0;
            if (jobId <= 0)
            {
                logger.LogWarning("Received classification job message with invalid JobId");
                return Ok();
            }

            ClassificationJob job = null;
            try
            {
                job = await db.ClassificationJobs
                    .Where(j => j.Id == jobId)
                    .FirstOrDefaultAsync();

                if (job == null)
                {
                    logger.LogWarning("ClassificationJob {JobId} not found", jobId);
                    return Ok();
                }

                job.ProcessingStartAt = dateTimeProvider.DateTimeOffsetUtcNow();
                await db.SaveChangesAsync(CancellationToken.None);

                var handler = jobHandlers.FirstOrDefault(h => h.JobType == job.JobType);
                if (handler == null)
                {
                    logger.LogError("No handler registered for job type {JobType}, job {JobId}", job.JobType, jobId);
                    job.FinishedAt = dateTimeProvider.DateTimeOffsetUtcNow();
                    job.IsSuccess = false;
                    job.ErrorMessage = $"No handler for job type: {job.JobType}";
                    await db.SaveChangesAsync(CancellationToken.None);
                    return Ok();
                }

                await handler.ExecuteAsync(job, HttpContext.RequestAborted);

                job.FinishedAt = dateTimeProvider.DateTimeOffsetUtcNow();
                job.IsSuccess = true;
                await db.SaveChangesAsync(CancellationToken.None);

                logger.LogInformation("ClassificationJob {JobId} completed successfully", jobId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ClassificationJob {JobId} failed", jobId);

                if (job != null)
                {
                    try
                    {
                        job.FinishedAt = dateTimeProvider.DateTimeOffsetUtcNow();
                        job.IsSuccess = false;
                        job.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (Exception saveEx)
                    {
                        logger.LogError(saveEx, "Failed to persist error state for job {JobId}", jobId);
                    }
                }
            }

            // Always return Ok so Dapr marks the message as processed
            return Ok();
        }
    }
}
