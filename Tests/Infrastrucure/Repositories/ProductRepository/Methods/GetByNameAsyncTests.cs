using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Domain.Entities;
using ARM4.Domain.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class GetByNameAsyncTests
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
        public async Task ReturnsEmptyList_When_NameNullOrWhitespace(string name)
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.GetByNameAsync(name);
            Assert.Empty(result);
        }

        [Fact]
        public async Task ReturnsEmptyList_When_NoMatches()
        {
            var db = CreateInMemoryDbContext();
            db.Products.Add(Product.CreateTestProduct());
            await db.SaveChangesAsync();

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var result = await repo.GetByNameAsync("nonexistent");
            Assert.Empty(result);
        }

        [Fact]
        public async Task ReturnsMatches_And_CachesResults()
        {
            var db = CreateInMemoryDbContext();
            var prod1 = new ProductBuilder("Apple", "Fruit", 1, 2, 10).Build();
            var prod2 = new ProductBuilder("Pineapple", "Fruit", 1, 2, 5).Build();
            db.Products.AddRange(prod1, prod2);
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            // Первый запрос: из БД + запись в кэше
            var first = await repo.GetByNameAsync("Apple");
            Assert.Equal(2, first.Count);

            // Удаляем из БД, чтобы убедиться, что второй запрос берёт из кэша
            db.Products.RemoveRange(db.Products);
            await db.SaveChangesAsync();

            // Второй запрос
            var second = await repo.GetByNameAsync("Apple");
            Assert.Equal(2, second.Count);
        }

        [Fact]
        public async Task ReturnsCachedList_IfExists()
        {
            var db = CreateInMemoryDbContext();
            var list = new List<Product> { new ProductBuilder("X", "Y", 1, 2, 3).Build() };
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5 });
            cache.Set("Products_Name_X", list, new MemoryCacheEntryOptions { Size = 1 });

            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);

            var result = await repo.GetByNameAsync("X");
            Assert.Same(list, result);
        }

        [Fact]
        public async Task Throws_InvalidOperationException_When_CacheFails()
        {
            var db = CreateInMemoryDbContext();
            var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), new ThrowingCache());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetByNameAsync("anything"));
            Assert.Equal("cache fail", ex.Message);
        }
    }
}
