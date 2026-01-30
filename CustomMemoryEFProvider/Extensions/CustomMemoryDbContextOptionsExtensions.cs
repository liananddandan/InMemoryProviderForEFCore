using CustomMemoryEFProvider.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CustomMemoryEFProvider.Extensions;

/// <summary>
/// Static class containing extension methods for configuring the custom in-memory provider
/// Follows EF Core convention (Use[ProviderName] pattern) for user familiarity
/// </summary>
public static class CustomMemoryDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the DbContext to use the custom in-memory database provider
    /// </summary>
    /// <param name="builder">DbContextOptionsBuilder to configure</param>
    /// <param name="databaseName">Unique name for the in-memory database instance</param>
    /// <param name="clearOnCreate">Whether to clear existing data on initialization</param>
    /// <returns>The same builder instance for method chaining</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder or databaseName is null</exception>
    public static DbContextOptionsBuilder UseCustomMemoryDb(
        this DbContextOptionsBuilder builder,
        string databaseName,
        bool clearOnCreate = false)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder), "DbContextOptionsBuilder cannot be null.");
        }
        
        // Create extension instance with user-provided configuration
        var extension = builder.Options.FindExtension<CustomMemoryDbContextOptionsExtension>() 
                        ?? new CustomMemoryDbContextOptionsExtension(databaseName, clearOnCreate);        
        // Add or update the extension in EF Core's options (EF Core internal API)
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        return builder;
    }

    /// <summary>
    /// Generic overload for typed DbContextOptionsBuilder (improves IntelliSense experience)
    /// </summary>
    /// <typeparam name="TContext">Type of DbContext to configure</typeparam>
    /// <param name="builder">Typed DbContextOptionsBuilder</param>
    /// <param name="databaseName">Unique database name</param>
    /// <param name="clearOnCreate">Clear data on initialization flag</param>
    /// <returns>Typed builder for method chaining</returns>
    public static DbContextOptionsBuilder<TContext> UseCustomMemoryDb<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string databaseName,
        bool clearOnCreate = false)
        where TContext : DbContext
    {
        // Delegate to non-generic overload (avoids code duplication)
        UseCustomMemoryDb((DbContextOptionsBuilder)builder, databaseName, clearOnCreate);
        return builder;
    }
}