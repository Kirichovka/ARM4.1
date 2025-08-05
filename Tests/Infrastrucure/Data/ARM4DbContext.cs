using System;
using System.Threading.Tasks;
using ARM4.Domain.Builders;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ARM4.Tests.Infrastructure
{
    public class ARM4DbContextTests : IDisposable
    {
        private readonly ARM4DbContext _db;

        public ARM4DbContextTests()
        {
            // отдельная InMemory-БД на каждый тест
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                          .UseInMemoryDatabase(Guid.NewGuid().ToString())
                          .Options;

            _db = new ARM4DbContext(options);
            _db.Database.EnsureCreated();
        }

        public void Dispose() => _db.Dispose();

        // ---------- helper ----------

        private static Product BuildSample()
        {
            return new ProductBuilder("Sample", "Cat", 5m, 10m, 1)
                   .Build();
        }

        // ---------- тесты ----------

        [Fact]
        public void OnModelCreating_AppliesProductConfiguration()
        {
            var entity = _db.Model.FindEntityType(typeof(Product))
                         ?? throw new InvalidOperationException("Product entity not mapped");

            // Имя таблицы
            Assert.Equal("Products", entity.GetTableName());

            // RowVersion — concurrency-token
            var rv = entity.FindProperty("RowVersion")
                     ?? throw new InvalidOperationException("RowVersion not mapped");
            Assert.True(rv.IsConcurrencyToken);
        }

        [Fact]
        public async Task CanAddAndRetrieve_Product()
        {
            var p = BuildSample();

            await _db.Products.AddAsync(p);
            await _db.SaveChangesAsync();

            var fetched = await _db.Products
                                   .SingleAsync(x => x.GetId() == p.GetId());

            Assert.Equal(p.Name, fetched.Name);
            Assert.Equal(p.GetQuantity(), fetched.GetQuantity());
        }
    }
}
