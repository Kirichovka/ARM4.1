using System;
using System.Linq;
using System.Reflection;
using ARM4.Logging.InfraErrorCodes;
using Xunit;

namespace ARM4.Tests.Logging
{
    public class InfraErrorCodesTests
    {
        [Theory]
        [InlineData(nameof(InfraErrorCodes.CacheError), "CacheError")]
        [InlineData(nameof(InfraErrorCodes.DatabaseError), "DatabaseError")]
        [InlineData(nameof(InfraErrorCodes.FileSystemError), "FileSystemError")]
        [InlineData(nameof(InfraErrorCodes.ConfigError), "ConfigError")]
        [InlineData(nameof(InfraErrorCodes.LoggerError), "LoggerError")]
        [InlineData(nameof(InfraErrorCodes.NetworkError), "NetworkError")]
        [InlineData(nameof(InfraErrorCodes.UnknownInfraError), "UnknownInfraError")]
        public void Constant_HasExpectedValue(string fieldName, string expectedValue)
        {
            // Arrange
            var type = typeof(InfraErrorCodes);
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);

            // Act
            var actual = field.GetValue(null) as string;

            // Assert
            Assert.Equal(expectedValue, actual);
        }

        [Fact]
        public void NoExtraConstants_AreDefined()
        {
            // Проверяем, что объявлено ровно 7 констант
            var type = typeof(InfraErrorCodes);
            var fields = type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .Select(f => f.Name)
                .ToList();

            Assert.Equal(7, fields.Count);
            Assert.Contains("CacheError", fields);
            Assert.Contains("DatabaseError", fields);
            Assert.Contains("FileSystemError", fields);
            Assert.Contains("ConfigError", fields);
            Assert.Contains("LoggerError", fields);
            Assert.Contains("NetworkError", fields);
            Assert.Contains("UnknownInfraError", fields);
        }
    }
}
