using System;
using Xunit;
using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.ValueObjects;
using ARM4.Domain.Common;

namespace ARM4.Tests.Builders
{
    public class ProductBuilderTests
    {
        private readonly ITimeProvider _fixedTime = new TestTimeProvider(new DateTime(2025, 08, 02, 12, 0, 0));

        [Fact]
        public void Build_WithRequiredFields_DefaultsOptional()
        {
            // Arrange
            var name = "Name";
            var category = "Cat";
            decimal wholesale = 5m, sale = 10m;
            int qty = 20;
            var builder = new ProductBuilder(name, category, wholesale, sale, qty);

            // Act
            var product = builder.Build(_fixedTime);

            // Assert: required fields
            Assert.Equal(name, product.GetName());
            Assert.Equal(category, product.GetCategory());
            Assert.Equal(wholesale, product.GetWholesalePrice());
            Assert.Equal(sale, product.GetSalePrice());
            Assert.Equal(qty, product.GetQuantity());

            // Optional defaults
            Assert.NotNull(product.GetId());
            Assert.NotEmpty(product.GetDisplayCode());
            Assert.Null(product.GetBarcode());
            Assert.Equal(Product.Unit.Piece, product.GetProductUnit());
            Assert.Null(product.GetSupplier());
            Assert.Null(product.GetManufacturer());
            Assert.Equal(_fixedTime.UtcNow, product.GetArrivalDate());
            Assert.Null(product.GetExpirationDate());
            Assert.False(product.GetIsDeleted());
            Assert.False(product.GetIsArchived());
        }

