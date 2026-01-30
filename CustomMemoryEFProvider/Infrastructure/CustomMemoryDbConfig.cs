namespace CustomMemoryEFProvider.Infrastructure;

/// <summary>
/// Data transfer object for in-memory database configuration
/// Makes configuration accessible to other provider services
/// </summary>
public class CustomMemoryDbConfig
{
    
    /// <summary>
    /// Gets the unique name of the in-memory database
    /// </summary>
    public string DatabaseName { get; }
    
    /// <summary>
    /// Gets whether to clear existing data on initialization
    /// </summary>
    public bool ClearOnCreate { get; }
    
    /// <summary>
    /// Initializes a new instance of the YourMemoryDbConfig class
    /// </summary>
    /// <param name="databaseName">Unique database name</param>
    /// <param name="clearOnCreate">Clear data on initialization flag</param>
    public CustomMemoryDbConfig(string databaseName, bool clearOnCreate)
    {
        DatabaseName = databaseName;
        ClearOnCreate = clearOnCreate;
    }
}