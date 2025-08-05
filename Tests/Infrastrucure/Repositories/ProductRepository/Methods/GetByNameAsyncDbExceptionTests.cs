using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class GetByNameAsyncDbExceptionTests
    {
        [Fact]
        public async Task Throws_On_GenericException_FromDb()
        {
            // 1) Мокаем DbSet<Product>
            var mockSet = new Mock<DbSet<Product>>();

            // При попытке асинхронного перечисления (ToListAsync) бросаем
            mockSet.As<IAsyncEnumerable<Product>>()
                   .Setup(m => m.GetAsyncEnumerator(default))
                   .Throws(new InvalidOperationException("db fail"));

            // LINQ-операции (Where/Provider) тоже должны бросать
            mockSet.As<IQueryable<Product>>()
                   .Setup(m => m.Provider)
                   .Throws(new InvalidOperationException("db fail"));

            // 2) Мокаем контекст, отдающий наш «сломанный» набор
            var ctxMock = new Mock<ARM4DbContext>(new DbContextOptions<ARM4DbContext>());
            ctxMock.Setup(c => c.Products).Returns(mockSet.Object);

            // 3) Репозиторий с «битым» контекстом
            var repo = new ProductRepository(
                ctxMock.Object,
                new NullLogger<ProductRepository>(),
                new MemoryCache(new MemoryCacheOptions()));

            // Act & Assert — должен проброситься InvalidOperationException("db fail")
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetByNameAsync("anything"));

            Assert.Equal("db fail", ex.Message);
        }
    }
}
