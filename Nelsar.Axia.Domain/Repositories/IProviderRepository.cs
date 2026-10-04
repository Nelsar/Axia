using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Domain.Repositories;

public interface IProviderRepository
{
    Task<IEnumerable<Provider>> GetAllAsync();
    Task<Provider?> GetByIdAsync(Guid id);
    Task<Provider> AddAsync(Provider provider);
    Task<Provider?> UpdateAsync(Provider provider);
    Task<bool> DeleteAsync(Guid id);
}
