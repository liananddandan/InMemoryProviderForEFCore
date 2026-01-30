using System.Collections;

namespace CustomMemoryEFProvider.Core.Interfaces;

public interface IMemoryTable : IDisposable
{
    Type EntityType { get; }
    int SaveChanges();
    void Clear();

    object? Find(Object[] keyValues);

    IEnumerable GetAllEntities();
}