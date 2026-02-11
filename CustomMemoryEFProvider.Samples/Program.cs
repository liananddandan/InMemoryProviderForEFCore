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

    // var tester = new EfProviderSmokeTest(new EfProviderSmokeTestOptions
    //  {
    //      CheckModel = true,
    //      CheckInternalServices = true,
    //      CheckValueGenerator = true,
    //
    //      CrudSingle = true,
    //      CrudMultiple = true,
    //      CrudDetached = true
    //  });
    
    // tester.Run(sp);
    // EfProviderFinderSmokeTest.Run(sp);
    // EfProviderImmediateExecutionSmokeTest.Run(sp);
    // EfProviderWhereSelectSmokeTest.Run(sp);
    // EfProviderOrderBySmokeTest.Run(sp);
    // EfProviderSkipTakeSmokeTest.Run(sp);
    // EfProviderIncludeReferenceSmokeTest.Run(sp);
    EfProviderIncludeCollectionSmokeTest.Run();
    // EfProviderIncludeNestedSmokeTest.Run(sp);
    // IdentityResolutionProblemSmokeTest.Run(sp);
    // QueryRowsSmokeTest.Run(sp);
}
catch (Exception e)
{
    Console.WriteLine($"❌ Validation Failed: {e.Message}");
    Console.WriteLine($"❌ Stack Trace: {e.StackTrace}");
}