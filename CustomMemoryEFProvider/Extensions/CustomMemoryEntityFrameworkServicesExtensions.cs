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
        // Validate input (align with official provider patterns)
        ArgumentNullException.ThrowIfNull(serviceCollection, nameof(serviceCollection));
        // NEW: register SnapshotValueBufferFactory for compiling visitor factory  
        serviceCollection.TryAddSingleton<SnapshotValueBufferFactory>();

        // Step 1: register EF Core framework-facing services (core pipeline slots)
        var builder = new EntityFrameworkServicesBuilder(serviceCollection);

        // These are EF Core "framework services" that must be present for a database provider
        builder.TryAdd<ITypeMappingSource, CustomMemoryTypeMappingSource>();
        builder.TryAdd<LoggingDefinitions, CustomMemoryLoggingDefinitions>();
        builder.TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory,
            CustomMemoryQueryableMethodTranslatingExpressionVisitorFactory>();
        builder.TryAdd<IShapedQueryCompilingExpressionVisitorFactory,
            CustomMemoryShapedQueryCompilingExpressionVisitorFactory>();
        builder.TryAdd<IValueGeneratorSelector, CustomMemoryValueGeneratorSelector>();
        builder.TryAdd<IDatabaseProvider, CustomMemoryDatabaseProvider>();
        // Required: provider must supply an IDatabase implementation 
        builder.TryAdd<IDatabase, CustomMemoryEfDatabase>();
        builder.TryAdd<IQueryContextFactory, CustomMemoryQueryContextFactory>();

        // Register EF Core core services (fills remaining defaults) 
        builder.TryAddCoreServices();

        // Override specific EF services when default behavior is not compatible with this provider
        serviceCollection.Replace(
            ServiceDescriptor.Scoped<IEntityFinderSource, CustomMemoryEntityFinderSource>()
        );

        // Step 2: provider-specific extension services (provider-only abstractions)
        builder.TryAddProviderSpecificServices(p =>
        {
            p.TryAddScoped<IEntityFinderFactory, CustomMemoryEntityFinderFactory>();
            p.TryAddScoped<ICustomMemoryDatabaseProvider, CustomMemoryDatabaseProvider>();
        });

        // Step 3: provider storage services (actual in-memory database implementation)
        serviceCollection.TryAddSingleton(new MemoryDatabaseRoot());
        serviceCollection.TryAddScoped<IMemoryDatabase>(sp =>
        {
            var cfg = sp.GetRequiredService<CustomMemoryDbConfig>();
            var root = sp.GetRequiredService<MemoryDatabaseRoot>();
            var db = root.GetOrAdd(cfg.DatabaseName);

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