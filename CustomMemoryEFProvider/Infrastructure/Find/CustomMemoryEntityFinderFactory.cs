using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;

namespace CustomMemoryEFProvider.Infrastructure.Find;

public sealed class CustomMemoryEntityFinderFactory : IEntityFinderFactory
{
    private readonly IStateManager _stateManager;
    private readonly IMemoryDatabase _db;

    public CustomMemoryEntityFinderFactory(IStateManager stateManager, IMemoryDatabase db)
    {
        _stateManager = stateManager;
        _db = db;
    }

    public IEntityFinder Create(IEntityType entityType)
    {
        Console.WriteLine($"[FINDER_SOURCE] Create for {entityType.ClrType.FullName}");

        var finderType = typeof(CustomMemoryEntityFinder<>).MakeGenericType(entityType.ClrType);
        return (IEntityFinder)Activator.CreateInstance(finderType, entityType, _stateManager, _db)!;
    }
}