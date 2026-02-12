using CustomEFCoreProvider.Samples;
using CustomEFCoreProvider.Samples.Infrastructure;
using CustomMemoryEFProvider.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


Console.WriteLine("Hello, World!");

try
{
    var services = new ServiceCollection();

    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

    services.AddDbContext<AppDbContext>(options =>
        options.UseCustomMemoryDb("TestDatabase"));

    using var sp = services.BuildServiceProvider();

    var tester = new EfProviderSmokeTest(new EfProviderSmokeTestOptions
     {
         CheckModel = true,
         CheckInternalServices = true,
         CheckValueGenerator = true,
    
         CrudSingle = true,
         CrudMultiple = true,
         CrudDetached = true
     });
    
    tester.Run();
    EfProviderFinderSmokeTest.Run();
    EfProviderImmediateExecutionSmokeTest.Run();
    EfProviderWhereSelectSmokeTest.Run();
    EfProviderOrderBySmokeTest.Run();
    EfProviderSkipTakeSmokeTest.Run();
    EfProviderIncludeReferenceSmokeTest.Run();
    EfProviderIncludeCollectionSmokeTest.Run();
    EfProviderIncludeNestedSmokeTest.Run();
    IdentityResolutionProblemSmokeTest.Run();
    QueryRowsSmokeTest.Run();
}
catch (Exception e)
{
    Console.WriteLine($"❌ Validation Failed: {e.Message}");
    Console.WriteLine($"❌ Stack Trace: {e.StackTrace}");
}