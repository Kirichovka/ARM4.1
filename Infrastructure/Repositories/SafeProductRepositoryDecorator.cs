using ARM4.Domain.Common;
using ARM4.Domain.Entities;
using ARM4.Domain.Interfaces;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ARM4.Infrastructure.Repositories
{
    public class SafeProductRepositoryDecorator : IProductRepository
    {
        private readonly IProductRepository _inner;
        private readonly ILogger<IProductRepository> _logger;

        public SafeProductRepositoryDecorator(
            IProductRepository inner,
            ILogger<IProductRepository> logger)
        {
            _inner = inner;
            _logger = logger;
        }

        public async Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.GetAllAsync(cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "GetAllAsync cancelled by token");
                return new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in GetAllAsync");
                return new List<Product>();
            }
        }

        public async Task<List<Product>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.GetByNameAsync(name, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "GetByNameAsync cancelled. Name={Name}", name);
                return new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in GetByNameAsync. Name={Name}", name);
                return new List<Product>();
            }
        }

        public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.GetByBarcodeAsync(barcode, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "GetByBarcodeAsync cancelled. Barcode={Barcode}", barcode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in GetByBarcodeAsync. Barcode={Barcode}", barcode);
                return null;
            }
        }

        public async Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.GetByIdAsync(id, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "GetByIdAsync cancelled. Id={Id}", id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in GetByIdAsync. Id={Id}", id);
                return null;
            }
        }

        public async Task<bool> ExistsByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.ExistsByIdAsync(id, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "ExistsByIdAsync cancelled. Id={Id}", id);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in ExistsByIdAsync. Id={Id}", id);
                return false;
            }
        }

        public async Task<bool> ExistsByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.ExistsByBarcodeAsync(barcode, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "ExistsByBarcodeAsync cancelled. Barcode={Barcode}", barcode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in ExistsByBarcodeAsync. Barcode={Barcode}", barcode);
                return false;
            }
        }

        public async Task<List<Product>> SearchAsync(
            string? name = null,
            string? category = null,
            string? supplier = null,
            int skip = 0,
            int take = 50,
            string? orderBy = null,
            bool ascending = true,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.SearchAsync(name, category, supplier, skip, take, orderBy, ascending, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex,
                    "SearchAsync cancelled. Name={Name}, Category={Category}, Supplier={Supplier}",
                    name, category, supplier);
                return new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception in SearchAsync. Name={Name}, Category={Category}, Supplier={Supplier}",
                    name, category, supplier);
                return new List<Product>();
            }
        }

        public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.AddAsync(product, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "AddAsync cancelled. Id={Id}", product.GetId());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in AddAsync. Id={Id}", product.GetId());
            }
        }

        public async Task AddRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.AddRangeAsync(products, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "AddRangeAsync cancelled. Count={Count}", products?.Count() ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in AddRangeAsync. Count={Count}", products?.Count() ?? 0);
            }
        }

        public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.UpdateAsync(product, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "UpdateAsync cancelled. Id={Id}", product.GetId());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in UpdateAsync. Id={Id}", product.GetId());
            }
        }

        public async Task UpdateRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.UpdateRangeAsync(products, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "UpdateRangeAsync cancelled. Count={Count}", products?.Count() ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in UpdateRangeAsync. Count={Count}", products?.Count() ?? 0);
            }
        }

        public async Task RemoveAsync(Product product, CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.RemoveAsync(product, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "RemoveAsync cancelled. Id={Id}", product.GetId());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in RemoveAsync. Id={Id}", product.GetId());
            }
        }

        public async Task RemoveRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.RemoveRangeAsync(products, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "RemoveRangeAsync cancelled. Count={Count}", products?.Count() ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in RemoveRangeAsync. Count={Count}", products?.Count() ?? 0);
            }
        }

        public async Task<OperationResult> UpdateProductsTransactionAsync(
            IEnumerable<Product>? products,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.UpdateProductsTransactionAsync(products, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "UpdateProductsTransactionAsync cancelled. Count={Count}", products?.Count() ?? 0);
                return OperationResult.Failure("Operation cancelled", InfraErrorCodes.UnknownInfraError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in UpdateProductsTransactionAsync. Count={Count}", products?.Count() ?? 0);
                return OperationResult.Failure("Internal error", InfraErrorCodes.UnknownInfraError);
            }
        }
    }
}
