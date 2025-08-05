using System;
using System.Linq;
using ARM4.Domain.Builders;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.Entities;
using ARM4.Domain.Common;
using ARM4.Domain.ValueObjects;
using ARM4.Tests.TestHelpers;
using Xunit;

namespace ARM4.Tests.Entities
{
    public class ProductTests
    {
        // Фабрика для быстрого создания "валидного" продукта
        private Product CreateValidProduct(ITimeProvider? tp = null) =>
            new ProductBuilder("Name", "Category", 10m, 15m, 5)
                .SetBarcode("0000000000000")
                .SetProductUnit(Product.Unit.Kilogram)
                .SetSupplier("Sup")
                .SetManufacturer("Man")
                .SetArrivalDate(new DateTime(2025, 1, 1))
                .SetExpirationDate(new DateTime(2025, 12, 31))
                .Build(tp);

        [Fact]
        public void Build_WithAllValidParameters_SetsAllProperties()
        {
            var now = new DateTime(2025, 6, 1);
            var tp = new TestTimeProvider(now);

            var p = CreateValidProduct(tp);

            Assert.False(string.IsNullOrWhiteSpace(p.GetId()));
            Assert.Matches(@"^\d{8}$", p.GetDisplayCode());
            Assert.Equal("Name", p.GetName());
            Assert.Equal("Category", p.GetCategory());
            Assert.Equal(10m, p.GetWholesalePrice());
            Assert.Equal(15m, p.GetSalePrice());
            Assert.Equal(5, p.GetQuantity());
            Assert.Equal("0000000000000", p.GetBarcode()!.Value);
            Assert.Equal(Product.Unit.Kilogram, p.GetProductUnit());
            Assert.Equal("Sup", p.GetSupplier());
            Assert.Equal("Man", p.GetManufacturer());
            Assert.Equal(new DateTime(2025, 1, 1), p.GetArrivalDate());
            Assert.Equal(new DateTime(2025, 12, 31), p.GetExpirationDate());
            Assert.False(p.GetIsDeleted());
            Assert.False(p.GetIsArchived());
            Assert.True(p.IsAvailable());
            Assert.False(p.IsExpired());
        }

