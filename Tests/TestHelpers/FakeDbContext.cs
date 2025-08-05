using ARM4.Domain.DomainExceptions;
using ARM4.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;

namespace Tests.Helpers;

/// <summary>
/// Базовый контекст, позволяющий подменять <see cref="DatabaseFacade"/>.
/// </summary>
internal sealed class FakeDbContext : ARM4DbContext
{
    private readonly Func<FakeDbContext, SimulatedError> _simulate;

    internal FakeDbContext(DbContextOptions<ARM4DbContext> options,
                           Func<FakeDbContext, SimulatedError> simulate)
        : base(options) => _simulate = simulate;

    public override Task<int> SaveChangesAsync(CancellationToken token = default)
    {
        return _simulate(this) switch
        {
            SimulatedError.ProductArchived
                => throw new ProductDomainException(ProductErrorCode.ProductArchived,
                           "Обновление архивированного продукта запрещено"),
            SimulatedError.ProductDeleted
                => throw new ProductDomainException(ProductErrorCode.ProductDeleted,
                           "Обновление удалённого продукта запрещено"),
            _ => base.SaveChangesAsync(token)
        };
    }

    public enum SimulatedError { None, ProductArchived, ProductDeleted }
}