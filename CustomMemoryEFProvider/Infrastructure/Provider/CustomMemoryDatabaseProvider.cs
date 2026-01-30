using CustomMemoryEFProvider.Infrastructure.Interfaces;
using CustomMemoryEFProvider.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CustomMemoryEFProvider.Infrastructure.Provider;

/// <summary>
/// Concrete implementation of the provider marker interface
/// Will be extended with real database operations later
/// </summary>
public class CustomMemoryDatabaseProvider : DatabaseProvider<CustomMemoryDbContextOptionsExtension>, ICustomMemoryDatabaseProvider
{
    // ========== EF Core IDatabaseProvider (Your Version) Implementation ==========
    public CustomMemoryDatabaseProvider(DatabaseProviderDependencies dependencies) : base(dependencies)
    {
        
    }

    /// <summary>
    /// Unique provider name (must match OptionsExtensionInfo.ProviderName)
    /// This is required for EF Core to identify your provider
    /// </summary>
    public string Name => "CustomMemoryEFProvider.Infrastructure.Provider.CustomMemoryDatabaseProvider";
    
    /// <summary>
    /// EF Core calls this to check if your provider is configured correctly
    /// Return true if your custom options are present (fixes configuration validation)
    /// </summary>
    public bool IsConfigured(IDbContextOptions options)
    {
        // Check if your CustomMemoryDbContextOptionsExtension is registered
        return options.FindExtension<CustomMemoryDbContextOptionsExtension>() != null;
    }
}