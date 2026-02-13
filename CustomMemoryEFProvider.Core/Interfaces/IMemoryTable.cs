using System.Collections;
using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Implementations;

namespace CustomMemoryEFProvider.Core.Interfaces;

public interface IMemoryTable : IDisposable
{
    Type EntityType { get; }
    int SaveChanges();
    void Clear();

    object? Find(Object[] keyValues);

    IEnumerable GetAllEntities();

    SnapshotRow[] ExportCommittedRows();
    PendingChangeRow[] ExportPendingRows();

    void ImportCommittedRows(SnapshotRow[] rows);
    void ImportPendingRows(PendingChangeRow[] rows);
}
public readonly record struct PendingChangeRow(object[] Key, ScalarSnapshot? Snapshot, EntityState State);