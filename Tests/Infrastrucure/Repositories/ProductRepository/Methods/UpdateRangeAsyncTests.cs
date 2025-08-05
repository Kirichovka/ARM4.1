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
    public class UpdateRangeAsyncTests
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
                () => repo.UpdateRangeAsync(null!));
        }

        [Fact]
        public async Task UpdatesAllProducts_And_ClearsCacheForEach()
        {
            // Arrange
            var products = new[]
            {
                new ProductBuilder("A","CatA",1,2,3).SetId("ID1").SetBarcode("B1").Build(),
                new ProductBuilder("B","CatB",4,5,6).SetId("ID2").SetBarcode("B2").Build(),
            };

            var db = CreateInMemoryDbContext();
            // Сначала добавляем «старые» версии
            db.Products.AddRange(products.Select(p => p).ToList());
            await db.SaveChangesAsync();

            // Мутируем передаваемый список
            products[0].SetName("A2");
            products[1].SetSalePrice(99m);

            var mockCache = new Mock<IMemoryCache>();
            mockCache.Setup(c => c.Remove(It.IsAny<object>()));

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                mockCache.Object);

            // Act
            await repo.UpdateRangeAsync(products);

            // Assert: из БД читаем обновлённые продукты
            var all = await db.Products.ToListAsync();
            Assert.Contains(all, p => p.GetId() == "ID1" && p.Name == "A2");
            Assert.Contains(all, p => p.GetId() == "ID2" && p.GetSalePrice() == 99m);

            // Assert: для каждого продукта кеш очищается
            foreach (var p in products)
            {
                mockCache.Verify(c => c.Remove("AllProducts"), Times.Once);
                mockCache.Verify(c => c.Remove($"Product_Id_{p.GetId()}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Product_Barcode_{p.GetBarcode()!.Value}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Products_Name_{p.Name}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Exists_Id_{p.GetId()}"), Times.Once);
                mockCache.Verify(c => c.Remove($"Exists_Barcode_{p.GetBarcode()!.Value}"), Times.Once);
            }
        }

        [Fact]
        public async Task ThrowsProductDomainException_And_NoPersistence()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var exDomain = new ProductDomainException(
                ProductErrorCode.InvalidName,
                "bad",
                field: "Name",
                value: "");
            var db = new ExceptionDbContext(opts, exDomain);

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[]
            {
                new ProductBuilder("A","Cat",1,2,3).Build()
            };

            var ex = await Assert.ThrowsAsync<ProductDomainException>(
                () => repo.UpdateRangeAsync(products));
            Assert.Same(exDomain, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsDomainException_And_NoPersistence()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var exDom = new DomainException(
                "d",
                errorCode: "DomErr",
                field: "F",
                value: "V");
            var db = new ExceptionDbContext(opts, exDom);

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[]
            {
                new ProductBuilder("A","Cat",1,2,3).Build()
            };

            var ex = await Assert.ThrowsAsync<DomainException>(
                () => repo.UpdateRangeAsync(products));
            Assert.Same(exDom, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }

        [Fact]
        public async Task ThrowsGenericException_And_NoPersistence()
        {
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var exGen = new InvalidOperationException("boom");
            var db = new ExceptionDbContext(opts, exGen);

            var repo = new ProductRepository(
                db,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new[]
            {
                new ProductBuilder("A","Cat",1,2,3).Build()
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.UpdateRangeAsync(products));
            Assert.Same(exGen, ex);

            Assert.Empty(await db.Products.ToListAsync());
        }
        [Fact]
        public async Task UpdateRangeAsync_LogsTrace_OnSuccess()
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
            db.Products.AddRange(products);
            db.SaveChanges();

            // Меняем количество у всех продуктов
            foreach (var p in products)
                p.SetQuantity(p.GetQuantity() + 1);

            await repo.UpdateRangeAsync(products);

            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("UpdateRangeAsync успешно завершён.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task UpdateRangeAsync_LogsWarning_OnProductDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProductDomainException(ProductErrorCode.InvalidSalePrice, "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var products = new[] { Product.CreateTestProduct() };

            await Assert.ThrowsAsync<ProductDomainException>(() => repo.UpdateRangeAsync(products));

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ProductDomainException в UpdateRangeAsync.")),
                    It.IsAny<ProductDomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task UpdateRangeAsync_LogsError_OnDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("DomainFail", "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var products = new[] { Product.CreateTestProduct() };

            await Assert.ThrowsAsync<DomainException>(() => repo.UpdateRangeAsync(products));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DomainException в UpdateRangeAsync.")),
                    It.IsAny<DomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task UpdateRangeAsync_CallsCacheRemove_ForEachProduct()
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

            foreach (var p in products)
                p.SetQuantity(p.GetQuantity() + 1);

            await repo.UpdateRangeAsync(products);

            foreach (var product in products)
            {
                cacheMock.Verify(c => c.Remove(It.Is<object>(key =>
                    key != null && key.ToString()!.Contains(product.GetId()))), Times.AtLeastOnce());
            }
        }
        [Fact]
        public async Task RemoveAsync_LogsTrace_OnSuccess()
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

            await repo.RemoveAsync(product);

            // Лог успешного завершения
            loggerMock.Verify(
                l => l.Log(LogLevel.Trace, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("RemoveAsync успешно завершён.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            // Очистка кэша по продукту
            cacheMock.Verify(c => c.Remove(It.Is<object>(key =>
                key != null && key.ToString()!.Contains(product.GetId()))), Times.AtLeastOnce());
        }
        [Fact]
        public async Task RemoveAsync_LogsWarning_OnProductDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProductDomainException(ProductErrorCode.InvalidSalePrice, "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var product = Product.CreateTestProduct();

            await Assert.ThrowsAsync<ProductDomainException>(() => repo.RemoveAsync(product));

            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ProductDomainException в RemoveAsync.")),
                    It.IsAny<ProductDomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
        [Fact]
        public async Task RemoveAsync_LogsError_OnDomainException()
        {
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

            var dbMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("DomainFail", "BadField", "BadValue"));

            var repo = new ProductRepository(dbMock.Object, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));
            var product = Product.CreateTestProduct();

            await Assert.ThrowsAsync<DomainException>(() => repo.RemoveAsync(product));

            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DomainException в RemoveAsync.")),
                    It.IsAny<DomainException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

    }
}
