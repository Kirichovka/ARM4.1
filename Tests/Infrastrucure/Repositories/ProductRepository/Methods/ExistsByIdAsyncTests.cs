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
    public class ExistsByIdAsyncTests
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
        public async Task ReturnsFalse_When_IdNullOrWhitespace(string id)
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.ExistsByIdAsync(id);
            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsFalse_When_NotFoundInDb_And_Caches()
        {
            var db = CreateInMemoryDbContext();
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1: DB miss, caches false
            var r1 = await repo.ExistsByIdAsync("nonexistent");
            Assert.False(r1);

            // Act #2: cache hit
            var r2 = await repo.ExistsByIdAsync("nonexistent");
            Assert.False(r2);
        }

        [Fact]
        public async Task ReturnsTrue_When_FoundInDb_And_Caches()
        {
            var builder = new ProductBuilder("Name", "Cat", 1, 2, 3)
                .SetId(Guid.NewGuid().ToString());
            var product = builder.Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1
            var r1 = await repo.ExistsByIdAsync(product.GetId());
            Assert.True(r1);

            // Remove from DB, to force cache hit
            db.Products.Remove(product);
            await db.SaveChangesAsync();

            // Act #2
            var r2 = await repo.ExistsByIdAsync(product.GetId());
            Assert.True(r2);
        }

        [Fact]
        public async Task ReturnsFalse_If_CacheHasFalse_RegardlessOfDb()
        {
            var builder = new ProductBuilder("N", "C", 1, 2, 3)
                .SetId("ID123");
            var product = builder.Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var key = $"Exists_Id_{product.GetId()}";
            cache.Set(key, false, new MemoryCacheEntryOptions { Size = 1 });

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            var result = await repo.ExistsByIdAsync(product.GetId());
            Assert.False(result);
        }

        [Fact]
        public async Task Throws_InvalidOperationException_When_CacheFails()
        {
            var db = CreateInMemoryDbContext();
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new ThrowingCache());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.ExistsByIdAsync("anything"));
            Assert.Equal("cache fail", ex.Message);
        }

        [Fact]
        public async Task Throws_On_Cancellation()
        {
            var builder = new ProductBuilder("N", "C", 1, 2, 3)
                .SetId("ID456");
            var product = builder.Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => repo.ExistsByIdAsync(product.GetId(), cts.Token));
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
                () => repo.ExistsByIdAsync("ID789"));
        }
    }
}
