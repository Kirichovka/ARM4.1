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
    public class ExistsByBarcodeAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(options);
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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ReturnsFalse_When_BarcodeNullOrWhitespace(string barcode)
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.ExistsByBarcodeAsync(barcode);
            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsFalse_When_NotFoundInDb_And_Caches()
        {
            // Arrange: пустая БД
            var db = CreateInMemoryDbContext();
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1
            var result1 = await repo.ExistsByBarcodeAsync("123");
            // Should query DB and cache false
            Assert.False(result1);

            // Act #2: remove из БД нет смысла, просто ensure cache hit
            var result2 = await repo.ExistsByBarcodeAsync("123");
            Assert.False(result2);
        }

        [Fact]
        public async Task ReturnsTrue_When_FoundInDb_And_Caches()
        {
            // Arrange
            var builder = new ProductBuilder("N", "C", 1, 2, 3).SetBarcode("XYZ");
            var product = builder.Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1
            var result1 = await repo.ExistsByBarcodeAsync("XYZ");
            Assert.True(result1);

            // Remove from DB to force cache path
            db.Products.Remove(product);
            await db.SaveChangesAsync();

            // Act #2
            var result2 = await repo.ExistsByBarcodeAsync("XYZ");
            Assert.True(result2);
        }

        [Fact]
        public async Task ReturnsFalse_If_CacheHasFalse_RegardlessOfDb()
        {
            // Arrange: в кэше сразу false
            var db = CreateInMemoryDbContext();
            var builder = new ProductBuilder("A", "B", 1, 2, 3).SetBarcode("AAA");
            db.Products.Add(builder.Build());
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var key = "Exists_Barcode_AAA";
            cache.Set(key, false, new MemoryCacheEntryOptions { Size = 1 });

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act
            var result = await repo.ExistsByBarcodeAsync("AAA");
            Assert.False(result);
        }

        [Fact]
        public async Task Throws_InvalidOperationException_When_CacheFails()
        {
            var db = CreateInMemoryDbContext();
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new ThrowingCache());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.ExistsByBarcodeAsync("whatever"));
            Assert.Equal("cache fail", ex.Message);
        }

        [Fact]
        public async Task Throws_On_Cancellation()
        {
            var builder = new ProductBuilder("N", "C", 1, 2, 3).SetBarcode("T");
            var db = CreateInMemoryDbContext();
            db.Products.Add(builder.Build());
            await db.SaveChangesAsync();

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => repo.ExistsByBarcodeAsync("T", cts.Token));
        }

        [Fact]
        public async Task Throws_On_DbFailure()
        {
            // Мокаем DbSet<Product>, чтобы AnyAsync бросал
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
                () => repo.ExistsByBarcodeAsync("X"));
        }
    }
}
