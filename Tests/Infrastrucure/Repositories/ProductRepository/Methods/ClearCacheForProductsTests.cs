using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class ClearCacheForProductsTests
    {
        // Помощник для вызова приватного метода через reflection
        private void InvokeClearCacheForProducts(ProductRepository repo, IEnumerable<Product> products)
        {
            var mi = typeof(ProductRepository)
                .GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(mi);
            mi.Invoke(repo, new object[] { products });
        }

        private ProductRepository CreateRepo(IMemoryCache cache)
        {
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            return new ProductRepository(db, new NullLogger<ProductRepository>(), cache);
        }

        [Fact]
        public void ThrowsArgumentNull_When_ProductsIsNull()
        {
            var repo = CreateRepo(new MemoryCache(new MemoryCacheOptions()));
            var mi = typeof(ProductRepository)
                .GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(mi);

            // Act & Assert
            var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => mi.Invoke(repo, new object[] { null! }));
            Assert.IsType<ArgumentNullException>(ex.InnerException);
            Assert.Equal("products", ((ArgumentNullException)ex.InnerException).ParamName);
        }

        [Fact]
        public void ClearsCache_ForProductWithoutBarcode()
        {
            // Arrange: создаём продукт без штрихкода
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3).Build();

            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = CreateRepo(cacheMock.Object);

            // Act
            InvokeClearCacheForProducts(repo, new[] { product });

            // Assert: очистка кэша для всех ключей, кроме Barcode
            cacheMock.Verify(c => c.Remove("AllProducts"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);

            // Проверка, что Remove по Barcode не вызвано
            string? barcode = product.GetBarcode()?.ToString();
            if (barcode != null)
            {
                cacheMock.Verify(c => c.Remove($"Product_Barcode_{barcode}"), Times.Never);
            }
            else
            {
                cacheMock.Verify(c => c.Remove(It.Is<string>(s => s.StartsWith("Product_Barcode_"))), Times.Never);
            }

            cacheMock.Verify(c => c.Remove($"Products_Name_{product.Name}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Exists_Id_{product.GetId()}"), Times.Once);
            if (barcode != null)
            {
                cacheMock.Verify(c => c.Remove($"Exists_Barcode_{barcode}"), Times.Never);
            }
            else
            {
                cacheMock.Verify(c => c.Remove(It.Is<string>(s => s.StartsWith("Exists_Barcode_"))), Times.Never);
            }

        }

        [Fact]
        public void ClearsCache_ForProductWithBarcode()
        {
            // Arrange: создаём продукт с штрихкодом
            var barcode = "12345";
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3)
                .SetBarcode(barcode)
                .Build();

            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = CreateRepo(cacheMock.Object);

            // Act
            InvokeClearCacheForProducts(repo, new[] { product });

            // Assert: очистка кэша для всех ключей, включая Barcode
            cacheMock.Verify(c => c.Remove("AllProducts"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Barcode_{barcode}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Products_Name_{product.Name}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Exists_Id_{product.GetId()}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Exists_Barcode_{barcode}"), Times.Once);
        }

        [Fact]
        public void SwallowsException_FromCacheRemove()
        {
            // Arrange: создаём продукт с ошибкой при удалении из кэша
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3).Build();

            // Кэш, у которого Remove бросает исключение
            var seq = 0;
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()))
                .Callback<object>(key =>
                {
                    if (seq++ == 0)
                        throw new InvalidOperationException("fail remove");
                });

            var repo = CreateRepo(cacheMock.Object);

            // Act: не должно выбрасывать исключение
            InvokeClearCacheForProducts(repo, new[] { product });

            // Assert: Remove вызван для всех ключей
            cacheMock.Verify(c => c.Remove("AllProducts"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            string? barcode = product.GetBarcode()?.ToString();
            if (barcode != null)
            {
                cacheMock.Verify(c => c.Remove($"Product_Barcode_{barcode}"), Times.Once);
            }
            else
            {
                cacheMock.Verify(c => c.Remove(It.Is<string>(s => s.StartsWith("Product_Barcode_"))), Times.Never);
            }
            cacheMock.Verify(c => c.Remove($"Products_Name_{product.Name}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Exists_Id_{product.GetId()}"), Times.Once);
            if (barcode != null)
            {
                cacheMock.Verify(c => c.Remove($"Exists_Barcode_{barcode}"), Times.Once);
            }
            else
            {
                cacheMock.Verify(c => c.Remove(It.Is<string>(s => s.StartsWith("Exists_Barcode_"))), Times.Never);
            }

        }
    }
}
