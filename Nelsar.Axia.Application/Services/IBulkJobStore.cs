namespace Nelsar.Axia.Application.Services;

public interface IBulkJobStore
{
    void Add(BulkUserJob job);
    BulkUserJob? Get(string jobId);
}
