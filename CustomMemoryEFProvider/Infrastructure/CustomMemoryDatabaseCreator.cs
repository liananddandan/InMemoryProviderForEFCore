using Microsoft.EntityFrameworkCore.Storage;

namespace CustomMemoryEFProvider.Infrastructure;

// ========== Optional (But Required for Full Provider Functionality) ==========
// Dummy IDatabaseCreator (EF Core needs this to avoid other infrastructure errors)
public class CustomMemoryDatabaseCreator : IDatabaseCreator
{
    public bool CanConnect() => true;
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public void Create() { }
    public Task CreateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void CreateTables() { }
    public Task CreateTablesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Delete() { }
    public Task DeleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool Exists() => true;
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public bool EnsureDeleted() => true;
    public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public bool EnsureCreated() => true;
    public Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}