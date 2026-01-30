using CustomMemoryEFProvider.Extensions;
using CustomMemoryEFProvider.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomMemoryEFProvider.Internal;

/// <summary>
/// Implements IDbContextOptionsExtension to provide configuration for custom in-memory database provider
/// Core responsibilities: 
/// 1. Encapsulate database configuration (name, clear-on-create flag)
/// 2. Register provider services to EF Core's DI container
/// 3. Provide configuration metadata for EF Core recognition
/// </summary>
public class CustomMemoryDbContextOptionsExtension : IDbContextOptionsExtension
{
    // Core configuration: Unique identifier for in-memory database instance
    private readonly string _databaseName;
    
    // Optional configuration: Clear existing data when database is initialized
    private readonly bool _clearOnCreate;
    
    // Cached hash code to avoid redundant calculations
    private int? _hashCode;
    
    // Lazy-initialized extension info (EF Core required metadata)
    private DbContextOptionsExtensionInfo _info;
    
    /// <summary>
    /// Initializes a new instance of the YourMemoryDbContextOptionsExtension class
    /// </summary>
    /// <param name="databaseName">Unique name for the in-memory database (cannot be null/empty)</param>
    /// <param name="clearOnCreate">Whether to clear existing data on initialization (default: false)</param>
    /// <exception cref="ArgumentNullException">Thrown when databaseName is null</exception>
    public CustomMemoryDbContextOptionsExtension(string databaseName, bool clearOnCreate = false)
    {
        _databaseName = databaseName ?? throw new ArgumentNullException(
            nameof(databaseName), "Database name for in-memory provider cannot be null.");
        _clearOnCreate = clearOnCreate;
    }

    /// <summary>
    /// Registers custom in-memory database services to EF Core's service collection
    /// This is the core entry point for EF Core to discover provider services
    /// </summary>
    /// <param name="services">EF Core's internal service collection</param>
    public void ApplyServices(IServiceCollection services)
    {
        // Register configuration as singleton (available to all provider services)
        services.AddSingleton(new CustomMemoryDbConfig(_databaseName, _clearOnCreate));
        
        services.AddEntityFrameworkCustomMemoryDatabase();
    }

    /// <summary>
    /// Validates the configuration for the in-memory database
    /// Ensures required configuration values are valid before context initialization
    /// </summary>
    /// <param name="options">DbContextOptions containing this extension</param>
    /// <exception cref="InvalidOperationException">Thrown when databaseName is whitespace</exception>
    public void Validate(IDbContextOptions options)
    {
        if (string.IsNullOrWhiteSpace(_databaseName))
        {
            throw new InvalidOperationException("Database name for in-memory provider cannot be null.");
        }
    }

    /// <summary>
    /// Gets metadata about this extension for EF Core's internal processing
    /// Lazy-initialized to avoid unnecessary object creation
    /// </summary>
    public DbContextOptionsExtensionInfo Info
    {
        get
        {
            if (_info == null)
            {
                _info = new CustomMemoryDbContextOptionsExtensionInfo(this);
            }
            return _info;
        }
    }
    
    /// <summary>
    /// Gets the unique name of the in-memory database instance
    /// </summary>
    public string DatabaseName => _databaseName;
    
    /// <summary>
    /// Gets whether to clear existing data when the database is initialized
    /// </summary>
    public bool ClearOnCreate => _clearOnCreate;
    
    /// <summary>
    /// Computes hash code based on configuration values (EF Core uses this for configuration comparison)
    /// </summary>
    /// <returns>Hash code for the configuration</returns>
    public override int GetHashCode()
    {
        if (_hashCode == null)
        {
            _hashCode = HashCode.Combine(
                typeof(CustomMemoryDbContextOptionsExtension),
                _databaseName,
                _clearOnCreate);
        }
        return _hashCode.Value;
    }
    
    /// <summary>
    /// Determines whether two configuration instances are equal (EF Core uses this for configuration comparison)
    /// </summary>
    /// <param name="obj">Object to compare with current instance</param>
    /// <returns>True if configurations are equal, false otherwise</returns>
    public override bool Equals(object? obj)
    {
        return obj is CustomMemoryDbContextOptionsExtension other
               && _databaseName == other._databaseName
               && _clearOnCreate == other._clearOnCreate;
    }
}