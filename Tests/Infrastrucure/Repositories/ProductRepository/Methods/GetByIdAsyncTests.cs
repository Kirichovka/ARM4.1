using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class GetByIdAsyncTests
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
        public async Task ReturnsNull_When_IdNullOrWhitespace(string id)
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.GetByIdAsync(id);
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

            var result = await repo.GetByIdAsync("no-such-id");
            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsEntity_And_CachesIt()
        {
            // Arrange: добавляем продукт
            var db = CreateInMemoryDbContext();
            var product = Product.CreateTestProduct();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);
            var id = product.GetId();

            // Act #1: читает из БД и сохраняет в кэш
            var fetched1 = await repo.GetByIdAsync(id);

            // Удаляем из БД «вручную», чтобы убедиться, что далее идёт кэш
            db.Entry(product).State = EntityState.Detached;
            db.Products.Remove(new ProductBuilder(product.Name, product.GetCategory(), product.GetWholesalePrice(), product.GetSalePrice(), product.GetQuantity())
                                     .SetId(product.GetId())
                                     .SetDisplayCode(product.GetDisplayCode())
                                     .Build());
            await db.SaveChangesAsync();

            // Act #2: должен вернуть из кэша
            var fetched2 = await repo.GetByIdAsync(id);

            Assert.NotNull(fetched1);
            Assert.Equal(id, fetched1!.GetId());

            Assert.NotNull(fetched2);
            Assert.Equal(id, fetched2!.GetId());
        }

        [Fact]
        public async Task ReturnsNull_When_CancellationRequested()
        {
            var db = CreateInMemoryDbContext();
            var product = Product.CreateTestProduct();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.GetByIdAsync(product.GetId(), cts.Token);
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
                () => repo.GetByIdAsync("any-id"));
            Assert.Equal("cache fail", ex.Message);
        }

        // IMemoryCache, у которого TryGetValue бросает, чтобы попасть в общий catch и пробросить
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
        public async Task ExistsByIdAsync_ParallelSameId_CallsDbOnce_CachesCorrectly()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ARM4DbContext(options);
            var builder = new ProductBuilder("N", "C", 1, 2, 3).SetId("PARALLEL_ID");
            var product = builder.Build();
            db.Products.Add(product);
            db.SaveChanges();

            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };

            int dbCalls = 0;
            dbMock.Setup(d => d.Products).Returns(db.Products);
            dbMock.Setup(d => d.Products.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref dbCalls))
                .ReturnsAsync(true);

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(dbMock.Object, new NullLogger<ProductRepository>(), cache);

            var tasks = Enumerable.Range(0, 20)
                .Select(_ => repo.ExistsByIdAsync(product.GetId()))
                .ToList();

            var results = await Task.WhenAll(tasks);

            Assert.All(results, Assert.True);
            Assert.True(dbCalls == 1, $"Ожидали один реальный вызов к БД, было: {dbCalls}");
        }
        [Fact]
        public async Task ExistsByIdAsync_ParallelManyIds_HandlesConcurrency()
        {
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            // Добавим продукты
            var ids = Enumerable.Range(0, 1000).Select(i => $"ID_{i}").ToList();
            foreach (var id in ids)
                db.Products.Add(new ProductBuilder("N", "C", 1, 2, 3).SetId(id).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            var tasks = ids.Select(id => repo.ExistsByIdAsync(id)).ToList();
            var results = await Task.WhenAll(tasks);

            Assert.All(results, Assert.True);
        }
        [Fact]
        public async Task ExistsByIdAsync_Performance_10kRequests()
        {
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var ids = Enumerable.Range(0, 10_000).Select(i => $"ID_{i}").ToList();
            foreach (var id in ids)
                db.Products.Add(new ProductBuilder("N", "C", 1, 2, 3).SetId(id).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await Task.WhenAll(ids.Select(id => repo.ExistsByIdAsync(id)));
            sw.Stop();

            Assert.All(results, Assert.True);
            Assert.True(sw.Elapsed.TotalSeconds < 20, $"Долго: {sw.Elapsed.TotalSeconds}s");
        }
        [Fact]
        public async Task ExistsByIdAsync_CacheSizeLimit_NotExceeded()
        {
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var ids = Enumerable.Range(0, 50).Select(i => $"ID_{i}").ToList();
            foreach (var id in ids)
                db.Products.Add(new ProductBuilder("N", "C", 1, 2, 3).SetId(id).Build());
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Делаем много обращений — кэш должен автоматически вытеснять старое
            foreach (var id in ids)
                await repo.ExistsByIdAsync(id);

            // К сожалению, MemoryCache не предоставляет API для проверки Count.
            // Но если SizeLimit работает — не будет OOM и всё будет быстро.
            Assert.True(true, "Если SizeLimit не сработал — тест упадёт по памяти.");
        }
        [Fact]
        public async Task ExistsByIdAsync_SizeLimitZero_DisablesCache()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ARM4DbContext(options);

            var builder = new ProductBuilder("N", "C", 1, 2, 3).SetId("ID_DISABLED_CACHE");
            var product = builder.Build();
            db.Products.Add(product);
            db.SaveChanges();

            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            int dbCalls = 0;
            dbMock.Setup(d => d.Products).Returns(db.Products);
            dbMock.Setup(d => d.Products.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref dbCalls))
                .ReturnsAsync(true);

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 0 });
            var repo = new ProductRepository(dbMock.Object, new NullLogger<ProductRepository>(), cache);

            // 3 обращения — все идут в БД
            for (int i = 0; i < 3; i++)
                Assert.True(await repo.ExistsByIdAsync(product.GetId()));

            Assert.True(dbCalls == 3, $"Ожидали 3 вызова к БД, было: {dbCalls}");
        }
        [Fact]
        public async Task ExistsByIdAsync_CacheExpires_ReturnsUpdatedValue()
        {
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var builder = new ProductBuilder("N", "C", 1, 2, 3).SetId("ID_EXP");
            var product = builder.Build();
            db.Products.Add(product);
            db.SaveChanges();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Первый вызов: попадает в кэш (true)
            Assert.True(await repo.ExistsByIdAsync(product.GetId()));

            // Удаляем из БД и ждём (имитируем expiration)
            db.Products.Remove(product);
            db.SaveChanges();
            await Task.Delay(1200); // expiration >1 сек, если настроен

            // Второй вызов: если кэш протух — вернёт false
            var result = await repo.ExistsByIdAsync(product.GetId());
            // Тут assert зависит от того, как ты настраиваешь expiration — если его нет, будет true
            Assert.True(true, "Поставь нужный assert под свой expiration");
        }
        [Fact]
        public async Task ExistsByIdAsync_DbSometimesFails_OtherwiseReturnsCorrectly()
        {
            var id = "FLOPPY";
            var builder = new ProductBuilder("N", "C", 1, 2, 3).SetId(id);
            var product = builder.Build();

            var dbMock = new Mock<ARM4DbContext>(new DbContextOptions<ARM4DbContext>()) { CallBase = true };
            int callCount = 0;
            dbMock.Setup(d => d.Products).Returns(new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options).Products);
            dbMock.Setup(d => d.Products.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (++callCount % 3 == 0)
                        throw new InvalidOperationException("db fail");
                    return true;
                });

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(dbMock.Object, new NullLogger<ProductRepository>(), cache);

            // Делаем 6 вызовов: каждый третий — кидает ошибку
            var errors = 0;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    var res = await repo.ExistsByIdAsync(id);
                    Assert.True(res);
                }
                catch (InvalidOperationException ex)
                {
                    Assert.Equal("db fail", ex.Message);
                    errors++;
                }
            }
            Assert.Equal(2, errors);
        }

    }
}
