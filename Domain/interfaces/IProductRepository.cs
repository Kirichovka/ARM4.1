using ARM4.Domain.Common;
using ARM4.Domain.Entities;

namespace ARM4.Domain.Interfaces {
    public interface IProductRepository
    {
        Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
        Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
        Task<List<Product>> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<Product>> SearchAsync(
            string? name = null,
            string? category = null,
            string? supplier = null,
            int skip = 0,
            int take = 50,
            string? orderBy = null,
            bool ascending = true,
            CancellationToken cancellationToken = default);

        Task<bool> ExistsByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
        Task<bool> ExistsByIdAsync(string id, CancellationToken cancellationToken = default);

        Task AddAsync(Product product, CancellationToken cancellationToken = default);
        Task AddRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);
        Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
        Task UpdateRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);
        Task RemoveAsync(Product product, CancellationToken cancellationToken = default);
        Task RemoveRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);
        Task<OperationResult> UpdateProductsTransactionAsync(
            IEnumerable<Product>? products,
            CancellationToken cancellationToken = default);
    }
}