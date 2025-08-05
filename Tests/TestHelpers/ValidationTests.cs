namespace Tests.TestHelpers;

using System.Threading;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.Entities;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

internal static class TestFactories
{
    public static ProductRepository CreateSut(
        DbContextOptions<ARM4DbContext> opts,
        ILogger<ProductRepository>? logger = null,
        ARM4DbContext? ctxOverride = null)
    {
        var ctx = ctxOverride ?? new ARM4DbContext(opts);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ProductRepository(ctx, logger ?? Mock.Of<ILogger<ProductRepository>>(), cache);
    }

    /// <summary>
    /// DbContext, который бросает <see cref="ProductDomainException"/> с нужным кодом.
    /// </summary>
    internal sealed class FlagFailingDbContext : ARM4DbContext
    {
        private readonly ProductErrorCode _code;
        public FlagFailingDbContext(DbContextOptions<ARM4DbContext> opts, ProductErrorCode code)
            : base(opts) => _code = code;

        public override Task<int> SaveChangesAsync(CancellationToken token = default) =>
            Task.FromException<int>(new ProductDomainException(_code, "Simulated flag validation error"));
    }
}
