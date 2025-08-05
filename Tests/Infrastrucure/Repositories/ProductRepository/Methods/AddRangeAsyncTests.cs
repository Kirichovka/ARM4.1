using ARM4.Domain.Builders;
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
    public class AddRangeAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(opts);
        }

        // Контекст, который бросает указанное исключение на SaveChangesAsync
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
                () => repo.AddRangeAsync(null!));
        }

        [Fact]
        public async Task AddsAllProducts_And_ClearsCacheForEach()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var products = new[]
            {
                new ProductBuilder("A","CA",1,2,3).SetId("ID1").SetBarcode("B1").Build(),
                new ProductBuilder("X","CB",4,5,6).SetId("ID2").SetBarcode("B2").Build(),
            };

            var mockCache = new Mock<IMemoryCache>();
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                mockCache.Object);

            // Act
            await repo.AddRangeAsync(products);

            // Assert: в БД оба продукта
            var all = await db.Products.ToListAsync();
            Assert.Equal(2, all.Count);

            // Очистка кэша — для каждого продукта должно вызвать Remove
            // ключи: AllProducts, Exists_Id_{id}, Exists_Barcode_{barcode}, Products_Name_{name}, Product_Id_{id}, Product_Barcode_{barcode}
            foreach (var p in products)
            {
                mockCache.Verify(c => c.Remove("AllProducts"), Times.Once);
                mockCache.Verify(c => c.Remove($"Exists_Id_{p.GetId()}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Exists_Barcode_{p.GetBarcode()!.Value}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Products_Name_{p.Name}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Product_Id_{p.GetId()}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Product_Barcode_{p.GetBarcode()!.Value}"), Times.Once);
            }
        }

        [Fact]
        public async Task ThrowsProductDomainException_And_NoPersistence()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domainEx = new ProductDomainException(
                ProductErrorCode.InvalidName,
                "bad name",
                field: "Name",
                value: "");
            var db = new ExceptionDbContext(opts, domainEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[] { new ProductBuilder("N", "C", 1, 2, 3).Build() };

            var ex = await Assert.ThrowsAsync<ProductDomainException>(
                () => repo.AddRangeAsync(products));
            Assert.Same(domainEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsDomainException_And_NoPersistence()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domEx = new DomainException(
                "domain error",
                errorCode: "DomErr",
                field: "Field",
                value: 42);
            var db = new ExceptionDbContext(opts, domEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[] { new ProductBuilder("N", "C", 1, 2, 3).Build() };

            var ex = await Assert.ThrowsAsync<DomainException>(
                () => repo.AddRangeAsync(products));
            Assert.Same(domEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsGenericException_And_NoPersistence()
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

            var products = new[] { new ProductBuilder("N", "C", 1, 2, 3).Build() };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.AddRangeAsync(products));
            Assert.Same(genEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }
        [Fact]
        public async Task AddRangeAsync_LogsTrace_OnSuccess()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            var products = new[]
            {
        Product.CreateTestProduct(),
        Product.CreateTestProduct()
    };

            await repo.AddRangeAsync(products);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("AddRangeAsync успешно завершён.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task AddRangeAsync_LogsWarning_OnProductDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProductDomainException(ProductErrorCode.InvalidSalePrice, "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var products = new[] { Product.CreateTestProduct() };

            await Assert.ThrowsAsync<ProductDomainException>(() => repo.AddRangeAsync(products));

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ProductDomainException в AddRangeAsync.")),
                    It.IsAny<ProductDomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task AddRangeAsync_LogsError_OnDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("DomainFail", "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var products = new[] { Product.CreateTestProduct() };

            await Assert.ThrowsAsync<DomainException>(() => repo.AddRangeAsync(products));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DomainException в AddRangeAsync.")),
                    It.IsAny<DomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task AddRangeAsync_CallsCacheRemove_ForEachProduct()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            // Mock MemoryCache — Remove должен быть вызван для каждого продукта
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = new[]
            {
        Product.CreateTestProduct(),
        Product.CreateTestProduct(),
        Product.CreateTestProduct()
    };

            // Act
            await repo.AddRangeAsync(products);

            // Assert: Remove должен быть вызван не менее одного раза на продукт
            foreach (var product in products)
            {
                // В зависимости от реализации может быть несколько ключей (например, Exists, Product, и т.д.)
                cacheMock.Verify(c => c.Remove(It.Is<object>(key =>
                    key != null && key.ToString()!.Contains(product.GetId()))), Times.AtLeastOnce());
            }
        }

    }
}
