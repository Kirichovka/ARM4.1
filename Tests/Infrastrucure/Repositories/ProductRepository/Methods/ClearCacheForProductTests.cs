using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class ClearCacheForProductTests
    {
        // Помощник для вызова приватного метода через reflection
        private void InvokeClearCache(ProductRepository repo, Product product)
        {
            var mi = typeof(ProductRepository)
                .GetMethod("ClearCacheForProduct", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(mi);
            mi.Invoke(repo, new object[] { product });
        }

        private ProductRepository CreateRepo(IMemoryCache cache)
        {
            // Контекст не используется в этом методе, но нужен конструктор
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            return new ProductRepository(db, new NullLogger<ProductRepository>(), cache);
        }

        [Fact]
        public void ThrowsArgumentNull_When_ProductIsNull()
        {
            var repo = CreateRepo(new MemoryCache(new MemoryCacheOptions()));
            var mi = typeof(ProductRepository)
                .GetMethod("ClearCacheForProduct", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(mi);

            // Act & Assert
            var ex = Assert.Throws<TargetInvocationException>(() => mi.Invoke(repo, new object[] { null! }));
            Assert.IsType<ArgumentNullException>(ex.InnerException);
            Assert.Equal("product", ((ArgumentNullException)ex.InnerException).ParamName);
        }

        [Fact]
        public void RemovesAllKeys_When_ProductWithoutBarcode()
        {
            // Arrange
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3).Build();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = CreateRepo(cacheMock.Object);

            // Act
            InvokeClearCache(repo, product);

            // Assert: ключи удалены
            cacheMock.Verify(c => c.Remove("AllProducts"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            // Barcode null => не должен вызываться
            cacheMock.Verify(c => c.Remove(It.Is<string>(s => s.StartsWith("Product_Barcode_"))), Times.Never);
        }

        [Fact]
        public void RemovesAllKeys_When_ProductWithBarcode()
        {
            // Arrange
            var barcode = "12345";
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3)
                .SetBarcode(barcode)
                .Build();

            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = CreateRepo(cacheMock.Object);

            // Act
            InvokeClearCache(repo, product);

            // Assert
            cacheMock.Verify(c => c.Remove("AllProducts"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Barcode_{barcode}"), Times.Once);
        }

        [Fact]
        public void SwallowsExceptions_FromCacheRemove()
        {
            // Arrange
            var barcode = "X";
            var product = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetBarcode(barcode)
                .Build();

            // Кэш, у которого первый Remove бросает, остальные — успешны
            var seq = 0;
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()))
                .Callback<object>(key =>
                {
                    if (seq++ == 0)
                        throw new InvalidOperationException("fail remove");
                });

            var repo = CreateRepo(cacheMock.Object);

            // Act: не должно пробросить
            InvokeClearCache(repo, product);

            // Assert: Remove вызван для всех ключей, несмотря на первое падение
            cacheMock.Verify(c => c.Remove("AllProducts"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            cacheMock.Verify(c => c.Remove($"Product_Barcode_{barcode}"), Times.Once);
        }
        [Fact]
        public void ClearCacheForProduct_LogsTrace_OnSuccess()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var product = Product.CreateTestProduct();
            product.GetType().GetProperty("barcode")?.SetValue(product, "1234567890"); // если поле не публичное

            // Act
            var method = repo.GetType().GetMethod("ClearCacheForProduct", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            method.Invoke(repo, new object[] { product });

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Кэш для ключа 'Product_Barcode_")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }
        [Fact]
        public void ClearCacheForProduct_LogsError_OnCacheException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>())).Throws(new Exception("CacheFail"));

            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var product = Product.CreateTestProduct();
            product.GetType().GetProperty("barcode")?.SetValue(product, "1234567890");

            var method = repo.GetType().GetMethod("ClearCacheForProduct", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            method.Invoke(repo, new object[] { product });

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Ошибка при очистке кэша для продукта")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public void ClearCacheForProducts_ThrowsArgumentNullException_OnNull()
        {
            var repo = new ProductRepository(
                new Mock<ARM4DbContext>().Object,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var method = repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            Assert.Throws<ArgumentNullException>(() => method.Invoke(repo, new object[] { null! }));
        }
        [Fact]
        public void ClearCacheForProducts_LogsWarning_OnTooManyProducts()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();

            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = Enumerable.Range(0, 10001).Select(_ => Product.CreateTestProduct()).ToList();

            var method = repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            method.Invoke(repo, new object[] { products });

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Попытка массовой очистки кэша для")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public void ClearCacheForProducts_LogsWarning_OnNullProduct()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();

            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = new Product?[] { Product.CreateTestProduct(), null, Product.CreateTestProduct() };

            var method = repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            method.Invoke(repo, new object[] { products });

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Пропущен null-продукт")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }
        [Fact]
        public void ClearCacheForProducts_LogsWarning_OnInvalidProductId()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();

            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var product = Product.CreateTestProduct();
            product.GetType().GetProperty("id")?.SetValue(product, "BAD_ID");
            var products = new[] { product };

            var method = repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            method.Invoke(repo, new object[] { products });

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Некорректный формат ProductId")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public void ClearCacheForProducts_LogsError_OnCacheException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>())).Throws(new Exception("CacheFail"));

            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = new[] { Product.CreateTestProduct() };

            var method = repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            method.Invoke(repo, new object[] { products });

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Ошибка очистки кэша для продукта")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }

    }
}
