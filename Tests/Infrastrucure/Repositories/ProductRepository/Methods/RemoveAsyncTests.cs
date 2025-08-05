using System;
using System.Threading;
using System.Threading.Tasks;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Domain.Builders;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.Entities;
using ARM4.Domain.Common;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class RemoveAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(opts);
        }

        // Контекст, бросающий указанное исключение на SaveChangesAsync
        private class ExceptionDbContext : ARM4DbContext
        {
            private readonly Exception _ex;
            public ExceptionDbContext(DbContextOptions<ARM4DbContext> opts, Exception ex)
                : base(opts) => _ex = ex;
            public override Task<int> SaveChangesAsync(CancellationToken ct = default)
                => Task.FromException<int>(_ex);
        }

        [Fact]
        public async Task ThrowsArgumentNull_When_ProductIsNull()
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.RemoveAsync(null!));
        }

        [Fact]
        public async Task RemovesProduct_FromDatabase_And_ClearsCache()
        {
            // Arrange
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3)
                .SetId("ID1")
                .SetBarcode("BC1")
                .Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var mockCache = new Mock<IMemoryCache>();
            mockCache.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                mockCache.Object);

            // Act
            await repo.RemoveAsync(product);

            // Assert: удалён из базы
            var exists = await db.Products.AnyAsync(p => EF.Property<string>(p, "id") == "ID1");
            Assert.False(exists);

            // Assert: очищены ключи кэша для этого продукта
            mockCache.Verify(c => c.Remove("AllProducts"), Times.Once);
            mockCache.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Product_Barcode_{product.GetBarcode()!.Value}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Products_Name_{product.Name}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Exists_Id_{product.GetId()}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Exists_Barcode_{product.GetBarcode()!.Value}"), Times.Once);
        }

        [Fact]
        public async Task ThrowsProductDomainException_And_DoesNotRemove()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domainEx = new ProductDomainException(
                ProductErrorCode.InvalidName,
                "bad",
                field: "Name",
                value: "");
            var db = new ExceptionDbContext(opts, domainEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetId("ID2")
                .Build();

            var ex = await Assert.ThrowsAsync<ProductDomainException>(
                () => repo.RemoveAsync(product));
            Assert.Same(domainEx, ex);

            // БД пуста (никаких изменений)
            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsDomainException_And_DoesNotRemove()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domEx = new DomainException(
                "Err",
                errorCode: "Code",
                field: "F",
                value: "V");
            var db = new ExceptionDbContext(opts, domEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetId("ID3")
                .Build();

            var ex = await Assert.ThrowsAsync<DomainException>(
                () => repo.RemoveAsync(product));
            Assert.Same(domEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsGenericException_And_DoesNotRemove()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var genEx = new InvalidOperationException("boom");
            var db = new ExceptionDbContext(opts, genEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetId("ID4")
                .Build();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.RemoveAsync(product));
            Assert.Same(genEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }
    }
}
