using System;
using System.Collections.Generic;
using ARM4.Domain.Builders;
using ARM4.Domain.Common;
using ARM4.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace ARM4.Tests.TestHelpers
{
    /// <summary>
    /// Centralized test data factories and helper extensions used across unit tests.
    /// </summary>
    public static class ProductTestFactories
    {
        /// <summary>
        /// Returns fixed <see cref="ITimeProvider"/> that always yields <paramref name="utcNow"/> or <see cref="DateTime.UtcNow"/>.
        /// </summary>
        public static ITimeProvider TimeProvider(DateTime? utcNow = null) =>
            new TestTimeProvider(utcNow ?? DateTime.UtcNow);

        /// <summary>
        /// Creates a <see cref="ProductBuilder"/> pre‑filled with sample data.
        /// </summary>
        public static ProductBuilder Builder(
            string name = "Test Product",
            string category = "Category",
            decimal wholesalePrice = 5m,
            decimal salePrice = 10m,
            int quantity = 1)
            => new ProductBuilder(name, category, wholesalePrice, salePrice, quantity);

        /// <summary>
        /// Builds a valid <see cref="Product"/> using sample data.
        /// </summary>
        public static Product Product(
            ITimeProvider? tp = null,
            string name = "Test Product",
            string category = "Category",
            decimal wholesalePrice = 5m,
            decimal salePrice = 10m,
            int quantity = 1)
        {
            var builder = Builder(name, category, wholesalePrice, salePrice, quantity);
            return builder.Build(tp ?? TimeProvider());
        }

        /// <summary>
        /// Builds a valid <see cref="Product"/> with an explicit EAN‑13 barcode.
        /// </summary>
        public static Product ProductWithBarcode(
            string barcode,
            ITimeProvider? tp = null,
            string name = "Test Product",
            string category = "Category",
            decimal wholesalePrice = 5m,
            decimal salePrice = 10m,
            int quantity = 1)
        {
            var builder = Builder(name, category, wholesalePrice, salePrice, quantity)
                .SetBarcode(barcode);
            return builder.Build(tp ?? TimeProvider());
        }

        /// <summary>
        /// Generates a deterministic list of <see cref="Product"/> objects.
        /// </summary>
        public static List<Product> ProductList(int count, ITimeProvider? tp = null)
        {
            var list = new List<Product>(capacity: count);
            for (var i = 0; i < count; i++)
            {
                list.Add(Product(
                    tp,
                    name: $"Product {i + 1}",
                    category: "Category",
                    wholesalePrice: 1m + i,
                    salePrice: 2m + i,
                    quantity: (i + 1) * 10));
            }
            return list;
        }
    }

    // ------------------ shared logger verification helpers -------------------

    public static class MoqLoggerExtensions
    {
        /// <summary>
        /// Shortcut to verify that a structured log entry with a message starting with <paramref name="startsWith"/>
        /// has been written exactly once at the specified <paramref name="level"/>.
        /// </summary>
        public static void VerifyLog<T>(
            this Mock<ILogger<T>> mock,
            LogLevel level,
            string startsWith) where T : class
        {
            mock.Verify(l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.StartsWith(startsWith, StringComparison.Ordinal)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                $"Expected a single log entry '{startsWith}...' at level {level}");
        }
    }
}
