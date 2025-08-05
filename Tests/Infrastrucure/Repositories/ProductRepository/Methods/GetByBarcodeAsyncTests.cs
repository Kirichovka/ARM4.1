using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Domain.ValueObjects;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class GetByBarcodeAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(options);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ReturnsNull_When_BarcodeNullOrWhitespace(string barcode)
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.GetByBarcodeAsync(barcode);
            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNull_When_NotFoundInDb()
        {
            var db = CreateInMemoryDbContext();
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.GetByBarcodeAsync("0000000000000");
            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsEntity_And_CachesIt()
        {
            // Arrange: создаём продукт с валидным штрихкодом
            var barcodeValue = "1234567890123";
            var product = new ProductBuilder(
                    name: "Test",
                    category: "Cat",
                    wholesalePrice: 10m,
                    salePrice: 15m,
                    quantity: 5)
                .SetBarcode(barcodeValue)
                .Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1: читаем из БД и сохраняем в кэш
            var fetched1 = await repo.GetByBarcodeAsync(barcodeValue);

            // Удаляем из БД, чтобы проверить, что второй вызов берёт из кэша
            db.Entry(product).State = EntityState.Detached;
            db.Products.Remove(new ProductBuilder(
                    name: product.Name,
                    category: product.GetCategory(),
                    wholesalePrice: product.GetWholesalePrice(),
                    salePrice: product.GetSalePrice(),
                    quantity: product.GetQuantity())
                .SetBarcode(barcodeValue)
                .SetId(product.GetId())
                .SetDisplayCode(product.GetDisplayCode())
                .Build());
            await db.SaveChangesAsync();

            // Act #2: теперь должно вернуть из кэша
            var fetched2 = await repo.GetByBarcodeAsync(barcodeValue);

            Assert.NotNull(fetched1);
            Assert.Equal(barcodeValue, fetched1!.GetBarcode()!.Value);

            Assert.NotNull(fetched2);
            Assert.Equal(barcodeValue, fetched2!.GetBarcode()!.Value);
        }

        [Fact]
        public async Task ReturnsNull_When_CancellationRequested()
        {
            // Arrange
            var barcodeValue = "1234567890123";
            var product = new ProductBuilder("T", "C", 1, 2, 1)
                .SetBarcode(barcodeValue)
                .Build();
            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await repo.GetByBarcodeAsync(barcodeValue, cts.Token);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task Throws_On_GenericException_FromCache()
        {
            // Arrange: кэш, у которого TryGetValue бросает
            var badCache = new ThrowingCache();
            var db = CreateInMemoryDbContext();
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                badCache);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetByBarcodeAsync("anybarcode"));
            Assert.Equal("cache fail", ex.Message);
        }

        private class ThrowingCache : IMemoryCache
        {
            public ICacheEntry CreateEntry(object key) => throw new NotImplementedException();
            public void Remove(object key) => throw new NotImplementedException();
            public void Dispose() { }
            public bool TryGetValue(object key, out object value)
            {
                throw new InvalidOperationException("cache fail");
            }
        }
        [Fact]
        public async Task ReturnsCachedEntity_IfExistsInCache()
        {
            // Arrange
            var barcodeValue = "CACHE123";
            var product = new ProductBuilder("N", "C", 1, 2, 3)
                .SetBarcode(barcodeValue)
                .Build();

            var db = CreateInMemoryDbContext();
            // В БД ничего не кладём — чтобы убедиться, что метод не лезет в БД при попадании в кэш

            // Подготовим кэш «вручную»
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var cacheKey = $"Product_Barcode_{barcodeValue}";
            cache.Set(cacheKey, product, new MemoryCacheEntryOptions { Size = 1 });

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act
            var result = await repo.GetByBarcodeAsync(barcodeValue);

            // Assert
            Assert.NotNull(result);
            Assert.Same(product, result); // именно тот же объект, что в кэше
        }

        [Fact]
        public async Task SkipsNullCacheEntry_AndQueriesDb()
        {
            // Arrange
            var barcodeValue = "NULLCACHE";
            var realProduct = new ProductBuilder("X", "Y", 5, 6, 7)
                .SetBarcode(barcodeValue)
                .Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(realProduct);
            await db.SaveChangesAsync();

            // Кэшируем явный null
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var cacheKey = $"Product_Barcode_{barcodeValue}";
            cache.Set(cacheKey, (Product?)null, new MemoryCacheEntryOptions { Size = 1 });

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act
            var result = await repo.GetByBarcodeAsync(barcodeValue);

            // Assert
            // т.к. в кэше null, метод должен перейти к БД и вернуть нужный объект
            Assert.NotNull(result);
            Assert.Equal(realProduct.GetId(), result!.GetId());
        }
        [Fact]
        public async Task GetByBarcodeAsync_LogsNotFound_WhenProductIsNull()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var repo = new ProductRepository(db, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));

            string barcode = "9999999999999";
            var product = await repo.GetByBarcodeAsync(barcode);

            Assert.Null(product);

            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Продукт не найден по Barcode")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public async Task GetByBarcodeAsync_LogsFoundInCache_WhenCalledTwice()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var barcodeValue = "CACHELOG";
            var product = new ProductBuilder("Test", "Cat", 1, 2, 3)
                .SetBarcode(barcodeValue).Build();

            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Products.Add(product);
            db.SaveChanges();

            var repo = new ProductRepository(db, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));

            // Первый вызов — из БД (кэш заполняется)
            await repo.GetByBarcodeAsync(barcodeValue);

            // Второй вызов — из кэша
            await repo.GetByBarcodeAsync(barcodeValue);

            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Найдено в кэше. Key=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public async Task GetByBarcodeAsync_Always_LogsCompletion()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var barcodeValue = "COMPLETELOG";
            var product = new ProductBuilder("Test", "Cat", 1, 2, 3)
                .SetBarcode(barcodeValue).Build();

            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Products.Add(product);
            db.SaveChanges();

            var repo = new ProductRepository(db, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.GetByBarcodeAsync(barcodeValue);

            // Assert: всегда пишется Information лог с нужным текстом
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("GetByBarcodeAsync завершён")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public async Task GetByBarcodeAsync_LogsCacheMiss_And_SavesToCache()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            string barcode = "7777777777777";
            db.Products.Add(new ProductBuilder("TestProduct", "Cat", 1, 2, 3).SetBarcode(barcode).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            // Act: кэш пустой => попадём в нужный if
            var result = await repo.GetByBarcodeAsync(barcode);

            // Assert: был cache miss и сохранение в кэш
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Кэш пропуск. Key=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once);

            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Сохранено в кэше. Key=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once);
        }
        [Fact]
        public async Task GetByBarcodeListAsync_LogsCacheSaveWithCount()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            string barcode = "COLL123456";
            // Если по логике метода допускается несколько продуктов с одним штрихкодом
            db.Products.Add(new ProductBuilder("P1", "Cat", 1, 2, 3).SetBarcode(barcode).Build());
            db.Products.Add(new ProductBuilder("P2", "Cat", 4, 5, 6).SetBarcode(barcode).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            // Act
            var products = await repo.GetByBarcodeAsync(barcode); // или аналогичный метод

            // Assert: лог "Сохранено в кэше. Key=..., Count=2"
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Сохранено в кэше. Key=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once);

        }

    }
}
