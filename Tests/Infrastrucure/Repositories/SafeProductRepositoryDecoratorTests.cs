using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARM4.Domain.Common;
using ARM4.Domain.Entities;
using ARM4.Domain.Interfaces;
using ARM4.Infrastructure.Repositories;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ARM4.Tests.Infrastructure
{
    public class SafeProductRepositoryDecoratorTests
    {
        private readonly Mock<IProductRepository> _inner = new();
        private readonly Mock<ILogger<IProductRepository>> _logger = new();

        private SafeProductRepositoryDecorator CreateSut() =>
            new(_inner.Object, _logger.Object);

        // ---------- helpers ----------

        private static List<Product> List(params string[] ids)
        {
            var rnd = new Random();
            return ids.Select(id =>
            {
                var p = Product.CreateTestProduct();
                p.SetQuantity(rnd.Next(1, 10));
                return p;
            }).ToList();
        }

        private static OperationResult Ok =>
            OperationResult.Success();

        // ---------- GetAllAsync ----------

        [Fact]
        public async Task GetAllAsync_ReturnsData_WhenInnerSucceeds()
        {
            var expected = List("A", "B");
            _inner.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(expected);

            var sut = CreateSut();
            var result = await sut.GetAllAsync();

            Assert.Same(expected, result);
            _logger.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetAllAsync_ReturnsEmptyList_OnCancellation()
        {
            _inner.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new OperationCanceledException());

            var sut = CreateSut();
            var result = await sut.GetAllAsync();

            Assert.Empty(result);
            _logger.VerifyLog(LogLevel.Warning, "GetAllAsync cancelled by token");
        }

        [Fact]
        public async Task GetAllAsync_ReturnsEmptyList_OnUnhandledException()
        {
            _inner.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("boom"));

            var sut = CreateSut();
            var result = await sut.GetAllAsync();

            Assert.Empty(result);
            _logger.VerifyLog(LogLevel.Error, "Unhandled exception in GetAllAsync");
        }

        // ---------- ExistsByIdAsync (покрываем bool + Exception) ----------

        [Fact]
        public async Task ExistsByIdAsync_ReturnsFalse_OnException()
        {
            _inner.Setup(r => r.ExistsByIdAsync("X", It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new Exception("err"));

            var sut = CreateSut();
            var ok = await sut.ExistsByIdAsync("X");

            Assert.False(ok);
            _logger.VerifyLog(LogLevel.Error, "Unhandled exception in ExistsByIdAsync");
        }

        // ---------- AddAsync (void метод, проглатывает) ----------

        [Fact]
        public async Task AddAsync_DoesNotThrow_WhenInnerThrows()
        {
            var product = Product.CreateTestProduct();
            _inner.Setup(r => r.AddAsync(product, It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new Exception("db down"));

            var sut = CreateSut();
            await sut.AddAsync(product); // не должно вылететь

            _logger.VerifyLog(LogLevel.Error, "Unhandled exception in AddAsync");
        }

        // ---------- UpdateProductsTransactionAsync (OperationResult) ----------

        [Fact]
        public async Task UpdateProductsTransactionAsync_ReturnsFailure_OnCancellation()
        {
            _inner.Setup(r => r.UpdateProductsTransactionAsync(
                             It.IsAny<IEnumerable<Product>>(),
                             It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new OperationCanceledException());

            var sut = CreateSut();

            var res = await sut.UpdateProductsTransactionAsync(null);

            Assert.False(res.IsSuccess);
            // проверяем список ErrorCodes
            Assert.Contains(InfraErrorCodes.UnknownInfraError, res.ErrorCodes);
            _logger.VerifyLog(LogLevel.Warning, "UpdateProductsTransactionAsync cancelled");
        }


        [Fact]
        public async Task UpdateProductsTransactionAsync_PassesThroughSuccess()
        {
            _inner.Setup(r => r.UpdateProductsTransactionAsync(
                             It.IsAny<IEnumerable<Product>>(),
                             It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Ok);

            var sut = CreateSut();
            var res = await sut.UpdateProductsTransactionAsync(Enumerable.Empty<Product>());

            Assert.True(res.IsSuccess);
            _logger.VerifyNoOtherCalls();
        }
    }

    // ---------- Moq extension to match structured logging -----------------

    internal static class MoqLoggerExtensions
    {
        public static void VerifyLog(
            this Mock<ILogger<IProductRepository>> mock,
            LogLevel level,
            string startsWith)
        {
            mock.Verify(l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.StartsWith(startsWith)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
