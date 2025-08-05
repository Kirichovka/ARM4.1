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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class UpdateAsyncTests
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
                () => repo.UpdateAsync(null!));
        }

        [Fact]
        public async Task UpdatesProduct_InDatabase_And_ClearsCache()
        {
            // Arrange: создаём продукт
            var builder = new ProductBuilder("OldName", "Cat", 1m, 2m, 3)
                .SetBarcode("BC1")
                .SetId("ID1");
            var product = builder.Build();

            var db = CreateInMemoryDbContext();
            db.Products.Add(product);
            await db.SaveChangesAsync();

            // Модифицируем поля
            product.SetName("NewName");
            product.SetSalePrice(9m);

            var mockCache = new Mock<IMemoryCache>();
            mockCache.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                mockCache.Object);

            // Act
            await repo.UpdateAsync(product);

            // Assert: из БД читаем обновлённый продукт
            var fromDb = await db.Products.FirstAsync(p => EF.Property<string>(p, "id") == "ID1");
            Assert.Equal("NewName", fromDb.Name);
            Assert.Equal(9m, fromDb.GetSalePrice());

            // Assert: очищены ключи: AllProducts и все product-specific
            mockCache.Verify(c => c.Remove("AllProducts"), Times.Once);
            mockCache.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Product_Barcode_{product.GetBarcode()!.Value}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Products_Name_{product.GetName()}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Exists_Id_{product.GetId()}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Exists_Barcode_{product.GetBarcode()!.Value}"), Times.Once);
        }

        [Fact]
        public async Task ThrowsProductDomainException_And_DoesNotChangeDatabase()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domainEx = new ProductDomainException(
                ProductErrorCode.InvalidSalePrice,
                "negative",
                field: "SalePrice",
                value: -1m);
            var db = new ExceptionDbContext(opts, domainEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetId("X")
                .Build();

            var ex = await Assert.ThrowsAsync<ProductDomainException>(
                () => repo.UpdateAsync(product));
            Assert.Same(domainEx, ex);

            // БД пуста
            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsDomainException_And_DoesNotChangeDatabase()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domEx = new DomainException(
                "D",
                errorCode: "DomE",
                field: "F",
                value: "V");
            var db = new ExceptionDbContext(opts, domEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("N", "C", 1m, 2m, 3)
                .SetId("Y")
                .Build();

            var ex = await Assert.ThrowsAsync<DomainException>(
                () => repo.UpdateAsync(product));
            Assert.Same(domEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsGenericException_And_DoesNotChangeDatabase()
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
                .SetId("Z")
                .Build();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.UpdateAsync(product));
            Assert.Same(genEx, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }
        [Fact]
        public async Task UpdateAsync_LogsTrace_OnSuccess()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            var product = Product.CreateTestProduct();
            db.Products.Add(product);
            db.SaveChanges();

            // Меняем что-то у продукта
            product.SetQuantity(product.GetQuantity() + 1);

            await repo.UpdateAsync(product);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("UpdateAsync успешно завершён.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task UpdateAsync_LogsWarning_OnProductDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProductDomainException(ProductErrorCode.InvalidSalePrice, "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var product = Product.CreateTestProduct();

            await Assert.ThrowsAsync<ProductDomainException>(() => repo.UpdateAsync(product));

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ProductDomainException в UpdateAsync.")),
                    It.IsAny<ProductDomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task UpdateAsync_LogsError_OnDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("DomainFail", "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var product = Product.CreateTestProduct();

            await Assert.ThrowsAsync<DomainException>(() => repo.UpdateAsync(product));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DomainException в UpdateAsync.")),
                    It.IsAny<DomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task UpdateAsync_CallsCacheRemove_ForProduct()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()));

            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var product = Product.CreateTestProduct();
            db.Products.Add(product);
            db.SaveChanges();

            product.SetQuantity(product.GetQuantity() + 1);

            await repo.UpdateAsync(product);

            cacheMock.Verify(c => c.Remove(It.Is<object>(key =>
                key != null && key.ToString()!.Contains(product.GetId()))), Times.AtLeastOnce());
        }

    }
}
