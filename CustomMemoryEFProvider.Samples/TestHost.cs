using CustomEFCoreProvider.Samples.Infrastructure;
using CustomMemoryEFProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class TestHost
{
    public static ServiceProvider BuildRootProvider(string dbName, bool clearOnCreate = false)
    {
        var services = new ServiceCollection();

        // 你 Samples 里如果还用到了 Console logger / diagnostics 可以继续加
        // services.AddLogging();

        services.AddDbContext<AppDbContext>(options =>
        {
            // 关键：把 dbName 透传给你的 provider options extension
            options.UseCustomMemoryDb(dbName, clearOnCreate);
        });

        return services.BuildServiceProvider(validateScopes: true);
    }
}