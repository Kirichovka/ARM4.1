using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class GetAllAsyncTests
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

        [Fact]
        public async Task ReturnsFromDb_And_Caches_When_CacheEmpty()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var p1 = Product.CreateTestProduct();
            var p2 = Product.CreateTestProduct();
            db.Products.AddRange(p1, p2);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act #1: cache miss
            var list1 = await repo.GetAllAsync();

            // Assert #1
            Assert.Equal(2, list1.Count);

            // Act #2: cache hit (теперь в cache)
            db.Products.RemoveRange(db.Products);
            await db.SaveChangesAsync();
            var list2 = await repo.GetAllAsync();

            // Assert #2
            Assert.Equal(2, list2.Count);
        }

        [Fact]
        public async Task ReturnsCachedList_IfExists()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            // не добавляем ничего в БД
            var cached = new List<Product> { Product.CreateTestProduct() };
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            cache.Set("AllProducts", cached, new MemoryCacheEntryOptions { Size = 1 });

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Act
            var result = await repo.GetAllAsync();

            // Assert
            Assert.Same(cached, result);
        }

        [Fact]
        public async Task Throws_InvalidOperationException_When_CacheFails()
        {
            var db = CreateInMemoryDbContext();
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new ThrowingCache());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetAllAsync());
            Assert.Equal("cache fail", ex.Message);
        }

        [Fact]
        public async Task Throws_On_DbFailure()
        {
            // Arrange: мокаем DbSet<Product>, чтобы ToListAsync бросал
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

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetAllAsync());
            Assert.Equal("db fail", ex.Message);
        }

        [Fact]
        public async Task Throws_On_Cancellation()
        {
            var db = CreateInMemoryDbContext();
            db.Products.Add(Product.CreateTestProduct());
            await db.SaveChangesAsync();

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => repo.GetAllAsync(cts.Token));
        }
    }
}
