using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CustomMemoryEFProvider.Infrastructure.Find;


public sealed class CustomMemoryEntityFinderSource : IEntityFinderSource
{
    private readonly IMemoryDatabase _db;

    public CustomMemoryEntityFinderSource(IMemoryDatabase db)
    {
        _db = db;
    }
    
    public IEntityFinder Create(
        IStateManager stateManager,
        IDbSetSource setSource,
        IDbSetCache setCache,
        IEntityType entityType)
    {
        Console.WriteLine($"[FINDER_SOURCE] Create for {entityType.ClrType.FullName}");

        var finderType = typeof(CustomMemoryEntityFinder<>).MakeGenericType(entityType.ClrType);
        return (IEntityFinder)Activator.CreateInstance(finderType, entityType, stateManager, _db)!;
    }
}