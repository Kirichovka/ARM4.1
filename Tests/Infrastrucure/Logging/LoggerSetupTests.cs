using System;
using System.IO;
using System.Linq;
using Serilog;
using Xunit;

namespace ARM4.Tests.Infrastructure
{
    public class LoggerSetupTests : IDisposable
    {
        private readonly string _originalDirectory;
        private readonly string _tempDir;

        public LoggerSetupTests()
        {
            // —охран€ем текущую рабочую директорию и переключаемс€ в изолированную
            _originalDirectory = Directory.GetCurrentDirectory();
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            Directory.SetCurrentDirectory(_tempDir);
        }

        [Fact(DisplayName = "CreateLogger создает папку logs и файл trace")]
        public void CreateLogger_CreatesLogsFolderAndTraceFile()
        {
            // Act
            var logger = LoggerSetup.CreateLogger();
            logger.Information("Trace test entry");
            Log.CloseAndFlush();

            // Assert Ч папка
            var logsDir = Path.Combine(_tempDir, "logs");
            Assert.True(Directory.Exists(logsDir), "ѕапка 'logs' не создана");

            // Assert Ч trace-файл
            var traceFile = Directory
                .EnumerateFiles(logsDir, "trace-*.log")
                .FirstOrDefault();
            Assert.NotNull(traceFile);
            var content = File.ReadAllText(traceFile!);
            Assert.Contains("Trace test entry", content);
        }

        [Fact(DisplayName = "CreateLogger пишет ошибки в error-файл")]
        public void CreateLogger_WritesErrorToErrorFile()
        {
            // Arrange
            var logger = LoggerSetup.CreateLogger();

            // Act
            logger.Error("Error test entry");
            Log.CloseAndFlush();

            // Assert Ч error-файл
            var logsDir = Path.Combine(_tempDir, "logs");
            var errorFile = Directory
                .EnumerateFiles(logsDir, "error-*.log")
                .FirstOrDefault();
            Assert.NotNull(errorFile);
            var content = File.ReadAllText(errorFile!);
            Assert.Contains("Error test entry", content);
        }

        [Fact(DisplayName = "CreateLogger создает JSON-лог")]
        public void CreateLogger_WritesJsonLogFile()
        {
            // Arrange
            var logger = LoggerSetup.CreateLogger();

            // Act
            logger.Warning("JSON log entry");
            Log.CloseAndFlush();

            // Assert Ч JSON-файл
            var logsDir = Path.Combine(_tempDir, "logs");
            var jsonFile = Directory
                .EnumerateFiles(logsDir, "log-*.json")
                .FirstOrDefault();
            Assert.NotNull(jsonFile);
            var content = File.ReadAllText(jsonFile!);
            Assert.Contains("\"JSON log entry\"", content);
        }

        [Fact(DisplayName = "Ќе падает при отсутствии appsettings.json или неверном JSON")]
        public void CreateLogger_HandlesMissingOrInvalidConfig()
        {
            // Arrange: создаЄм неверный appsettings.json
            File.WriteAllText("appsettings.json", "{ invalid json ");

            // Act & Assert: метод не выбрасывает
            var ex = Record.Exception(() => {
                var logger = LoggerSetup.CreateLogger();
                logger.Information("Test after bad config");
                Log.CloseAndFlush();
            });

            Assert.Null(ex);

            // » при этом папка logs создаЄтс€
            Assert.True(Directory.Exists("logs"));
        }

        public void Dispose()
        {
            // √арантируем сброс и очистку
            Log.CloseAndFlush();
            Directory.SetCurrentDirectory(_originalDirectory);
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
