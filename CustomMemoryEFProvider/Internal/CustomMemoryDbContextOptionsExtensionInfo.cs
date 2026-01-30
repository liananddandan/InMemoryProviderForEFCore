using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CustomMemoryEFProvider.Internal;

/// <summary>
/// Provides metadata about the in-memory provider extension for EF Core
/// EF Core uses this for logging, service provider reuse, and debugging
/// </summary>
public class CustomMemoryDbContextOptionsExtensionInfo : DbContextOptionsExtensionInfo
{
    private readonly CustomMemoryDbContextOptionsExtension _extension;
    private int? _hashCode;

    /// <summary>
    /// Gets a human-readable string for logging (appears in EF Core logs)
    /// </summary>
    public override string LogFragment =>
        $"Using custom in-memory database: {_extension.DatabaseName} (Clear on create: {_extension.ClearOnCreate})";
    
    /// <summary>
    /// Initializes a new instance of the YourMemoryDbContextOptionsExtensionInfo class
    /// </summary>
    /// <param name="extension">The extension to provide metadata for</param>
    public CustomMemoryDbContextOptionsExtensionInfo(CustomMemoryDbContextOptionsExtension extension)
        : base(extension)
    {
        _extension = extension;
    }

    /// <summary>
    /// Generates a hash code for the service provider configuration
    /// EF Core uses this to determine if the service provider can be reused
    /// Must match the hash logic of the extension configuration
    /// </summary>
    /// <returns>Hash code for service provider configuration</returns>
    public override int GetServiceProviderHashCode()
    {
        // Reuse the existing hash code logic (consistent with extension equality)
        return _extension.GetHashCode();
    }

    /// <summary>
    /// Determines whether the same service provider can be reused for different options
    /// Returns true if database name and clear flag are identical
    /// </summary>
    /// <param name="other">Other extension info to compare</param>
    /// <returns>True if service provider can be reused, false otherwise</returns>
    public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
    {
        return other is CustomMemoryDbContextOptionsExtensionInfo info
            && info._extension.DatabaseName == _extension.DatabaseName
            && info._extension.ClearOnCreate == _extension.ClearOnCreate;
    }
    
    /// <summary>
    /// Computes hash code (delegates to the extension's hash code)
    /// </summary>
    /// <returns>Hash code for the metadata</returns>
    public override int GetHashCode()
    {
        if (_hashCode == null)
        {
            _hashCode = _extension.GetHashCode();
        }
        return _hashCode.Value;
    }

    /// <summary>
    /// Populates debug information for EF Core's diagnostic tools
    /// Makes configuration visible in debuggers and logging
    /// </summary>
    /// <param name="debugInfo">Dictionary to populate with debug data</param>
    public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
    {
        debugInfo["YourMemoryProvider:DatabaseName"] = _extension.DatabaseName;
        debugInfo["YourMemoryProvider:ClearOnCreate"] = _extension.ClearOnCreate.ToString();
    }

    /// <summary>
    /// Indicates this extension represents a database provider (required by EF Core)
    /// </summary>
    public override bool IsDatabaseProvider => true;
    
    /// <summary>
    /// Gets the unique provider name (used for debugging and logging)
    /// </summary>
    public string ProviderName => "CustomMemoryEFProvider.Infrastructure.Provider.CustomMemoryDatabaseProvider";
}