        [Theory]
        [InlineData("", ProductErrorCode.InvalidName)]
        [InlineData("   ", ProductErrorCode.InvalidName)]
        public void SetName_EmptyOrWhitespace_ThrowsInvalidName(string v, ProductErrorCode code)
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.SetName(v));
            Assert.Equal(code, ex.Code);
        }

        [Fact]
        public void SetName_TooLong_Throws()
        {
            var p = CreateValidProduct();
            var longName = new string('x', 201);
            var ex = Assert.Throws<ProductDomainException>(() => p.SetName(longName));
            Assert.Equal(ProductErrorCode.InvalidName, ex.Code);
        }

        [Theory]
        [InlineData("", ProductErrorCode.InvalidCategory)]
        [InlineData("   ", ProductErrorCode.InvalidCategory)]
        public void SetCategory_EmptyOrWhitespace_Throws(string v, ProductErrorCode code)
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.SetCategory(v));
            Assert.Equal(code, ex.Code);
        }

        [Fact]
        public void SetCategory_TooLong_ThrowsInvalidNameErrorCode()
        {
            var p = CreateValidProduct();
            var longCat = new string('c', 201);
            var ex = Assert.Throws<ProductDomainException>(() => p.SetCategory(longCat));
            Assert.Equal(ProductErrorCode.InvalidName, ex.Code);
        }

        [Fact]
        public void SetWholesalePrice_Negative_Throws()
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.SetWholesalePrice(-0.01m));
            Assert.Equal(ProductErrorCode.InvalidWholesalePrice, ex.Code);
        }

        [Fact]
        public void SetSalePrice_Negative_Throws()
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.SetSalePrice(-1m));
            Assert.Equal(ProductErrorCode.InvalidSalePrice, ex.Code);
        }

        [Fact]
        public void SetSalePrice_LessThanWholesale_Throws()
        {
            var p = CreateValidProduct();
            p.SetWholesalePrice(10m);
            var ex = Assert.Throws<ProductDomainException>(() => p.SetSalePrice(5m));
            Assert.Equal(ProductErrorCode.InvalidSalePrice, ex.Code);
        }

        [Fact]
        public void SetQuantity_Negative_Throws()
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.SetQuantity(-1));
            Assert.Equal(ProductErrorCode.InvalidQuantity, ex.Code);
        }

        [Fact]
        public void SetProductUnit_InvalidEnum_Throws()
        {
            var p = CreateValidProduct();
            // Неопределённое значение
            var ex = Assert.Throws<ProductDomainException>(() => p.SetProductUnit((Product.Unit)999));
            Assert.Equal(ProductErrorCode.InvalidUnit, ex.Code);
        }

        [Fact]
        public void SetArrivalDate_Default_Throws()
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.SetArrivalDate(default));
            Assert.Equal(ProductErrorCode.InvalidArrivalDate, ex.Code);
        }

        [Fact]
        public void SetExpirationDate_BeforeArrival_Throws()
        {
            var p = CreateValidProduct();
            p.SetArrivalDate(new DateTime(2025, 6, 1));
            var ex = Assert.Throws<ProductDomainException>(() => p.SetExpirationDate(new DateTime(2025, 5, 31)));
            Assert.Equal(ProductErrorCode.InvalidExpirationDate, ex.Code);
        }

        [Fact]
        public void ValidateInvariant_ArchivedWithoutDeleted_Throws()
        {
            var p = CreateValidProduct();
            p.MarkAsArchived();
            var ex = Assert.Throws<ProductDomainException>(() => p.ValidateInvariant());
            Assert.Equal(ProductErrorCode.InvalidStateTransition, ex.Code);
        }

        [Fact]
        public void ReduceQuantity_InvalidOrTooLarge_Throws()
        {
            var p = CreateValidProduct();
            Assert.Throws<ProductDomainException>(() => p.ReduceQuantity(0)).Code
                .Equals(ProductErrorCode.InvalidQuantity);
            Assert.Throws<ProductDomainException>(() => p.ReduceQuantity(10)).Code
                .Equals(ProductErrorCode.NotEnoughStock);
        }

        [Fact]
        public void ReduceQuantity_Valid_DecreasesQuantity()
        {
            var p = CreateValidProduct();
            p.ReduceQuantity(3);
            Assert.Equal(2, p.GetQuantity());
        }

        [Fact]
        public void IncreaseQuantity_Invalid_Throws()
        {
            var p = CreateValidProduct();
            var ex = Assert.Throws<ProductDomainException>(() => p.IncreaseQuantity(0));
            Assert.Equal(ProductErrorCode.InvalidQuantity, ex.Code);
        }

        [Fact]
        public void IncreaseQuantity_Valid_IncreasesQuantity()
        {
            var p = CreateValidProduct();
            p.IncreaseQuantity(10);
            Assert.Equal(15, p.GetQuantity());
        }

        [Fact]
        public void IsExpired_UsesTimeProvider()
        {
            var past = new DateTime(2025, 1, 1);
            var tp = new TestTimeProvider(past);
            var p = CreateValidProduct(tp);
            p.SetExpirationDate(past.AddDays(-1));
            Assert.True(p.IsExpired());
        }

        [Fact]
        public void IsAvailable_RespectsFlagsAndExpiration()
        {
            var now = new DateTime(2025, 1, 1);
            var tp = new TestTimeProvider(now);
            var p = CreateValidProduct(tp);
            // Наличие товара
            Assert.True(p.IsAvailable());

            // Просрочен
            p.SetExpirationDate(now.AddDays(-1));
            Assert.False(p.IsAvailable());

            // Восстановим срок, но удалим
            p.SetExpirationDate(now.AddDays(1));
            p.MarkAsDeleted();
            Assert.False(p.IsAvailable());

            // Восстановим, но архивируем
            p = CreateValidProduct(tp);
            p.MarkAsArchived();
            Assert.False(p.IsAvailable());
        }

        [Fact]
        public void CreateTestProduct_ReturnsValidProduct()
        {
            var now = new DateTime(2025, 2, 2);
            var tp = new TestTimeProvider(now);
            var p = Product.CreateTestProduct(tp);
            Assert.Equal("Test Product", p.GetName());
            Assert.True(p.GetQuantity() > 0);
            Assert.Equal(now.Date, p.GetArrivalDate().Date);
        }
    }
}
