using CustomMemoryEFProvider.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CustomMemoryEFProvider.Infrastructure;

/// <summary>
/// Minimal implementation of ISingletonOptionsInitializer required by EF Core.
/// 
/// WHY THIS CLASS IS MANDATORY:
/// EF Core enforces a mandatory service registration checklist for any component marked as a "Database Provider" 
/// (via IsDatabaseProvider = true in CustomMemoryOptionsExtensionInfo). 
/// ISingletonOptionsInitializer is the first required service in this checklist - EF Core will throw a 
/// "No service for type 'ISingletonOptionsInitializer' has been registered" exception if this service is missing, 
/// regardless of whether your custom provider actually uses its functionality.
/// 
/// This class is a "null implementation" (no business logic) - it only exists to satisfy EF Core's 
/// provider validation rules, not to add any functional behavior to your custom memory provider.
/// </summary>
public class CustomMemorySingletonOptionsInitializer : ISingletonOptionsInitializer
{
    /// <summary>
    /// Empty implementation to satisfy EF Core's interface contract.
    /// No logic is needed here for basic custom provider functionality.
    /// </summary>
    /// <param name="serviceProvider">EF Core's internal service provider (unused)</param>
    /// <param name="options">DbContext configuration options (unused)</param>
    public void EnsureInitialized(IServiceProvider serviceProvider, IDbContextOptions options)
    {
        Console.WriteLine("✅ CustomMemorySingletonOptionsInitializer.EnsureInitialized is invoked.！");

        // Step 1: Validate the custom memory provider extension is registered in DbContext options
        var memoryExtension = options.FindExtension<CustomMemoryDbContextOptionsExtension>();
        if (memoryExtension == null)
        {
            throw new InvalidOperationException(
                "CustomMemoryOptionsExtension is not registered in DbContext options. " +
                "Ensure you call UseCustomMemoryDb() when configuring DbContext.");
        }

        // Step 2: Validate CustomMemoryDbConfig is registered in the service provider
        var dbConfig = serviceProvider.GetService<CustomMemoryDbConfig>();
        if (dbConfig == null)
        {
            throw new InvalidOperationException(
                "CustomMemoryDbConfig is not registered in the service collection. " +
                "This is required for the custom memory provider to function.");
        }

        Console.WriteLine($"✅ get config from dbConfig：DatabaseName = {dbConfig.DatabaseName}");

        // Step 3: Validate mandatory configuration values (e.g., non-empty database name)
        if (string.IsNullOrWhiteSpace(dbConfig.DatabaseName))
        {
            throw new InvalidOperationException(
                "DatabaseName in CustomMemoryDbConfig cannot be null or empty. " +
                "Provide a valid database name when calling UseCustomMemoryDb().");
        }
    }
}