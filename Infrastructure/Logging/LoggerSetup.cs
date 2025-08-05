using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Enrichers.Sensitive;
using Serilog.Enrichers;
using Serilog.Core;

public static class LoggerSetup
{
    public static ILogger CreateLogger()
    {
        var logDirectory = "logs";
        try
        {
            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create log directory '{logDirectory}': {ex.Message}");
        }

        try
        {
            var selfLogPath = Path.Combine(logDirectory, "serilog-selflog.txt");
            Serilog.Debugging.SelfLog.Enable(msg => File.AppendAllText(selfLogPath, msg + Environment.NewLine));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to enable Serilog SelfLog: {ex.Message}");
        }
        // Загружаем конфигурацию, если нужно
        IConfiguration? configuration = null;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load appsettings.json: {ex.Message}");
        }

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Verbose() // Позволяет использовать trace-уровень
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithEnvironmentUserName()
            // Маскировка чувствительных данных (работает только на стандартные поля!)
            .Enrich.WithSensitiveDataMasking(options => { })
            .WriteTo.Console();

        if (configuration != null)
            loggerConfig.ReadFrom.Configuration(configuration);

        // Trace/Verbose — все логи (максимум подробностей)
        loggerConfig.WriteTo.File(
            path: Path.Combine(logDirectory, "trace-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 3, // Храним 3 дня
            restrictedToMinimumLevel: LogEventLevel.Verbose,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}");

        // Error — только ошибки (critical + error)
        loggerConfig.WriteTo.File(
            path: Path.Combine(logDirectory, "error-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 3, // Храним 3 дня
            restrictedToMinimumLevel: LogEventLevel.Error,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}");

        // Audit — отдельный файл только для логов с property "IsAudit"
        loggerConfig.WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(le => le.Properties.ContainsKey("IsAudit"))
            .WriteTo.File(
                path: Path.Combine(logDirectory, "audit-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 3,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} AUDIT] {Message:lj} {Properties}{NewLine}{Exception}"
            )
        );

        // JSON лог для интеграций и парсинга
        loggerConfig.WriteTo.File(
            new JsonFormatter(),
            path: Path.Combine(logDirectory, "log-.json"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 3
        );

        try
        {
            return loggerConfig.CreateLogger();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create logger: {ex.Message}");
            // Возвращаем базовый логгер в консоль
            return new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();
        }
    }
}