        [Fact]
        public void Build_WithAllFields_AssignedCorrectly()
        {
            // Arrange
            var builder = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetId("custom-id")
                .SetDisplayCode("DC123456")
                .SetBarcode("1234567890123")
                .SetProductUnit(Product.Unit.Kilogram)
                .SetSupplier("Sup")
                .SetManufacturer("Manu");
            var arrival = new DateTime(2025, 1, 1);
            var expiration = new DateTime(2025, 1, 5);
            builder.SetArrivalDate(arrival)
                   .SetExpirationDate(expiration);

            // Act
            var product = builder.Build(_fixedTime);

            // Assert
            Assert.Equal("custom-id", product.GetId());
            Assert.Equal("DC123456", product.GetDisplayCode());
            Assert.Equal("1234567890123", product.GetBarcode()?.Value);
            Assert.Equal(Product.Unit.Kilogram, product.GetProductUnit());
            Assert.Equal("Sup", product.GetSupplier());
            Assert.Equal("Manu", product.GetManufacturer());
            Assert.Equal(arrival, product.GetArrivalDate());
            Assert.Equal(expiration, product.GetExpirationDate());
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void SetId_Invalid_ThrowsWithProperDetails(string id)
        {
            // Arrange
            var builder = new ProductBuilder("n", "c", 1m, 2m, 1);

            // Act
            var ex = Assert.Throws<ProductDomainException>(() => builder.SetId(id!));

            // Assert
            Assert.Equal(ProductErrorCode.InvalidOperation, ex.Code);
            Assert.Equal("Id продукта не может быть пустым.", ex.Message);
            Assert.Equal(nameof(ProductBuilder.Id), ex.Field);
        }

        // 2) Слишком длинный Id (>64)
        [Fact]
        public void SetId_TooLong_ThrowsWithProperDetails()
        {
            // Arrange
            var builder = new ProductBuilder("n", "c", 1m, 2m, 1);
            var longId = new string('x', 65);

            // Act
            var ex = Assert.Throws<ProductDomainException>(() => builder.SetId(longId));

            // Assert
            Assert.Equal(ProductErrorCode.InvalidOperation, ex.Code);
            Assert.Equal("Id продукта слишком длинный.", ex.Message);
            Assert.Equal(nameof(ProductBuilder.Id), ex.Field);
        }

        // 3) Пустой или null DisplayCode
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void SetDisplayCode_Invalid_ThrowsWithProperDetails(string code)
        {
            // Arrange
            var builder = new ProductBuilder("n", "c", 1m, 2m, 1);

            // Act
            var ex = Assert.Throws<ProductDomainException>(() => builder.SetDisplayCode(code!));

            // Assert
            Assert.Equal(ProductErrorCode.InvalidOperation, ex.Code);
            Assert.Equal("DisplayCode не может быть пустым.", ex.Message);
            Assert.Equal(nameof(ProductBuilder.DisplayCode), ex.Field);
        }

        // 4) Слишком длинный DisplayCode (>32)
        [Fact]
        public void SetDisplayCode_TooLong_ThrowsWithProperDetails()
        {
            // Arrange
            var builder = new ProductBuilder("n", "c", 1m, 2m, 1);
            var longCode = new string('x', 33);

            // Act
            var ex = Assert.Throws<ProductDomainException>(() => builder.SetDisplayCode(longCode));

            // Assert
            Assert.Equal(ProductErrorCode.InvalidOperation, ex.Code);
            Assert.Equal("DisplayCode слишком длинный.", ex.Message);
            Assert.Equal(nameof(ProductBuilder.DisplayCode), ex.Field);
        }

        [Fact]
        public void SetDisplayCode_TooLong_Throws()
        {
            var builder = new ProductBuilder("n", "c", 1m, 2m, 1);
            var longCode = new string('x', 33);
            Assert.Throws<ProductDomainException>(() => builder.SetDisplayCode(longCode));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void BarcodeVO_EmptyOrNull_ThrowsArgumentException(string value)
        {
            // Act
            var ex = Assert.Throws<ArgumentException>(() => new BarcodeVO(value));

            // Assert
            Assert.Equal("Штрихкод не может быть пустым.", ex.Message);
            Assert.Equal(nameof(value), ex.ParamName);
        }

        // Тест для некорректного штрихкода (не 13 цифр)
        [Fact]
        public void BarcodeVO_InvalidLength_ThrowsArgumentException()
        {
            // Arrange
            var invalidBarcode = "123456789012"; // 12 цифр

            // Act
            var ex = Assert.Throws<ArgumentException>(() => new BarcodeVO(invalidBarcode));

            // Assert
            Assert.Equal("Штрихкод должен состоять из 13 цифр (EAN13).", ex.Message);
            Assert.Equal(nameof(invalidBarcode), ex.ParamName);
        }

        // Тест для корректного штрихкода
        [Fact]
        public void BarcodeVO_ValidBarcode_DoesNotThrow()
        {
            // Arrange
            var validBarcode = "1234567890123"; // 13 цифр

            // Act
            var barcode = new BarcodeVO(validBarcode);

            // Assert
            Assert.Equal(validBarcode, barcode.Value);
        }

        [Theory]
        [InlineData("", "Cat", 1, 2, 1)]
        [InlineData("Name", "   ", 1, 2, 1)]
        public void Constructor_InvalidNameOrCategory_Throws(string name, string cat, decimal w, decimal s, int q)
        {
            var builder = new ProductBuilder(name, cat, w, s, q);
            Assert.Throws<ProductDomainException>(() => builder.Build(_fixedTime));
        }

        [Theory]
        [InlineData(-1, 2, 1)]
        [InlineData(1, -2, 1)]
        [InlineData(5, 3, 1)] // sale < wholesale
        public void Build_InvalidPrices_Throws(decimal w, decimal s, int q)
        {
            var builder = new ProductBuilder("N", "C", w, s, q);
            Assert.Throws<ProductDomainException>(() => builder.Build(_fixedTime));
        }

        [Fact]
        public void Constructor_InvalidQuantity_Throws()
        {
            var builder = new ProductBuilder("N", "C", 1, 2, -1);
            Assert.Throws<ProductDomainException>(() => builder.Build(_fixedTime));
        }

        [Theory]
        [InlineData(" ")]
        [InlineData("")]
        public void SetSupplier_Invalid_Throws(string sup)
        {
            var builder = new ProductBuilder("N", "C", 1, 2, 1).SetSupplier(sup);
            Assert.Throws<ProductDomainException>(() => builder.Build(_fixedTime));
        }

        [Theory]
        [InlineData(" ")]
        [InlineData("")]
        public void SetManufacturer_Invalid_Throws(string manu)
        {
            var builder = new ProductBuilder("N", "C", 1, 2, 1).SetManufacturer(manu);
            Assert.Throws<ProductDomainException>(() => builder.Build(_fixedTime));
        }

        [Fact]
        public void FluentInterface_ReturnsSameInstance()
        {
            var builder = new ProductBuilder("N", "C", 1, 2, 1);
            Assert.Same(builder, builder.SetId("x"));
            Assert.Same(builder, builder.SetDisplayCode("y"));
            Assert.Same(builder, builder.SetBarcode("1234567890123"));
            Assert.Same(builder, builder.SetSupplier("S"));
            Assert.Same(builder, builder.SetManufacturer("M"));
        }

        [Fact]
        public void Build_WithExpirationBeforeArrival_Throws()
        {
            var builder = new ProductBuilder("n", "c", 1m, 2m, 1);
            var arrival = new DateTime(2025, 1, 10);
            var expiration = new DateTime(2025, 1, 5);
            builder.SetArrivalDate(arrival)
                   .SetExpirationDate(expiration);
            Assert.Throws<ProductDomainException>(() => builder.Build(_fixedTime));
        }
    }

    // TestTimeProvider implementation
    public class TestTimeProvider : ITimeProvider
    {
        public DateTime UtcNow { get; set; }
        public TestTimeProvider(DateTime fixedTime) => UtcNow = fixedTime;
    }
}
