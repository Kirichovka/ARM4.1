using System;
using System.Reflection;
using Xunit;

namespace ARM4.Tests.Common
{
    public class DomainExceptionTests
    {
        [Fact]
        public void Constructor_WithMessage_SetsDefaults()
        {
            // Act
            var ex = new DomainException("Test message");

            // Assert
            Assert.Equal("Test message", ex.Message);
            Assert.Equal("Unknown", ex.ErrorCode);
            Assert.Null(ex.Field);
            Assert.Null(ex.Value);
            Assert.Null(ex.InnerException);
        }

        [Fact]
        public void Constructor_WithAllParameters_SetsAllProperties()
        {
            // Arrange
            var inner = new InvalidOperationException("Inner");
            var message = "Something went wrong";
            var errorCode = "MyCode";
            var field = "MyField";
            var value = (object)1234;

            // Act
            var ex = new DomainException(message, errorCode, field, value, inner);

            // Assert
            Assert.Equal(message, ex.Message);
            Assert.Equal(errorCode, ex.ErrorCode);
            Assert.Equal(field, ex.Field);
            Assert.Equal(value, ex.Value);
            Assert.Same(inner, ex.InnerException);
        }

        [Fact]
        public void ToLogObject_ContainsAllExpectedProperties()
        {
            // Arrange
            var inner = new Exception("InnerEx");
            var ex = new DomainException("Msg", "CodeX", "F", "V", inner);

            // Act
            var logObj = ex.ToLogObject();
            var type = logObj.GetType();

            // Assert
            Assert.Equal("CodeX", (string)type.GetProperty("ErrorCode")!.GetValue(logObj)!);
            Assert.Equal("F", (string)type.GetProperty("Field")!.GetValue(logObj)!);
            Assert.Equal("V", (string)type.GetProperty("Value")!.GetValue(logObj)!);
            Assert.Equal("Msg", (string)type.GetProperty("Message")!.GetValue(logObj)!);
            Assert.Equal("DomainException", (string)type.GetProperty("ExceptionType")!.GetValue(logObj)!);
        }

        [Fact]
        public void ToString_WithoutField_FormatsCorrectly()
        {
            // Arrange
            var ex = new DomainException("Hello world", "E1");

            // Act
            var str = ex.ToString();

            // Assert
            Assert.Equal("[E1] Hello world", str);
        }

        [Fact]
        public void ToString_WithFieldAndValue_FormatsCorrectly()
        {
            // Arrange
            var ex = new DomainException("Oops", "ERR42", "FieldName", 99);

            // Act
            var str = ex.ToString();

            // Assert
            Assert.Equal("[ERR42] Oops (Field: FieldName, Value: 99)", str);
        }
    }
}
