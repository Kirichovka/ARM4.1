using System;
using System.Linq;
using System.Reflection;
using ARM4.Domain.DomainExceptions;
using Xunit;

namespace ARM4.Tests.Domain
{
    public class ProductDomainExceptionTests
    {
        [Fact]
        public void ProductErrorCode_Enum_HasExpectedMembersCount()
        {
            // Arrange & Act
            var names = Enum.GetNames(typeof(ProductErrorCode));

            // Assert: we expect exactly 21 members as declared
            Assert.Equal(21, names.Length);
        }

        [Fact]
        public void Constructor_Minimal_SetsDefaults()
        {
            // Act
            var ex = new ProductDomainException(
                ProductErrorCode.InvalidName,
                "Invalid name provided");

            // Assert
            Assert.Equal("Invalid name provided", ex.Message);
            Assert.Equal(ProductErrorCode.InvalidName, ex.Code);
            Assert.Equal("InvalidName", ex.ErrorCode);
            Assert.Null(ex.Field);
            Assert.Null(ex.Value);
            Assert.Null(ex.InnerException);
        }

        [Fact]
        public void Constructor_AllParameters_SetsAllProperties()
        {
            // Arrange
            var inner = new ArgumentNullException("arg");
            var code = ProductErrorCode.InvalidSalePrice;
            var message = "Sale price must be positive";
            var field = "SalePrice";
            var value = (object)(-5);

            // Act
            var ex = new ProductDomainException(code, message, field, value, inner);

            // Assert
            Assert.Equal(message, ex.Message);
            Assert.Equal(code, ex.Code);
            Assert.Equal(code.ToString(), ex.ErrorCode);
            Assert.Equal(field, ex.Field);
            Assert.Equal(value, ex.Value);
            Assert.Same(inner, ex.InnerException);
        }

        [Fact]
        public void ToLogObject_ReturnsAnonymousObject_WithExpectedProperties()
        {
            // Arrange
            var ex = new ProductDomainException(
                ProductErrorCode.NotEnoughStock,
                "Not enough in stock",
                "Quantity",
                0);

            // Act
            var logObj = ex.ToLogObject();
            var type = logObj.GetType();

            // Assert
            Assert.Equal("NotEnoughStock", (string)type.GetProperty("ErrorCode")!.GetValue(logObj)!);
            Assert.Equal("Quantity", (string)type.GetProperty("Field")!.GetValue(logObj)!);
            Assert.Equal(0, type.GetProperty("Value")!.GetValue(logObj)!);
            Assert.Equal("Not enough in stock", (string)type.GetProperty("Message")!.GetValue(logObj)!);
            Assert.Equal(nameof(ProductDomainException), (string)type.GetProperty("ExceptionType")!.GetValue(logObj)!);
        }

        [Fact]
        public void ToString_WithoutField_FormatsAsBase()
        {
            // Arrange
            var ex = new ProductDomainException(
                ProductErrorCode.ValidationFailed,
                "Validation error occurred");

            // Act
            var str = ex.ToString();

            // Assert
            Assert.Equal("[ValidationFailed] Validation error occurred", str);
        }

        [Fact]
        public void ToString_WithFieldAndValue_IncludesThem()
        {
            // Arrange
            var ex = new ProductDomainException(
                ProductErrorCode.InvalidBarcode,
                "Barcode format invalid",
                "Barcode",
                "ABC123");

            // Act
            var str = ex.ToString();

            // Assert
            Assert.Equal(
                "[InvalidBarcode] Barcode format invalid (Field: Barcode, Value: ABC123)",
                str);
        }
    }
}
