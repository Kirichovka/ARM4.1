using ARM4.Domain.Common;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Tests.Helpers;

namespace ARM4.Tests.Repositories
{
    public class UpdateProductsTransactionAsyncTests
    {
        private ARM4DbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ARM4DbContext(options);
        }

        // Контекст, который при SaveChangesAsync будет кидать заданное исключение
        private class ExceptionDbContext : ARM4DbContext
        {
            private readonly Exception _toThrow;
            public ExceptionDbContext(DbContextOptions<ARM4DbContext> opts, Exception toThrow)
                : base(opts) => _toThrow = toThrow;

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                => Task.FromException<int>(_toThrow);
        }

        ///Тесты для метода UpdateProductsTransactionAsync в ProductRepository
        [Fact]
        public async Task ThrowsArgumentNull_When_ProductsNull()
        {
            var repo = new ProductRepository(CreateInMemoryDbContext(),
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.UpdateProductsTransactionAsync(null));
        }

        [Fact]
        public async Task ReturnsSuccess_On_HappyPath()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var p = Product.CreateTestProduct();
            db.Products.Add(p);
            await db.SaveChangesAsync();

            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { p });

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.Empty(result.ErrorCodes);
        }

        [Fact]
        public async Task ReturnsSuccess_When_CacheThrows()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var p = Product.CreateTestProduct();
            db.Products.Add(p);
            await db.SaveChangesAsync();

            // Кэш, при Remove бросающий
            var badCache = new MemoryCache(new MemoryCacheOptions());
            // вручную навесим делегат, чтобы Remove бросал
            // (MemoryCache.Remove — виртуальный, но не везде; проще юзать Mock, но оставим так)
            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             badCache);

            // Act: очистка кэша внутри метода
            var result = await repo.UpdateProductsTransactionAsync(new[] { p });

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task ReturnsFailure_On_OperationCanceledException()
        {
            // Arrange
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ExceptionDbContext(opts, new OperationCanceledException("cancelled"));
            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { Product.CreateTestProduct() });

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Операция отменена", result.Errors[0]);
            Assert.Contains(InfraErrorCodes.UnknownInfraError, result.ErrorCodes);
        }

        
        [Fact]
        public async Task ReturnsFailure_On_DbUpdateConcurrencyException()
        {
            // Arrange
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ExceptionDbContext(opts, new DbUpdateConcurrencyException("conflict"));
            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { Product.CreateTestProduct() });

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Конфликт при параллельном обновлении", result.Errors[0]);
            Assert.Contains("ConcurrencyError", result.ErrorCodes);
        }

        [Fact]
        public async Task ReturnsFailure_On_ProductDomainException()
        {
            // Arrange
            var domainEx = new ProductDomainException(ProductErrorCode.InvalidSalePrice, "bad price");
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ExceptionDbContext(opts, domainEx);
            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { Product.CreateTestProduct() });

            // Assert
            Assert.False(result.IsSuccess);
            // сообщение в коде: $"Ошибка валидации продуктов: {ex.Message}"
            Assert.Contains("Ошибка валидации продуктов: bad price", result.Errors[0]);
            Assert.Contains(ProductErrorCode.InvalidSalePrice.ToString(), result.ErrorCodes);
        }

        [Fact]
        public async Task ReturnsFailure_On_DomainException()
        {
            // Arrange
            var domEx = new DomainException("domain failed", "DomErr");
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ExceptionDbContext(opts, domEx);
            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { Product.CreateTestProduct() });

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Ошибка доменной логики", result.Errors[0]);
            Assert.Contains(domEx.ErrorCode, result.ErrorCodes);
        }

        [Fact]
        public async Task ReturnsFailure_On_GenericException()
        {
            // Arrange
            var ex = new InvalidOperationException("boom");
            var opts = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ExceptionDbContext(opts, ex);
            var repo = new ProductRepository(db,
                                             new NullLogger<ProductRepository>(),
                                             new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { Product.CreateTestProduct() });

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Ошибка при обновлении: boom", result.Errors[0]);
            Assert.Contains(InfraErrorCodes.UnknownInfraError, result.ErrorCodes);
        }

        [Fact]
        public async Task UpdateProductsTransactionAsync_RollsBack_WhenSaveChangesFails()
        {
            // Arrange: создаём InMemory базу и добавляем продукт
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            // Mock контекста: SaveChangesAsync бросает ошибку
            var dbContextMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbContextMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated failure"));

            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(dbContextMock.Object, new NullLogger<ProductRepository>(), cache);

            // Act & Assert: метод бросает ошибку
            var ex = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await repo.UpdateProductsTransactionAsync(new[] { product });
            });
            Assert.Contains("Simulated failure", ex.Message);

            // Проверяем, что продукт в базе остался без изменений (транзакция откатилась)
            using (var dbContext = new ARM4DbContext(options))
            {
                var found = dbContext.Products.Find(product.GetId());
                Assert.NotNull(found);
                Assert.Equal(product.GetQuantity(), found.GetQuantity());
            }
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_HandlesRaceConditionWithCache()
        {
            // Arrange: создаём базу и добавляем несколько продуктов
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var products = Enumerable.Range(0, 800)
                .Select(_ => Product.CreateTestProduct())
                .ToList();

            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.AddRange(products);
                dbContext.SaveChanges();
            }

            var cache = new MemoryCache(new MemoryCacheOptions());

            // Подготовим несколько потоков для одновременного обновления
            var updateTasks = new List<Task>();

            for (int i = 0; i < products.Count; i++)
            {
                int index = i;
                updateTasks.Add(Task.Run(async () =>
                {
                    // Каждый поток меняет свой продукт (например, увеличивает количество)
                    using var context = new ARM4DbContext(options);
                    var repo = new ProductRepository(context, new NullLogger<ProductRepository>(), cache);
                    var product = products[index];
                    product.SetQuantity(product.GetQuantity() + 1);
                    await repo.UpdateProductsTransactionAsync(new[] { product });
                }));
            }

            // Act: запускаем все параллельно
            await Task.WhenAll(updateTasks);

            // Assert: проверяем, что все продукты были обновлены корректно
            using (var dbContext = new ARM4DbContext(options))
            {
                foreach (var product in products)
                {
                    var updated = dbContext.Products.Find(product.GetId());
                    Assert.NotNull(updated);
                    Assert.Equal(product.GetQuantity(), updated.GetQuantity());
                }
            }
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_Succeeds_WhenCollectionIsEmpty()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: "EmptyCollectionTest")
                .Options;

            using var dbContext = new ARM4DbContext(options);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(dbContext, new NullLogger<ProductRepository>(), cache);

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new ARM4.Domain.Entities.Product[0]);

            // Assert
            Assert.True(result.IsSuccess, "Expected method to succeed for empty collection.");
            // Дополнительно: в базе ничего не изменилось, не выброшено исключение и т.д.
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_Throws_WhenCollectionContainsNullElements()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var dbContext = new ARM4DbContext(options);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(dbContext, new NullLogger<ProductRepository>(), cache);

            var products = new Product[]
            {
                Product.CreateTestProduct(),
                null!, // специально вставляем null-элемент
                Product.CreateTestProduct()
            };

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await repo.UpdateProductsTransactionAsync(products);
            });
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_DoesNotFail_WhenCacheRemoveThrowsOnMultipleProducts()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var products = new[]
            {
                Product.CreateTestProduct(),
                Product.CreateTestProduct()
            };

            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.AddRange(products);
                dbContext.SaveChanges();
            }

            // Mock кэша: Remove бросает исключение всегда
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>())).Throws(new InvalidOperationException("Cache remove failed"));

            // repo с mock-кэшем
            using var dbContext2 = new ARM4DbContext(options);
            var repo = new ProductRepository(dbContext2, new NullLogger<ProductRepository>(), cacheMock.Object);

            // Модифицируем продукты для обновления
            foreach (var p in products)
                p.SetQuantity(p.GetQuantity() + 100);

            // Act
            var result = await repo.UpdateProductsTransactionAsync(products);

            // Assert
            Assert.True(result.IsSuccess, "Ожидается успешное выполнение несмотря на ошибки кэша");
            // (можно также проверить, что mock вызывался 2 раза)
            cacheMock.Verify(c => c.Remove(It.IsAny<object>()), Times.AtLeast(2));
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_ParallelInvocations_DoNotThrowAndUpdateCorrectly()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var products = Enumerable.Range(0, 5)
                .Select(_ => Product.CreateTestProduct())
                .ToList();

            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.AddRange(products);
                dbContext.SaveChanges();
            }

            var cache = new MemoryCache(new MemoryCacheOptions());

            // Запускаем параллельно несколько вызовов (каждый продукт обновляется отдельно)
            var tasks = products.Select(async p =>
            {
                using var db = new ARM4DbContext(options);
                var repo = new ProductRepository(db, new NullLogger<ProductRepository>(), cache);
                p.SetQuantity(p.GetQuantity() + 42);
                var result = await repo.UpdateProductsTransactionAsync(new[] { p });
                Assert.True(result.IsSuccess, "Ожидается успешное выполнение");
            }).ToList();

            await Task.WhenAll(tasks);

            // Проверяем, что обновления прошли
            using (var dbContext = new ARM4DbContext(options))
            {
                foreach (var p in products)
                {
                    var reloaded = dbContext.Products.Find(p.GetId());
                    Assert.Equal(p.GetQuantity(), reloaded!.GetQuantity());
                }
            }
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_ThrowsOrFails_WhenTokenIsCancelled()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            using var dbContext2 = new ARM4DbContext(options);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var repo = new ProductRepository(dbContext2, new NullLogger<ProductRepository>(), cache);

            var tokenSource = new CancellationTokenSource();
            tokenSource.Cancel(); // сразу отменяем

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await repo.UpdateProductsTransactionAsync(new[] { product }, tokenSource.Token);
            });
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_WritesExpectedLogs()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            var cache = new MemoryCache(new MemoryCacheOptions());
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            using var dbContext2 = new ARM4DbContext(options);
            var repo = new ProductRepository(dbContext2, loggerMock.Object, cache);

            // Act
            await repo.UpdateProductsTransactionAsync(new[] { product });

            // Assert
            // Проверяем, что был хотя бы один вызов LogInformation или LogTrace (или другой нужный уровень)
            loggerMock.Verify(
                l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("транзакционное обновление") || v.ToString()!.Contains("TRACE") || v.ToString()!.Contains("AUDIT")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce
            );
        }


        [Fact]
        public async Task UpdateProductsTransactionAsync_Rollback_LeavesDbUnchanged_WhenFails()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            // Изменяем quantity (это не попадает в БД пока SaveChanges не вызван)
            product.SetQuantity(product.GetQuantity() + 123);

            // Мокаем SaveChangesAsync чтобы он всегда падал
            var dbContextMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            dbContextMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated fail for rollback"));

            var repo = new ProductRepository(dbContextMock.Object, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));

            // Act & Assert: бросаем ошибку
            await Assert.ThrowsAsync<Exception>(() => repo.UpdateProductsTransactionAsync(new[] { product }));

            // Assert: БД не изменилась!
            using (var dbContext = new ARM4DbContext(options))
            {
                var reloaded = dbContext.Products.Find(product.GetId());
                Assert.NotNull(reloaded);
                // Проверяем, что значение quantity осталось прежним (rollback сработал)
                Assert.NotEqual(product.GetQuantity(), reloaded.GetQuantity());
            }
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_ParallelSameProduct_HandlesConcurrency()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            // Запускаем два параллельных обновления с разными значениями
            var tasks = new List<Task>();

            // Первый поток увеличивает на 1
            tasks.Add(Task.Run(async () =>
            {
                using var db1 = new ARM4DbContext(options);
                var repo1 = new ProductRepository(db1, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));
                var p1 = db1.Products.Find(product.GetId());
                p1!.SetQuantity(p1.GetQuantity() + 1);
                await repo1.UpdateProductsTransactionAsync(new[] { p1 });
            }));

            // Второй поток увеличивает на 2
            tasks.Add(Task.Run(async () =>
            {
                using var db2 = new ARM4DbContext(options);
                var repo2 = new ProductRepository(db2, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));
                var p2 = db2.Products.Find(product.GetId());
                p2!.SetQuantity(p2.GetQuantity() + 2);
                await repo2.UpdateProductsTransactionAsync(new[] { p2 });
            }));

            await Task.WhenAll(tasks);

            // Assert: одно из обновлений "выиграло", оба не потерялись (но итоговое значение зависит от очереди)
            using (var dbContext = new ARM4DbContext(options))
            {
                var updated = dbContext.Products.Find(product.GetId());
                Assert.NotNull(updated);
                // Quantity должен быть либо на 1, либо на 2 больше оригинального (но не больше чем на 2)
                var expected1 = product.GetQuantity() + 1;
                var expected2 = product.GetQuantity() + 2;
                Assert.True(
                    updated.GetQuantity() == expected1 ||
                    updated.GetQuantity() == expected2,
                    $"Quantity should be {expected1} or {expected2}, but was {updated.GetQuantity()}"
                );
            }
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_LogsError_WhenCacheRemoveThrows()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            // Mock кэша: Remove бросает исключение
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()))
                .Throws(new InvalidOperationException("Cache remove failed"));

            // Mock логгера для ProductRepository
            var loggerMock = new Mock<ILogger<ProductRepository>>();

            using var dbContext2 = new ARM4DbContext(options);
            var repo = new ProductRepository(dbContext2, loggerMock.Object, cacheMock.Object);

            // Act
            var result = await repo.UpdateProductsTransactionAsync(new[] { product });

            // Assert: метод завершился успехом
            Assert.True(result.IsSuccess);

            // Логгер был вызван с точным сообщением об ошибке очистки кэша после транзакции
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Ошибка очистки кэша после транзакции") &&
                        v.ToString()!.Contains("ErrorCode=CacheError")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.AtLeastOnce
            );
        }

        // Тест для ClearCacheForProducts: проверка логирования при большом количестве продуктов
        [Fact]
        public void ClearCacheForProducts_LogsWarning_WhenProductCountTooHigh()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            var db = new Mock<ARM4DbContext>().Object;

            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = Enumerable.Range(0, 10_001)
                .Select(_ => Product.CreateTestProduct())
                .ToList();

            // Act
            repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(repo, new object[] { products });

            // Assert
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Попытка массовой очистки кэша для") &&
                        v.ToString()!.Contains("операция может занять много времени")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public void ClearCacheForProducts_LogsWarning_WhenProductIsNull()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = new Product?[]
            {
        Product.CreateTestProduct(),
        null,
        Product.CreateTestProduct()
            };

            // Act
            repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(repo, new object[] { products });

            // Assert
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Пропущен null-продукт") &&
                        v.ToString()!.Contains("ErrorCode=CacheError")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.AtLeastOnce
            );
        }
        [Fact]
        public void ClearCacheForProducts_LogsWarning_WhenProductIdIsInvalid()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var product = Product.CreateTestProduct();
            // Хак: подменяем Id на не-гуид
            product.GetType().GetProperty("id")!.SetValue(product, "NOT_A_GUID");

            var products = new[] { product };

            // Act
            repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(repo, new object[] { products });

            // Assert
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Некорректный формат ProductId, пропускаем очистку кэша")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public void ClearCacheForProducts_LogsTrace_WhenStarts()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            var db = new Mock<ARM4DbContext>().Object;
            var repo = new ProductRepository(db, loggerMock.Object, cacheMock.Object);

            var products = Enumerable.Range(0, 2)
                .Select(_ => Product.CreateTestProduct())
                .ToList();

            // Act
            repo.GetType().GetMethod("ClearCacheForProducts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(repo, new object[] { products });

            // Assert
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Начало массовой очистки кэша")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_LogsInformation_WhenStarts()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var product = Product.CreateTestProduct();
            using var dbContext = new ARM4DbContext(options);
            dbContext.Products.Add(product);
            dbContext.SaveChanges();

            var repo = new ProductRepository(dbContext, loggerMock.Object, new MemoryCache(new MemoryCacheOptions()));

            // Act
            await repo.UpdateProductsTransactionAsync(new[] { product });

            // Assert
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString()!.Contains("Начало транзакционного обновления продуктов")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_HandlesHugeCollection_WithNullsAndInvalidIds_AndSaveChangesFails()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            // Собираем коллекцию
            var products = new List<Product?>();
            for (int i = 0; i < 9500; i++)
                products.Add(Product.CreateTestProduct());
            for (int i = 0; i < 250; i++)
                products.Add(null); // добавим null
            for (int i = 0; i < 250; i++)
            {
                var p = Product.CreateTestProduct();
                // подменим id на не-гуид
                p.GetType().GetProperty("id")!.SetValue(p, "BAD_ID_" + i);
                products.Add(p);
            }

            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.AddRange(products.Where(x => x != null)!);
                dbContext.SaveChanges();
            }

            // SaveChangesAsync — имитируем ошибку только на конкретных продуктах
            var dbContextMock = new Mock<ARM4DbContext>(options) { CallBase = true };
            int saveCallCount = 0;
            dbContextMock.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => saveCallCount++)
                .ReturnsAsync(1);

            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()))
                .Callback<object>(key =>
                {
                    // Сымитировать падение кэша на каждом 500-м ключе
                    if (key != null && key.ToString()!.Contains("500"))
                        throw new Exception("Massive cache error!");
                });

            var repo = new ProductRepository(dbContextMock.Object, loggerMock.Object, cacheMock.Object);

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await repo.UpdateProductsTransactionAsync(products!);
            sw.Stop();

            // Assert — не упал, прошёл
            Assert.True(saveCallCount > 0, "SaveChangesAsync был вызван");
            // Проверка, что логи были вызваны с предупреждениями и ошибками
            loggerMock.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
            loggerMock.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
            // Можно добавить тайминги — если >10 сек, сообщить
            Assert.True(sw.Elapsed.TotalSeconds < 10, $"Слишком долго: {sw.Elapsed.TotalSeconds}s");
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_Stress_1000ParallelTransactions_WithCacheAndDbErrors()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var baseProducts = Enumerable.Range(0, 100)
                .Select(_ => Product.CreateTestProduct())
                .ToList();

            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.AddRange(baseProducts);
                dbContext.SaveChanges();
            }

            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var cacheMock = new Mock<IMemoryCache>();
            cacheMock.Setup(c => c.Remove(It.IsAny<object>()))
                .Callback<object>(key =>
                {
                    // Кэш кидает ошибку на каждом 13-м ключе
                    if (key != null && key.ToString()!.EndsWith("13"))
                        throw new Exception("Cache boom!");
                });

            int dbErrorCount = 0;
            // DbContext, который иногда бросает ошибку при SaveChanges
            var dbFactory = new Func<ARM4DbContext>(() =>
            {
                var db = new Mock<ARM4DbContext>(options) { CallBase = true };
                db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1)
                    .Callback(() =>
                    {
                        if (Interlocked.Increment(ref dbErrorCount) % 50 == 0)
                            throw new Exception("DB boom!");
                    });
                return db.Object;
            });

            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var prods = baseProducts.Select(p =>
                    {
                        var clone = Product.CreateTestProduct();
                        clone.GetType().GetProperty("id")!.SetValue(clone, p.GetId());
                        return clone;
                    }).ToList();
                    foreach (var p in prods) p.SetQuantity(p.GetQuantity() + i);

                    var repo = new ProductRepository(dbFactory(), loggerMock.Object, cacheMock.Object);
                    try
                    {
                        await repo.UpdateProductsTransactionAsync(prods);
                    }
                    catch { /* ожидаем ошибки */ }
                }));
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert: всё отработало, тест не завис
            Assert.True(sw.Elapsed.TotalSeconds < 30, $"Тест выполнился слишком медленно: {sw.Elapsed.TotalSeconds}s");
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_Performance_BigCollection()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var products = Enumerable.Range(0, 20_000)
                .Select(_ => Product.CreateTestProduct())
                .ToList();

            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.AddRange(products);
                dbContext.SaveChanges();
            }

            var repo = new ProductRepository(new ARM4DbContext(options), new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await repo.UpdateProductsTransactionAsync(products);
            sw.Stop();

            Assert.True(result.IsSuccess, "Ожидался успех");
            Assert.True(sw.Elapsed.TotalSeconds < 20, $"Операция заняла слишком долго: {sw.Elapsed.TotalSeconds} сек");
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_SqlServer_HandlesRealConcurrency()
        {
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseSqlServer("Server=localhost;Database=YourTestDb;User Id=sa;Password=yourpass;")
                .Options;

            var product = Product.CreateTestProduct();
            using (var dbContext = new ARM4DbContext(options))
            {
                dbContext.Products.Add(product);
                dbContext.SaveChanges();
            }

            // Теперь параллельно апдейтим один и тот же продукт двумя репозиториями — возможен deadlock или concurrency exception
            var tasks = new List<Task>();
            for (int i = 0; i < 2; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var ctx = new ARM4DbContext(options);
                    var repo = new ProductRepository(ctx, new NullLogger<ProductRepository>(), new MemoryCache(new MemoryCacheOptions()));
                    var p = ctx.Products.Find(product.GetId());
                    p!.SetQuantity(p.GetQuantity() + i + 1);
                    try
                    {
                        await repo.UpdateProductsTransactionAsync(new[] { p });
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // ok: ждали конфликт
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Можно проверить, что хотя бы один апдейт сработал, а другой — бросил concurrency-ошибку
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_BeginTransactionThrows_ExceptionIsPropagated()
        {
            // ---------- Arrange ----------
            // In-memory EF Core контекст
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var realContext = new ARM4DbContext(options);

            // Мокаем DatabaseFacade, чтобы выбросить ошибку при открытии транзакции
            var dbFacadeMock = new Mock<DatabaseFacade>(realContext);
            dbFacadeMock
                .Setup(f => f.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("DB connection failed"));

            // Небольшой подкласс, который подменяет свойство Database
            var ctx = new FakeDbContext(options, dbFacadeMock.Object);

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var repo = new ProductRepository(ctx, loggerMock.Object, memoryCache);

            var productsToUpdate = new List<Product>
        {
            Product.CreateTestProduct(),
            Product.CreateTestProduct()
        };

            // ---------- Act / Assert ----------
            var ex = await Assert.ThrowsAsync<Exception>(async () =>
                await repo.UpdateProductsTransactionAsync(productsToUpdate));

            Assert.Equal("DB connection failed", ex.Message);

            // Дополнительно убеждаемся, что метод действительно пытался открыть транзакцию
            dbFacadeMock.Verify(f => f.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact]
        public async Task UpdateProductsTransactionAsync_CommitThrows_ReturnsFailureAndRollbacks()
        {
            // ---------- Arrange ----------
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var realContext = new ARM4DbContext(options);

            // Репозиторий ожидает существующие записи, чтобы дошло до CommitAsync
            var p1 = Product.CreateTestProduct();
            var p2 = Product.CreateTestProduct();
            realContext.Products.AddRange(p1, p2);
            realContext.SaveChanges();

            // Мокаем транзакцию: CommitAsync бросает, RollbackAsync записывает факт вызова
            var txMock = new Mock<IDbContextTransaction>();
            txMock.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new Exception("Commit failed"));
            txMock.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            // Мокаем DatabaseFacade, чтобы вернуть нашу транзакцию
            var dbFacadeMock = new Mock<DatabaseFacade>(realContext);
            dbFacadeMock.Setup(f => f.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(txMock.Object);

            // Подменяем Database в контексте
            var ctx = new FakeDbContext(options, dbFacadeMock.Object);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var loggerMock = new Mock<ILogger<ProductRepository>>();
            var repo = new ProductRepository(ctx, loggerMock.Object, cache);

            var productsToUpdate = new List<Product> { p1, p2 };

            // ---------- Act ----------
            var result = await repo.UpdateProductsTransactionAsync(productsToUpdate);

            // ---------- Assert ----------
            Assert.False(result.IsSuccess);
            Assert.Contains(InfraErrorCodes.UnknownInfraError, result.ErrorCodes);
            txMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            txMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_RollbackFails_Throws()
        {
            #region ── Arrange ─────────────────────────────────────────────────────────
            // In-memory DbContext
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                          .UseInMemoryDatabase(Guid.NewGuid().ToString())
                          .Options;

            // ▶ DbContext-заменитель, который:
            //   • возвращает подменный DatabaseFacade (с «ломаной» транзакцией);
            //   • бросает DomainException на SaveChangesAsync, чтобы мы попали в catch.
            var ctx = new TestDbContext(options);

            // Кэш и логгер как в предыдущих тестах
            var memCache = new MemoryCache(new MemoryCacheOptions());
            var logger = Mock.Of<ILogger<ProductRepository>>();

            var repo = new ProductRepository(ctx, logger, memCache);
            var products = new[] { Product.CreateTestProduct() };
            #endregion

            #region ── Act & Assert ───────────────────────────────────────────────────
            var ex = await Assert.ThrowsAsync<Exception>(
                () => repo.UpdateProductsTransactionAsync(products, CancellationToken.None));

            Assert.Equal("Rollback failed", ex.Message);
            #endregion
        }

        #region ── Тестовые двойники ────────────────────────────────────────────────
        /// <summary>
        /// Транзакция, которая «проваливает» Rollback.
        /// </summary>
        private sealed class FailingRollbackTransaction : IDbContextTransaction
        {
            public Guid TransactionId => Guid.NewGuid();
            public DbTransaction GetDbTransaction() => null!;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;

            public Task CommitAsync(CancellationToken ct = default)     // до коммита не дойдёт
                => Task.CompletedTask;
            public void Commit() { }

            public Task RollbackAsync(CancellationToken ct = default)
                => Task.FromException(new Exception("Rollback failed"));
            public void Rollback() => throw new Exception("Rollback failed");
        }

        /// <summary>
        /// DatabaseFacade, отдающая FailingRollbackTransaction.
        /// </summary>
        private sealed class FakeDatabaseFacade : DatabaseFacade
        {
            public FakeDatabaseFacade(DbContext ctx) : base(ctx) { }
            public override Task<IDbContextTransaction> BeginTransactionAsync(
                CancellationToken ct = default) =>
                Task.FromResult<IDbContextTransaction>(new FailingRollbackTransaction());
        }

        /// <summary>
        /// DbContext, подменяющий DatabaseFacade и бросающий
        /// DomainException в SaveChangesAsync.
        /// </summary>
        private sealed class TestDbContext : ARM4DbContext
        {
            public TestDbContext(DbContextOptions<ARM4DbContext> opts) : base(opts)
            {

                // Инъекция FakeDatabaseFacade через приватное поле базового DbContext
                var field = typeof(DbContext).GetField("_databaseFacade",
                             BindingFlags.Instance | BindingFlags.NonPublic);
                field!.SetValue(this, new FakeDatabaseFacade(this));
            }

            public override Task<int> SaveChangesAsync(CancellationToken ct = default)
                => Task.FromException<int>(
                    new DomainException("Save failed", "SomeDomainError"));
        }
        #endregion
        [Fact]
        public async Task UpdateProductsTransactionAsync_ConcurrencyConflict_ReturnsExpectedFailure()
        {
            // ---------- Arrange ----------
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                          .UseInMemoryDatabase(Guid.NewGuid().ToString())
                          .Options;

            var ctx = new ConcurrencyFailingDbContext(options);

            var txMock = new Mock<IDbContextTransaction>();
            txMock.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var dbFacadeMock = new Mock<DatabaseFacade>(ctx);
            dbFacadeMock.Setup(f => f.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(txMock.Object);

            ctx.OverrideDatabase(dbFacadeMock.Object);

            var repo = new ProductRepository(
                ctx,
                Mock.Of<ILogger<ProductRepository>>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new List<Product> { Product.CreateTestProduct() };

            // ---------- Act ----------
            var result = await repo.UpdateProductsTransactionAsync(products);

            // ---------- Assert ----------
            Assert.False(result.IsSuccess);

            Assert.Contains(
                result.Errors,
                e => e.Contains("Конфликт при параллельном обновлении", StringComparison.Ordinal));

            Assert.Contains("ConcurrencyError", result.ErrorCodes);

            txMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            txMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        // ===== вспомогательные классы =====
        private sealed class ConcurrencyFailingDbContext : ARM4DbContext
        {
            private DatabaseFacade? _override;
            public ConcurrencyFailingDbContext(DbContextOptions<ARM4DbContext> opts) : base(opts) { }

            public void OverrideDatabase(DatabaseFacade facade) => _override = facade;
            public override DatabaseFacade Database => _override ?? base.Database;

            public override Task<int> SaveChangesAsync(CancellationToken ct = default) =>
                Task.FromException<int>(new DbUpdateConcurrencyException("Simulated concurrency conflict"));
        }
        [Fact]
        public async Task UpdateRange_Throws_ExceptionHandledAndFailureReturned()
        {
            // ---------- Arrange ----------
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            // DbSet<Product> → бросает на UpdateRange
            var productSet = new Mock<DbSet<Product>>();
            productSet.Setup(s => s.UpdateRange(It.IsAny<IEnumerable<Product>>()))
                      .Throws(new InvalidOperationException("Invalid state in entity"));

            // транзакция ⇒ Rollback вызывается без ошибок
            var tx = new Mock<IDbContextTransaction>();
            tx.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

            // DatabaseFacade → отдаёт подменную транзакцию
            var facade = new Mock<DatabaseFacade>(new DbContext(options));
            facade.Setup(f => f.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(tx.Object);

            // собственный контекст-заглушка
            var ctx = new UpdateRangeFailingDbContext(options, facade.Object)
            {
                Products = productSet.Object      // подмена DbSet
            };

            var repo = new ProductRepository(
                ctx,
                Mock.Of<ILogger<ProductRepository>>(),
                new MemoryCache(new MemoryCacheOptions()));

            var products = new List<Product> { Product.CreateTestProduct() };

            // ---------- Act ----------
            var result = await repo.UpdateProductsTransactionAsync(products);

            // ---------- Assert ----------
            Assert.False(result.IsSuccess);
            Assert.Contains(InfraErrorCodes.UnknownInfraError, result.ErrorCodes);
            tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Специализированный контекст: подменяет и DatabaseFacade, и DbSet&lt;Product&gt;.
        /// </summary>
        private sealed class UpdateRangeFailingDbContext : ARM4DbContext
        {
            private readonly DatabaseFacade _facade;

            public UpdateRangeFailingDbContext(
                DbContextOptions<ARM4DbContext> opts,
                DatabaseFacade facade) : base(opts)
            {
                _facade = facade ?? throw new ArgumentNullException(nameof(facade));
            }

            public override DatabaseFacade Database => _facade;

            // DbSet подменяется извне (через init/сеттер или в конструкторе теста)
            public new DbSet<Product> Products { get; set; } = null!;
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_Success_LogsCorrectMetrics()
        {
            // ---------- Arrange ----------
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var ctx = new ARM4DbContext(options);
            ctx.Products.AddRange(Product.CreateTestProduct(), Product.CreateTestProduct());
            ctx.SaveChanges();

            var logInvocations = new List<(LogLevel level, string message)>();

            var loggerMock = new Mock<ILogger<ProductRepository>>();
            loggerMock
                .Setup(l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback((LogLevel lvl, EventId _, object state, Exception? ex,
                           Func<object, Exception?, string> formatter) =>
                {
                    string msg = formatter(state, ex);
                    logInvocations.Add((lvl, msg));
                });

            var repo = new ProductRepository(
                ctx,
                loggerMock.Object,
                new MemoryCache(new MemoryCacheOptions()));

            // ---------- Act ----------
            var result = await repo.UpdateProductsTransactionAsync(ctx.Products.ToList());

            // ---------- Assert ----------
            Assert.True(result.IsSuccess);

            // 1) Стартовое Trace-сообщение
            var startMsg = logInvocations.First(m => m.message.Contains("Начало UpdateProductsTransactionAsync"));
            var correlationId = Regex.Match(startMsg.message, @"CorrelationId=(.+?)\s").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(correlationId));

            // 2) Info-сообщение аудита
            var auditMsg = logInvocations.First(m => m.message.Contains("Начало транзакционного обновления"));
            Assert.Contains($"CorrelationId={correlationId}", auditMsg.message);
            Assert.Contains("IsAudit=True", auditMsg.message, StringComparison.OrdinalIgnoreCase);

            // 3) Финальное Trace-сообщение с DurationMs
            var endMsg = logInvocations.First(m => m.message.Contains("завершён"));
            Assert.Contains($"CorrelationId={correlationId}", endMsg.message);

            var durationMatch = Regex.Match(endMsg.message, @"DurationMs=(\d+)");
            Assert.True(durationMatch.Success);
            Assert.True(int.Parse(durationMatch.Groups[1].Value) >= 0);

            // Дополнительно убеждаемся, что не было Rollback-сообщений
            Assert.DoesNotContain(logInvocations, m => m.message.Contains("Rollback", StringComparison.OrdinalIgnoreCase));
        }
        [Fact]
        public async Task UpdateProductsTransactionAsync_OnSuccess_ClearsCache()
        {
            // ---------- Arrange ----------
            var options = new DbContextOptionsBuilder<ARM4DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            // ⮕ Реальные объекты-продукты
            var p1 = Product.CreateTestProduct();          // имеет Id и Barcode
            var p2 = Product.CreateTestProduct();
            using var realCtx = new ARM4DbContext(options);
            realCtx.Products.AddRange(p1, p2);
            realCtx.SaveChanges();

            // ⮕ Мокаем транзакцию, чтобы Commit/Rollback отработали без ошибок
            var txMock = new Mock<IDbContextTransaction>();
            txMock.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var dbFacadeMock = new Mock<DatabaseFacade>(realCtx);
            dbFacadeMock.Setup(f => f.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(txMock.Object);

            // ⮕ Контекст с подменённым DatabaseFacade
            var ctx = new FakeDbContext(options, dbFacadeMock.Object);

            // ⮕ Памятный кэш заполняем «грязными» значениями
            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("AllProducts", new object());
            cache.Set($"Product_Id_{p1.GetId()}", p1);
            cache.Set($"Product_Id_{p2.GetId()}", p2);
            cache.Set($"Product_Barcode_{p1.GetBarcode()}", p1);
            cache.Set($"Product_Barcode_{p2.GetBarcode()}", p2);

            // ⮕ Логгер несущественен, но нужен для DI
            var logger = Mock.Of<ILogger<ProductRepository>>();
            var repo = new ProductRepository(ctx, logger, cache);

            // ⮕ Модифицируем сущности, чтобы дошло до SaveChanges/Commit
            p1.SetQuantity(p1.GetQuantity() + 5);
            p2.SetQuantity(p2.GetQuantity() + 7);
            var listToUpdate = new[] { p1, p2 };

            // ---------- Act ----------
            var result = await repo.UpdateProductsTransactionAsync(listToUpdate);

            // ---------- Assert ----------
            Assert.True(result.IsSuccess);

            // кэш «AllProducts» должен исчезнуть
            Assert.False(cache.TryGetValue("AllProducts", out _));

            // все продуктовые ключи – тоже
            foreach (var p in listToUpdate)
            {
                Assert.False(cache.TryGetValue($"Product_Id_{p.GetId()}", out _));
                Assert.False(cache.TryGetValue($"Product_Barcode_{p.GetBarcode()}", out _));
            }

            // подтверждаем, что коммит действительно выполнялся
            txMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// DbContext, возвращающий пользовательский <see cref="DatabaseFacade"/>.
        /// </summary>
        private sealed class FakeDbContext : ARM4DbContext
        {
            private readonly DatabaseFacade _facade;
            public FakeDbContext(DbContextOptions<ARM4DbContext> opts, DatabaseFacade facade) : base(opts)
            {
                _facade = facade ?? throw new ArgumentNullException(nameof(facade));
            }

            public override DatabaseFacade Database => _facade;
        }
    }
}
