namespace CustomMemoryEFProvider.Infrastructure.Interfaces;

/// <summary>
/// Marker interface for the custom in-memory database provider
/// Used for service registration and provider identification
/// </summary>
public interface ICustomMemoryDatabaseProvider
{
    /// <summary>
    /// Gets the display name of the custom provider
    /// </summary>
    string ProviderName => "CustomMemoryProvider";
}