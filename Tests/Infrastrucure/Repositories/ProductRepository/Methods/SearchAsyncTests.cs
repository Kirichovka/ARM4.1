using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class SearchAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContextWithProducts(int count)
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ARM4DbContext(options);

            for (int i = 0; i < count; i++)
                db.Products.Add(Product.CreateTestProduct());
            db.SaveChanges();

            return db;
        }

        private class ThrowingCache : IMemoryCache
        {
            public ICacheEntry CreateEntry(object key) => throw new NotImplementedException();
            public void Remove(object key) { }
            public void Dispose() { }
            public bool TryGetValue(object key, out object value)
            {
                throw new InvalidOperationException("cache fail");
            }
        }

        [Fact]
        public async Task ThrowsArgumentOutOfRange_When_SkipNegative()
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContextWithProducts(10),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repo.SearchAsync(skip: -1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1001)]
        public async Task ThrowsArgumentOutOfRange_When_TakeInvalid(int take)
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContextWithProducts(10),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repo.SearchAsync(take: take));
        }

        [Fact]
        public async Task ReturnsPagedResults_And_Caches()
        {
            // Arrange: 100 products
            var db = CreateInMemoryDbContextWithProducts(100);
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1: без skip/take → вернёт первые 50
            var first = await repo.SearchAsync();
            Assert.Equal(50, first.Count);

            // Act #2: skip=50,take=20 → вернёт следующие 20
            var second = await repo.SearchAsync(skip: 50, take: 20);
            Assert.Equal(20, second.Count);

            // Проверяем кэширование: second вызов same params
            // удалим из БД, чтобы убедиться в cache hit
            db.Products.RemoveRange(db.Products);
            await db.SaveChangesAsync();

            var cached = await repo.SearchAsync(skip: 50, take: 20);
            Assert.Equal(20, cached.Count);
        }

        [Fact]
        public async Task Throws_InvalidOperationException_When_CacheFails()
        {
            var db = CreateInMemoryDbContextWithProducts(10);
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new ThrowingCache());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.SearchAsync());
            Assert.Equal("cache fail", ex.Message);
        }

        [Fact]
        public async Task Throws_On_DbFailure()
        {
            // Мокаем DbSet<Product>, чтобы ToListAsync бросал
            var mockSet = new Mock<DbSet<Product>>();
            mockSet.As<IAsyncEnumerable<Product>>()
                   .Setup(m => m.GetAsyncEnumerator(default))
                   .Throws(new InvalidOperationException("db fail"));
            mockSet.As<IQueryable<Product>>()
                   .Setup(m => m.Provider)
                   .Throws(new InvalidOperationException("db fail"));

            var ctxMock = new Mock<ARM4DbContext>(new DbContextOptions<ARM4DbContext>());
            ctxMock.Setup(c => c.Products).Returns(mockSet.Object);

            var repo = new ProductRepository(
                ctxMock.Object,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.SearchAsync());
        }

        [Fact]
        public async Task Throws_On_Cancellation()
        {
            var db = CreateInMemoryDbContextWithProducts(10);
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));

            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => repo.SearchAsync(cancellationToken: cts.Token));
        }
        [Theory]
        [InlineData(-1, 10)] // skip < 0
        [InlineData(0, 0)]   // take <= 0
        [InlineData(0, 1001)] // take > 1000
        public async Task SearchAsync_Throws_OnInvalidSkipOrTake(int skip, int take)
        {
            var repo = new ProductRepository(
                new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                repo.SearchAsync(name: null, category: null, supplier: null, skip: skip, take: take, orderBy: null, ascending: true, CancellationToken.None));
        }
        [Fact]
        public async Task SearchAsync_LogsCacheMiss_And_SavesToCache()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Products.Add(new ProductBuilder("A", "Cat", 1, 2, 3).Build());
            db.Products.Add(new ProductBuilder("B", "Cat", 4, 5, 6).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            var products = await repo.SearchAsync("A", null, null, 0, 10, null, true, CancellationToken.None);

            // Лог cache miss
            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Кэш пропуск. Key=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            // Лог cache set
            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Сохранено в кэше. Key=") && v.ToString()!.Contains("Retrieved=1")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            // Лог завершения
            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SearchAsync завершён. Count=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task SearchAsync_LogsCacheHit_WhenCalledTwice()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Products.Add(new ProductBuilder("C", "Cat", 1, 2, 3).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            // Первый вызов - кэш пустой
            await repo.SearchAsync("C", null, null, 0, 10, null, true, CancellationToken.None);
            // Второй вызов - попадание в кэш
            await repo.SearchAsync("C", null, null, 0, 10, null, true, CancellationToken.None);

            // Проверяем, что кэш-хит логировался
            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Найдено в кэше. Key=")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task SearchAsync_LogsError_OnException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var dbMock = new Mock<ARM4DbContext>(new DbContextOptions<ARM4DbContext>());

            // Мокаем Products так, чтобы .ToListAsync всегда бросал
            var setMock = new Mock<DbSet<Product>>();
            setMock.As<IQueryable<Product>>().Setup(m => m.Provider).Throws(new InvalidOperationException("db fail"));
            dbMock.SetupGet(c => c.Products).Returns(setMock.Object);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, cache);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.SearchAsync("ANY", null, null, 0, 10, null, true, CancellationToken.None));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Ошибка в SearchAsync.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task SearchAsync_LogsStart_WithAllParams()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Products.Add(new ProductBuilder("X", "Y", 1, 2, 3).Build());
            db.SaveChanges();

            var repo = new ProductRepository(db, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));

            await repo.SearchAsync("X", "Y", "S", 0, 1, "name", false, CancellationToken.None);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Начало SearchAsync.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task ExistsByBarcodeAsync_LogsCacheSet_WhenMiss()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            // В базе есть продукт с нужным barcode
            string barcode = "EXISTS_BARCODE";
            db.Products.Add(new ProductBuilder("N", "C", 1, 2, 3).SetBarcode(barcode).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            // Первый вызов — попадём в cache miss, сохранится в кэш
            var exists = await repo.ExistsByBarcodeAsync(barcode);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Сохранено в кэше. Key=") &&
                        v.ToString()!.Contains("Exists=True")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ExistsByBarcodeAsync завершён")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            Assert.True(exists);
        }
        [Fact]
        public async Task ExistsByBarcodeAsync_LogsCacheHit_WhenHit()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            string barcode = "EXISTS_CACHE_HIT";
            db.Products.Add(new ProductBuilder("N", "C", 1, 2, 3).SetBarcode(barcode).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            // Первый вызов — попадём в cache miss, сохранится в кэш
            await repo.ExistsByBarcodeAsync(barcode);
            // Второй вызов — попадём в cache hit
            await repo.ExistsByBarcodeAsync(barcode);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Найдено в кэше. Key=") &&
                        v.ToString()!.Contains("Exists=True")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task ExistsByBarcodeAsync_LogsCacheSetAndHit_ForNotFound()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            string barcode = "NOT_FOUND";
            // Продукт НЕ добавляем

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            // Первый вызов — cache miss, Exists=False, сохранится в кэш
            var exists1 = await repo.ExistsByBarcodeAsync(barcode);

            // Второй вызов — cache hit, Exists=False
            var exists2 = await repo.ExistsByBarcodeAsync(barcode);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Сохранено в кэше. Key=") &&
                        v.ToString()!.Contains("Exists=False")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Найдено в кэше. Key=") &&
                        v.ToString()!.Contains("Exists=False")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            Assert.False(exists1);
            Assert.False(exists2);
        }

    }
}
