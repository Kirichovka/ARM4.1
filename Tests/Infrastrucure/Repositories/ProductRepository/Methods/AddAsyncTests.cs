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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class AddAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(opts);
        }

        // Контекст, бросающий указанное исключение при SaveChangesAsync
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
                () => repo.AddAsync(null!));
        }

        [Fact]
        public async Task AddsProduct_And_ClearsAllRelevantCacheEntries()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3)
                .SetBarcode("BAR123")
                .SetId("ID123")
                .SetDisplayCode("DC123")
                .Build();

            // Mock cache to verify Remove calls
            var mockCache = new Mock<IMemoryCache>();
            mockCache.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                mockCache.Object);

            // Act
            await repo.AddAsync(product);

            // Assert: продукт в БД
            var fromDb = await db.Products
                .FirstOrDefaultAsync(p => EF.Property<string>(p, "id") == product.GetId());
            Assert.NotNull(fromDb);

            // Assert: Remove вызван как минимум для этих ключей
            mockCache.Verify(c => c.Remove("AllProducts"), Times.Once);
            mockCache.Verify(c => c.Remove($"Product_Id_{product.GetId()}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Product_Barcode_{product.GetBarcode()!.Value}"), Times.Once);
            mockCache.Verify(c => c.Remove($"Products_Name_{product.Name}"), Times.Once);
        }

        [Fact]
        public async Task ThrowsProductDomainException_And_DoesNotPersist()
        {
            // Arrange
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

            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3).Build();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ProductDomainException>(
                () => repo.AddAsync(product));
            Assert.Same(domainEx, ex);

            // В БД нет записей
            Assert.Equal(0, await db.Products.CountAsync());
        }

        [Fact]
        public async Task ThrowsDomainException_And_DoesNotPersist()
        {
            // Arrange
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var domEx = new DomainException(
                "domain error",
                errorCode: "DomErr",
                field: "Field",
                value: 123);
            var db = new ExceptionDbContext(opts, domEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3).Build();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<DomainException>(
                () => repo.AddAsync(product));
            Assert.Same(domEx, ex);

            Assert.Equal(0, await db.Products.CountAsync());
        }

        [Fact]
        public async Task ThrowsGenericException_And_DoesNotPersist()
        {
            // Arrange
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var genEx = new InvalidOperationException("boom");
            var db = new ExceptionDbContext(opts, genEx);
            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var product = new ProductBuilder("Name", "Cat", 1m, 2m, 3).Build();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.AddAsync(product));
            Assert.Same(genEx, ex);

            Assert.Equal(0, await db.Products.CountAsync());
        }
        [Fact]
        public async Task AddAsync_LogsTrace_OnSuccess()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var db = new ARM4DbContext(new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(db, loggerMock.Object, cache);

            var product = Product.CreateTestProduct();

            await repo.AddAsync(product);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("AddAsync успешно завершён.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task AddAsync_LogsWarning_OnProductDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            // Мокаем контекст так, чтобы SaveChangesAsync бросал ProductDomainException
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProductDomainException(ProductErrorCode.InvalidSalePrice, "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var product = Product.CreateTestProduct();

            await Assert.ThrowsAsync<ProductDomainException>(() => repo.AddAsync(product));

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ProductDomainException в AddAsync.")),
                    It.IsAny<ProductDomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task AddAsync_LogsError_OnDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            // Мокаем контекст так, чтобы SaveChangesAsync бросал DomainException
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("DomainFail", "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var product = Product.CreateTestProduct();

            await Assert.ThrowsAsync<DomainException>(() => repo.AddAsync(product));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DomainException в AddAsync.")),
                    It.IsAny<DomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }
}
