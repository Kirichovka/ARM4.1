using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class ProductRepositoryConstructorTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(options);
        }

        [Fact]
        public async Task Ctor_WithNullDb_ThrowsArgumentNullException()
        {
            // Arrange
            ARM4DbContext? db = null;
            var logger = new NullLogger<ProductRepository>();
            var cache = new MemoryCache(new MemoryCacheOptions());

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                _ = new ProductRepository(db!, logger, cache);
                await Task.CompletedTask;
            });
        }

        [Fact]
        public async Task Ctor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            ILogger<ProductRepository>? logger = null;
            var cache = new MemoryCache(new MemoryCacheOptions());

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                _ = new ProductRepository(db, logger!, cache);
                await Task.CompletedTask;
            });
        }

        [Fact]
        public async Task Ctor_WithNullCache_ThrowsArgumentNullException()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var logger = new NullLogger<ProductRepository>();
            IMemoryCache? cache = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                _ = new ProductRepository(db, logger, cache!);
                await Task.CompletedTask;
            });
        }

        [Fact]
        public async Task Ctor_WithAllDependencies_CreatesInstance_And_GetAllAsyncReturnsEmpty()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var logger = new NullLogger<ProductRepository>();
            var cache = new MemoryCache(new MemoryCacheOptions());

            // Act
            var repo = new ProductRepository(db, logger, cache);

            // Assert
            Assert.NotNull(repo);

            // Проверяем, что GetAllAsync работает асинхронно и возвращает пустую коллекцию на пустой БД
            var products = await repo.GetAllAsync();
            Assert.Empty(products);
        }
    }
}
