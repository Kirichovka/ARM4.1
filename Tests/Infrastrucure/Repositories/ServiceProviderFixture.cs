// Repositories/ProductRepository/ServiceProviderFixture.cs
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class ServiceProviderFixture
{
    public IServiceProvider ServiceProvider { get; }

    public ServiceProviderFixture()
    {
        var services = new ServiceCollection();

        // InMemory EF
        services.AddDbContext<ARM4DbContext>(opts =>
            opts.UseInMemoryDatabase("TestDb"));

        // MemoryCache
        services.AddMemoryCache();

        // Logger
        services.AddLogging();

        // Ваш репозиторий
        services.AddTransient<ProductRepository>();

        ServiceProvider = services.BuildServiceProvider();
    }
}
