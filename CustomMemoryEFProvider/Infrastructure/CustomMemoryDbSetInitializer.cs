using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace CustomMemoryEFProvider.Infrastructure;

/// <summary>
/// Minimal implementation of IDbSetInitializer required by EF Core.
/// 
/// WHY THIS CLASS IS MANDATORY:
/// As part of EF Core's mandatory service checklist for database providers (IsDatabaseProvider = true),
/// IDbSetInitializer is required for EF Core's internal DbSet initialization process. Even though your 
/// custom memory provider does not use DbSet functionality, EF Core will throw a "No service for type 
/// 'IDbSetInitializer' has been registered" exception if this service is missing.
/// 
/// This is another "null implementation" - it only satisfies EF Core's validation rules and contains 
/// no functional logic for your custom provider.
/// </summary>
public class CustomMemoryDbSetInitializer : IDbSetInitializer
{
    /// <summary>
    /// Empty implementation to comply with EF Core's interface contract.
    /// No DbSet initialization logic is needed for a custom memory provider.
    /// </summary>
    /// <param name="context">EF Core DbContext instance (unused)</param>
    /// <param name="model">EF Core metadata model (unused)</param>
    public void InitializeSets(DbContext context)
    {
        
    }
}