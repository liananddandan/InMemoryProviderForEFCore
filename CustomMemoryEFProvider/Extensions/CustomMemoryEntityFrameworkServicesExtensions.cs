using CustomMemoryEFProvider.Core.Implementations;
using CustomMemoryEFProvider.Core.Interfaces;
using CustomMemoryEFProvider.Infrastructure;
using CustomMemoryEFProvider.Infrastructure.Find;
using CustomMemoryEFProvider.Infrastructure.Interfaces;
using CustomMemoryEFProvider.Infrastructure.Provider;
using CustomMemoryEFProvider.Infrastructure.Query;
using CustomMemoryEFProvider.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomMemoryEFProvider.Extensions;

public static class CustomMemoryEntityFrameworkServicesExtensions
{
    public static IServiceCollection AddEntityFrameworkCustomMemoryDatabase(
        this IServiceCollection serviceCollection)
    {
        // 校验入参（对齐官方 Provider 写法）
        ArgumentNullException.ThrowIfNull(serviceCollection, nameof(serviceCollection));

        // ========== 第一步：注册 EF Core 框架级核心服务 ==========
        var builder = new EntityFrameworkServicesBuilder(serviceCollection);
        
        // 2) 这些是 EF “框架服务”，不要放 TryAddProviderSpecificServices 里
        builder.TryAdd<ITypeMappingSource, CustomMemoryTypeMappingSource>();
        builder.TryAdd<LoggingDefinitions, CustomMemoryLoggingDefinitions>();
        builder.TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory,
            CustomMemoryQueryableMethodTranslatingExpressionVisitorFactory>();
        builder.TryAdd<IShapedQueryCompilingExpressionVisitorFactory,
            CustomMemoryShapedQueryCompilingExpressionVisitorFactory>();
        builder.TryAdd<IValueGeneratorSelector, CustomMemoryValueGeneratorSelector>();
        builder.TryAdd<IDatabaseProvider, CustomMemoryDatabaseProvider>();
        builder.TryAdd<IDatabase, CustomMemoryEfDatabase>();                 // 你必须提供
        builder.TryAdd<IQueryContextFactory, CustomMemoryQueryContextFactory>();
        builder.TryAddCoreServices();
        serviceCollection.Replace(
            ServiceDescriptor.Scoped<IEntityFinderSource, CustomMemoryEntityFinderSource>()
        );
        // 3) 你自己的 provider 扩展服务，放 provider-specific 没问题
        builder.TryAddProviderSpecificServices(p =>
        {
            p.TryAddScoped<IEntityFinderFactory, CustomMemoryEntityFinderFactory>();
            p.TryAddScoped<ICustomMemoryDatabaseProvider, CustomMemoryDatabaseProvider>();
        });

        // 4) 你自己的业务服务
        serviceCollection.TryAddSingleton(new MemoryDatabaseRoot());
        serviceCollection.TryAddScoped<IMemoryDatabase>(sp =>
        {
            var cfg = sp.GetRequiredService<CustomMemoryDbConfig>();
            var root = sp.GetRequiredService<MemoryDatabaseRoot>();
            var db = root.GetOrAdd(cfg.DatabaseName);
            Console.WriteLine($"[IMemoryDatabase] scope={sp.GetHashCode()} root={root.GetHashCode()} db={db.GetHashCode()} name={cfg.DatabaseName}");

            if (cfg.ClearOnCreate)
            {
                db.ClearAllTables();
            }
            
            return db;
        });
        serviceCollection.TryAddScoped(typeof(IMemoryTable<>), typeof(MemoryTable<>));
        return serviceCollection;      
    }
}