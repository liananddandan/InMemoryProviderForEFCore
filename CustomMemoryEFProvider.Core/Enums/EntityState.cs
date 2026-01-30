namespace CustomMemoryEFProvider.Core.Enums;

/// <summary>
/// Represents the state of an entity in the in-memory table (for change tracking)
/// </summary>
public enum EntityState
{
    /// <summary>
    /// Entity is unchanged (no modifications)
    /// </summary>
    Unchanged,
    
    /// <summary>
    /// Entity is newly added and not yet saved
    /// </summary>
    Added,
    
    /// <summary>
    /// Entity is modified and not yet saved
    /// </summary>
    Modified,
    
    /// <summary>
    /// Entity is marked for deletion and not yet saved
    /// </summary>
    Deleted
}