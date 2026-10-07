using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Infrastructure.BackgroundServices;

internal class BulkUserBackgroundService : BackgroundService
{
    private readonly IBulkJobQueue _jobQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BulkUserBackgroundService> _logger;

    public BulkUserBackgroundService(
        IBulkJobQueue jobQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<BulkUserBackgroundService> logger)
    {
        _jobQueue = jobQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BulkUserJob job;
            try
            {
                job = await _jobQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                job.State = BulkJobState.InProgress;
                using var scope = _scopeFactory.CreateScope();

                // Before anything in this scope reaches a directory. There is no HttpContext on
                // this thread, so without this the batch resolves to the configured default
                // whatever domain it was queued against — see LdapProviderScope. A job whose
                // domain has since been deactivated or gone unreachable now throws out of the
                // module call and is recorded as Failed below, which is the right answer: better
                // a failed batch than a hundred accounts in the wrong directory.
                scope.ServiceProvider.GetRequiredService<LdapProviderScope>().Use(job.ProviderId);

                // And as whom. The batch runs as the person who queued it, so its rows are
                // authorised against what they hold now and audited under their name — rather
                // than running as nobody, unchecked and unattributed.
                scope.ServiceProvider.GetRequiredService<BackgroundPrincipal>().RunAs(job.RequestedBy, job.RequestedByDn, job.ProviderId);

                // The same way in the HTTP API uses. This service used to build the command and
                // send it through ISender itself, which made it the one caller outside Application
                // reaching past the module surface — and the one reason the command types had to
                // stay public.
                var users = scope.ServiceProvider.GetRequiredService<IUsersModule>();
                var response = await users.ProcessBulkAsync(job.Requests, stoppingToken);
                job.Results.Clear();
                job.Results.AddRange(response.Items);
                job.State = response.FailedCount > 0 ? BulkJobState.Failed : BulkJobState.Completed;
                job.ErrorMessage = response.ErrorMessage;
                job.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                job.State = BulkJobState.Failed;
                job.CompletedAt = DateTime.UtcNow;
                // Do not surface ex.Message to API clients polling GetBulkJobStatusAsync —
                // the real exception (which can include LDAP server details) goes to the
                // server log only.
                job.ErrorMessage = "Bulk job processing failed unexpectedly. See server logs for details.";
                _logger.LogError(ex, "Background bulk user processing failed for job {JobId}", job.Id);
            }
        }
    }
}
