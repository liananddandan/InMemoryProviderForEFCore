using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace CustomMemoryEFProvider.Infrastructure.Storage;

public class CustomMemoryEfDatabase : Database
{
    private readonly IMemoryDatabase _memoryDatabase;

    public CustomMemoryEfDatabase(
        DatabaseDependencies dependencies,
        IMemoryDatabase memoryDatabase)
        : base(dependencies)
    {
        _memoryDatabase = memoryDatabase;
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
        var affected = 0;
        
        foreach (var entry in entries)
        {
            // 先只做 Added，跑通闭环
            if (entry.EntityState is not
                (Microsoft.EntityFrameworkCore.EntityState.Added
                or Microsoft.EntityFrameworkCore.EntityState.Modified
                or Microsoft.EntityFrameworkCore.EntityState.Deleted))
                continue;

            var entityEntry = entry.ToEntityEntry();
            var entity  = entityEntry.Entity;
            var runtimeType = entity.GetType();

            switch (entry.EntityState)
            {
                case Microsoft.EntityFrameworkCore.EntityState.Added:
                    InvokeTableMethod(runtimeType, "Add", entity);
                    affected++;
                    break;
                case Microsoft.EntityFrameworkCore.EntityState.Modified:
                    InvokeTableMethod(runtimeType, "Update", entity);
                    affected++;
                    break;
                case Microsoft.EntityFrameworkCore.EntityState.Deleted:
                    InvokeTableMethod(runtimeType, "Remove", entity);
                    affected++;
                    break;
            }
        }

        _memoryDatabase.SaveChanges();
        return affected;
    }

    public override Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new CancellationToken())
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SaveChanges((entries)));
    }

    private void InvokeTableMethod(Type entityRuntimeType, string methodName, object entity)
    {
        var table = _memoryDatabase.GetTable(entityRuntimeType);
        
        var method = table.GetType().GetMethods()
            .FirstOrDefault(m =>
                m.Name == methodName &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(entityRuntimeType));
        
        if (method == null)
            throw new MissingMethodException(table.GetType().FullName, methodName);
        
        method.Invoke(table, new[] { entity });
    }
}