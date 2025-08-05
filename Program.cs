// Program.cs
using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ARM4.Infrastructure.Data;
using ARM4.Domain.Interfaces;
using ARM4.Infrastructure.Repositories;
using ARM4.Domain.Common;

public class Program
{
    public static void Main(string[] args)
    {
        // 1. Собираем конфигурацию
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // 2. Инициализируем Serilog
        Log.Logger = LoggerSetup.CreateLogger();

        // 3. Создаём хост
        var host = Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureAppConfiguration((ctx, cfg) => cfg.AddConfiguration(configuration))
            .ConfigureServices((ctx, services) =>
            {
                // Конфигурация
                services.AddSingleton<IConfiguration>(configuration);

                // Логирование
                services.AddLogging();

                // DbContext с SQL Server
                services.AddDbContext<ARM4DbContext>(opts =>
                    opts.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

                // Кэш
                services.AddMemoryCache();

                // Провайдер времени
                services.AddSingleton<ITimeProvider, SystemTimeProvider>();

                // Репозиторий
                services.AddScoped<IProductRepository, ProductRepository>();

                // TODO: добавить другие сервисы
            })
            .Build();

        // 4. Автоматическое применение миграций при старте
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ARM4DbContext>();
            db.Database.Migrate();
        }

        // 5. Запуск приложения
        host.Run();
    }
}
