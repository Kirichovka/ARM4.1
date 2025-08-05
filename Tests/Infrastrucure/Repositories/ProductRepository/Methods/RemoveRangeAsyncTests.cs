using ARM4.Domain.Builders;
using ARM4.Domain.Common;
using ARM4.Domain.DomainExceptions;
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class RemoveRangeAsyncTests
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
        public async Task ThrowsArgumentNull_When_ProductsNull()
        {
            var repo = new ProductRepository(
                CreateInMemoryDbContext(),
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.RemoveRangeAsync(null!));
        }

        [Fact]
        public async Task RemovesAllProducts_And_ClearsCacheForEach()
        {
            // Arrange
            var products = new[]
            {
                new ProductBuilder("A","CatA",1,2,3).SetId("ID1").SetBarcode("B1").Build(),
                new ProductBuilder("B","CatB",4,5,6).SetId("ID2").SetBarcode("B2").Build(),
            };

            var db = CreateInMemoryDbContext();
            db.Products.AddRange(products);
            await db.SaveChangesAsync();

            var mockCache = new Mock<IMemoryCache>();
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                mockCache.Object);

            // Act
            await repo.RemoveRangeAsync(products);

            // Assert: оба удалены из БД
            var remaining = await db.Products.ToListAsync();
            Assert.Empty(remaining);

            // Кэш очищен для каждого продукта
            foreach (var p in products)
            {
                mockCache.Verify(c => c.Remove("AllProducts"), Times.Once);
                mockCache.Verify(c => c.Remove($"Product_Id_{p.GetId()}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Product_Barcode_{p.GetBarcode()!.Value}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Products_{nameof(Product.Name)}_{p.Name}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Exists_Id_{p.GetId()}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Exists_Barcode_{p.GetBarcode()!.Value}"), Times.Once);
            }
        }

        [Fact]
        public async Task ThrowsProductDomainException_And_DoesNotRemove()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var pdEx = new ProductDomainException(
                ProductErrorCode.InvalidName,
                "bad",
                field: "Name",
                value: "");
            var db = new ExceptionDbContext(opts, pdEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[] { new ProductBuilder("A", "C", 1, 2, 3).Build() };

            var ex = await Assert.ThrowsAsync<ProductDomainException>(
                () => repo.RemoveRangeAsync(products));
            Assert.Same(pdEx, ex);

            // БД не изменилась
            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsDomainException_And_DoesNotRemove()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dEx = new DomainException(
                "Err",
                errorCode: "DomErr",
                field: "Field",
                value: 42);
            var db = new ExceptionDbContext(opts, dEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[] { new ProductBuilder("A", "C", 1, 2, 3).Build() };

            var ex = await Assert.ThrowsAsync<DomainException>(
                () => repo.RemoveRangeAsync(products));
            Assert.Same(dEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsGenericException_And_DoesNotRemove()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var gEx = new InvalidOperationException("boom");
            var db = new ExceptionDbContext(opts, gEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[] { new ProductBuilder("A", "C", 1, 2, 3).Build() };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.RemoveRangeAsync(products));
            Assert.Same(gEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }
        [Fact]
        public async Task RemoveRangeAsync_LogsTrace_OnSuccess()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = new[]
            {
        Product.CreateTestProduct(),
        Product.CreateTestProduct()
    };
            db.Products.AddRange(products);
            db.SaveChanges();

            await repo.RemoveRangeAsync(products);

            // Лог успешного завершения
            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("RemoveRangeAsync успешно завершён.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            // Очистка кэша по каждому продукту
            foreach (var product in products)
            {
                cacheMock.Verify(c => c.Remove(It.Is<object>(key =>
                    key != null && key.ToString()!.Contains(product.GetId()))), Times.AtLeastOnce());
            }
        }
        [Fact]
        public async Task RemoveRangeAsync_LogsWarning_OnProductDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProductDomainException(ProductErrorCode.InvalidSalePrice, "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var products = new[] { Product.CreateTestProduct() };

            await Assert.ThrowsAsync<ProductDomainException>(() => repo.RemoveRangeAsync(products));

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ProductDomainException в RemoveRangeAsync.")),
                    It.IsAny<ProductDomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task RemoveRangeAsync_LogsError_OnDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("DomainFail", "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var products = new[] { Product.CreateTestProduct() };

            await Assert.ThrowsAsync<DomainException>(() => repo.RemoveRangeAsync(products));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DomainException в RemoveRangeAsync.")),
                    It.IsAny<DomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

    }
}
