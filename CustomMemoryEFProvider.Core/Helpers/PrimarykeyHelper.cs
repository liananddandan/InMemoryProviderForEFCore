using System.Reflection;
using CustomMemoryEFProvider.Core.Exceptions;

namespace CustomMemoryEFProvider.Core.Helpers;

/// <summary>
/// Helper class for extracting and validating primary key values from entities
/// </summary>
public static class PrimaryKeyHelper
{
    /// <summary>
    /// Extracts primary key values from an entity (supports single/composite keys)
    /// </summary>
    /// <typeparam name="TEntity">Type of the entity</typeparam>
    /// <param name="entity">Entity to extract primary key from</param>
    /// <param name="entityType">Type metadata of the entity</param>
    /// <returns>Array of primary key values (length = 1 for single key, >1 for composite key)</returns>
    /// <exception cref="ArgumentNullException">Thrown if entity or entityType is null</exception>
    /// <exception cref="MemoryDatabaseException">Thrown if no primary key is defined for the entity</exception>
    public static object[] ExtractPrimaryKeyValues<TEntity>(TEntity entity, Type entityType) where TEntity : class
    {
        // Validate input parameters
        if (entity == null)
            throw new ArgumentNullException(nameof(entity), "Entity cannot be null");
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType), "Entity type cannot be null");

        // Get primary key properties (supports composite keys)
        var primaryKeyProperties = GetPrimaryKeyProperties(entityType);
        
        if (primaryKeyProperties == null || primaryKeyProperties.Length == 0)
            throw new MemoryDatabaseException($"No primary key defined for entity type: {entityType.Name}");

        // Extract value from each primary key property
        var keyValues = new object[primaryKeyProperties.Length];
        for (int i = 0; i < primaryKeyProperties.Length; i++)
        {
            keyValues[i] = primaryKeyProperties[i].GetValue(entity) 
                ?? throw new MemoryDatabaseException($"Primary key property {primaryKeyProperties[i].Name} cannot be null");
        }

        return keyValues;
    }

    /// <summary>
    /// Gets primary key properties for an entity type using reflection
    /// </summary>
    /// <param name="entityType">Entity type to inspect</param>
    /// <returns>Array of primary key PropertyInfo objects</returns>
    private static PropertyInfo[] GetPrimaryKeyProperties(Type entityType)
    {
        // Simplified logic: 
        // For demonstration (Core layer, no EF Core metadata), we assume:
        // 1. Primary key property is named "Id" (single key) OR
        // 2. Composite key properties are named "XXXId" (e.g., OrderId + ProductId)
        // Note: In Provider layer, we'll replace this with EF Core metadata for real primary key detection

        // Step 1: Check for single "Id" property (most common case)
        var idProperty = entityType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProperty != null)
            return new[] { idProperty };

        // Step 2: Check for composite key (properties ending with "Id")
        var idProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name) // Ensure consistent order for composite keys
            .ToArray();

        return idProperties.Length > 0 ? idProperties : Array.Empty<PropertyInfo>();
    }
}