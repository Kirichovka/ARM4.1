using ARM4.Domain.Common;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.Entities;
using ARM4.Domain.Interfaces;
using ARM4.Infrastructure.Data;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace ARM4.Infrastructure.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly ARM4DbContext _db;
        private readonly ILogger<ProductRepository> _logger;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public ProductRepository(ARM4DbContext db, ILogger<ProductRepository> logger, IMemoryCache cache)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }
        public async Task<OperationResult> UpdateProductsTransactionAsync(
            IEnumerable<Product>? products,
            CancellationToken cancellationToken = default)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            // 1) CorrelationId для сквозной трассировки
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            int count = products.Count();
            var sw = Stopwatch.StartNew();

            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Начало UpdateProductsTransactionAsync. Count={Count}",
                correlationId, count);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _logger.LogInformation(
                    "CorrelationId={CorrelationId} — Начало транзакционного обновления продуктов. Count={Count}, IsAudit={IsAudit}",
                    correlationId, count, true);

                _db.Products.UpdateRange(products);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                
                // Очистка кэша
                try
                {
                    ClearCacheForProducts(products);
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Кэш очищен после транзакции",
                        correlationId);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogError(
                        cacheEx,
                        "CorrelationId={CorrelationId} — Ошибка очистки кэша после транзакции. ErrorCode={ErrorCode}",
                        correlationId, InfraErrorCodes.CacheError);
                }

                sw.Stop();
                _logger.LogTrace(
                    "CorrelationId={CorrelationId} — UpdateProductsTransactionAsync завершён. DurationMs={DurationMs}",
                    correlationId, sw.ElapsedMilliseconds);

                return OperationResult.Success();
            }
            catch (OperationCanceledException oce)
            {
                sw.Stop();
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(
                    oce,
                    "CorrelationId={CorrelationId} — UpdateProductsTransactionAsync отменён. Count={Count}, DurationMs={DurationMs}",
                    correlationId, count, sw.ElapsedMilliseconds);
                return OperationResult.Failure("Операция отменена", InfraErrorCodes.UnknownInfraError);
            }
            catch (DbUpdateConcurrencyException dce)
            {
                sw.Stop();
                await transaction.RollbackAsync(cancellationToken);

                _logger.LogWarning(
                    dce,
                    "Concurrency conflict в UpdateProductsTransactionAsync. Count={Count}, DurationMs={DurationMs}",
                    count, sw.ElapsedMilliseconds);

                return OperationResult.Failure(
                    "Конфликт при параллельном обновлении",
                    "ConcurrencyError");
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(
                    ex,
                    "CorrelationId={CorrelationId} — ProductDomainException. ErrorCode={ErrorCode}, Count={Count}, DurationMs={DurationMs}",
                    correlationId, ex.Code, count, sw.ElapsedMilliseconds);
                return OperationResult.Failure($"Ошибка валидации: {ex.Message}", ex.Code.ToString());
            }
            catch (DomainException ex)
            {
                sw.Stop();
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(
                    ex,
                    "CorrelationId={CorrelationId} — DomainException. ErrorCode={ErrorCode}, Count={Count}, DurationMs={DurationMs}",
                    correlationId, ex.ErrorCode, count, sw.ElapsedMilliseconds);
                return OperationResult.Failure($"Ошибка доменной логики: {ex.Message}", ex.ErrorCode);
            }
            catch (Exception ex)
            {
                sw.Stop();
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(
                    ex,
                    "CorrelationId={CorrelationId} — Неизвестная ошибка. Count={Count}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    correlationId, count, sw.ElapsedMilliseconds, InfraErrorCodes.UnknownInfraError);
                return OperationResult.Failure($"Ошибка при обновлении: {ex.Message}", InfraErrorCodes.UnknownInfraError);
            }
        }
        public async Task<Product?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogTrace("GetByIdAsync: пустой Id, возвращаю null");
                return null;
            }

            // 1) CorrelationId для сквозной трассировки
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            var sw = Stopwatch.StartNew();
            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Начало GetByIdAsync. Id={Id}",
                correlationId, id);

            try
            {
                _logger.LogDebug(
                    "CorrelationId={CorrelationId} — Запрос продукта по Id. Id={Id}",
                    correlationId, id);

                string cacheKey = $"Product_Id_{id}";
                if (!_cache.TryGetValue(cacheKey, out Product? product) || product == null)
                {
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Кэш пропуск. Key={CacheKey}",
                        correlationId, cacheKey);

                    product = await _db.Products
                        .FirstOrDefaultAsync(p => EF.Property<string>(p, "id") == id,
                                             cancellationToken);

                    if (product != null)
                    {
                        // 2) Параметры кэша с Size и SlidingExpiration
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            Size = 1,
                            SlidingExpiration = TimeSpan.FromMinutes(5)
                        };
                        _cache.Set(cacheKey, product, cacheOptions);

                        _logger.LogTrace(
                            "CorrelationId={CorrelationId} — Сохранено в кэше. Key={CacheKey}",
                            correlationId, cacheKey);
                    }
                    else
                    {
                        _logger.LogTrace(
                            "CorrelationId={CorrelationId} — Продукт не найден. Id={Id}",
                            correlationId, id);
                    }
                }
                else
                {
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Найден в кэше. Key={CacheKey}",
                        correlationId, cacheKey);
                }

                sw.Stop();
                bool found = product != null;
                _logger.LogInformation(
                    "CorrelationId={CorrelationId} — GetByIdAsync завершён. Id={Id}, Found={Found}, DurationMs={DurationMs}",
                    correlationId, id, found, sw.ElapsedMilliseconds);

                return product;
            }
            catch (OperationCanceledException oce)
            {
                sw.Stop();
                _logger.LogWarning(
                    oce,
                    "CorrelationId={CorrelationId} — GetByIdAsync отменён. Id={Id}, DurationMs={DurationMs}",
                    correlationId, id, sw.ElapsedMilliseconds);

                return null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "CorrelationId={CorrelationId} — Ошибка в GetByIdAsync. Id={Id}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    correlationId, id, sw.ElapsedMilliseconds, InfraErrorCodes.UnknownInfraError);

                throw;
            }
        }
        public async Task<Product?> GetByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(barcode))
            {
                _logger.LogTrace("GetByBarcodeAsync: пустой Barcode, возвращаю null");
                return null;
            }

            // 1) CorrelationId для сквозной трассировки
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            var sw = Stopwatch.StartNew();
            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Начало GetByBarcodeAsync. Barcode={Barcode}",
                correlationId, barcode);

            try
            {
                _logger.LogDebug(
                    "CorrelationId={CorrelationId} — Запрос продукта по Barcode. Barcode={Barcode}",
                    correlationId, barcode);

                string cacheKey = $"Product_Barcode_{barcode}";
                if (!_cache.TryGetValue(cacheKey, out Product? product) || product == null)
                {
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Кэш пропуск. Key={CacheKey}",
                        correlationId, cacheKey);

                    product = await _db.Products
                        .FirstOrDefaultAsync(
                            p => EF.Property<string?>(p, "barcode") == barcode,
                            cancellationToken);

                    if (product != null)
                    {
                        // 2) Настройка MemoryCacheEntryOptions
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            Size = 1,
                            SlidingExpiration = TimeSpan.FromMinutes(5)
                        };
                        _cache.Set(cacheKey, product, cacheOptions);

                        _logger.LogTrace(
                            "CorrelationId={CorrelationId} — Сохранено в кэше. Key={CacheKey}",
                            correlationId, cacheKey);
                    }
                    else
                    {
                        _logger.LogTrace(
                            "CorrelationId={CorrelationId} — Продукт не найден по Barcode={Barcode}",
                            correlationId, barcode);
                    }
                }
                else
                {
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Найдено в кэше. Key={CacheKey}",
                        correlationId, cacheKey);
                }

                sw.Stop();
                bool found = product != null;
                _logger.LogInformation(
                    "CorrelationId={CorrelationId} — GetByBarcodeAsync завершён. Barcode={Barcode}, Found={Found}, DurationMs={DurationMs}",
                    correlationId, barcode, found, sw.ElapsedMilliseconds);

                return product;
            }
            catch (OperationCanceledException oce)
            {
                sw.Stop();
                _logger.LogWarning(
                    oce,
                    "CorrelationId={CorrelationId} — GetByBarcodeAsync отменён. Barcode={Barcode}, DurationMs={DurationMs}",
                    correlationId, barcode, sw.ElapsedMilliseconds);
                return null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "CorrelationId={CorrelationId} — Ошибка в GetByBarcodeAsync. Barcode={Barcode}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    correlationId, barcode, sw.ElapsedMilliseconds, InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task<List<Product>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogTrace(
                "Начало GetByNameAsync. Name={Name}",
                name);

            try
            {
                _logger.LogDebug(
                    "Запрос продуктов по имени. Filter={Name}",
                    name);

                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger.LogTrace(
                        "GetByNameAsync: пустой фильтр, возвращаю пустой список");
                    return new List<Product>();
                }

                string cacheKey = $"Products_Name_{name}";
                List<Product>? products;

                if (!_cache.TryGetValue(cacheKey, out products) || products == null)
                {
                    _logger.LogTrace(
                        "Кэш пропуск. Key={CacheKey}",
                        cacheKey);

                    products = await _db.Products
                        .Where(p => EF.Property<string>(p, "name").Contains(name))
                        .ToListAsync(cancellationToken);

                    _cache.Set(cacheKey, products, _cacheDuration);
                    _logger.LogTrace(
                        "Сохранено в кэше. Key={CacheKey}, Count={Count}",
                        cacheKey,
                        products.Count);
                }
                else
                {
                    _logger.LogTrace(
                        "Найдено в кэше. Key={CacheKey}, Count={Count}",
                        cacheKey,
                        products.Count);
                }

                int count = products.Count;
                sw.Stop();

                _logger.LogInformation(
                    "Поиск по имени завершён. Name={Name}, Found={Count}, DurationMs={DurationMs}",
                    name,
                    count,
                    sw.ElapsedMilliseconds);

                _logger.LogTrace(
                    "GetByNameAsync завершён. Count={Count}, DurationMs={DurationMs}",
                    count,
                    sw.ElapsedMilliseconds);

                return products;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Ошибка в GetByNameAsync. Filter={Name}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    name,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogTrace("Начало GetAllAsync");

            try
            {
                _logger.LogDebug("Запрос всех продуктов");

                string cacheKey = "AllProducts";
                List<Product>? products;

                // Если кэш отсутствует или равен null — загружаем из БД
                if (!_cache.TryGetValue(cacheKey, out products) || products == null)
                {
                    _logger.LogTrace("Кэш пропуск. Key={CacheKey}", cacheKey);

                    products = await _db.Products.ToListAsync(cancellationToken);

                    _cache.Set(cacheKey, products, _cacheDuration);
                    _logger.LogTrace("Сохранено в кэше. Key={CacheKey}, Count={Count}", cacheKey, products.Count);
                }
                else
                {
                    _logger.LogTrace("Найдено в кэше. Key={CacheKey}, Count={Count}", cacheKey, products.Count);
                }

                int count = products.Count;
                sw.Stop();

                // Audit-лог получения списка
                _logger.LogInformation(
                    "Получен список всех продуктов. Count={Count}, DurationMs={DurationMs}",
                    count,
                    sw.ElapsedMilliseconds);

                _logger.LogTrace(
                    "GetAllAsync завершён. Count={Count}, DurationMs={DurationMs}",
                    count,
                    sw.ElapsedMilliseconds);

                return products;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Ошибка в GetAllAsync. DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
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
            if (skip < 0)
                throw new ArgumentOutOfRangeException(nameof(skip), "skip не может быть отрицательным");
            if (take <= 0 || take > 1000)
                throw new ArgumentOutOfRangeException(nameof(take), "take должен быть в диапазоне 1…1000");

            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Начало SearchAsync. Name={Name}, Category={Category}, Supplier={Supplier}, Skip={Skip}, Take={Take}, OrderBy={OrderBy}, Ascending={Ascending}",
                correlationId, name, category, supplier, skip, take, orderBy, ascending);

            string cacheKey = $"Search_{name}_{category}_{supplier}_{skip}_{take}_{orderBy}_{ascending}";
            List<Product>? products;

            try
            {
                if (!_cache.TryGetValue(cacheKey, out products) || products == null)
                {
                    _logger.LogTrace("CorrelationId={CorrelationId} — Кэш пропуск. Key={CacheKey}", correlationId, cacheKey);

                    IQueryable<Product> query = _db.Products;
                    // …строим запрос по фильтрам…

                    products = await query
                        .Skip(skip)
                        .Take(take)
                        .ToListAsync(cancellationToken);

                    // 3) Параметры кэша с Size и SlidingExpiration
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        Size = 1,
                        SlidingExpiration = TimeSpan.FromMinutes(5)
                    };
                    _cache.Set(cacheKey, products, cacheOptions);

                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Сохранено в кэше. Key={CacheKey}, Retrieved={Count}",
                        correlationId, cacheKey, products.Count);
                }
                else
                {
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Найдено в кэше. Key={CacheKey}, Count={Count}",
                        correlationId, cacheKey, products.Count);
                }

                int resultCount = products.Count;
                sw.Stop();
                _logger.LogTrace(
                    "CorrelationId={CorrelationId} — SearchAsync завершён. Count={Count}, DurationMs={DurationMs}",
                    correlationId, resultCount, sw.ElapsedMilliseconds);

                return products;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "CorrelationId={CorrelationId} — Ошибка в SearchAsync. DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    correlationId, sw.ElapsedMilliseconds, InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task<bool> ExistsByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogTrace(
                "Начало ExistsByBarcodeAsync. Barcode={Barcode}",
                barcode);

            try
            {
                _logger.LogDebug(
                    "Проверка существования продукта по Barcode. Barcode={Barcode}",
                    barcode);

                if (string.IsNullOrWhiteSpace(barcode))
                {
                    _logger.LogTrace(
                        "ExistsByBarcodeAsync: пустой Barcode, возвращаю false");
                    return false;
                }

                string cacheKey = $"Exists_Barcode_{barcode}";
                bool exists;

                if (!_cache.TryGetValue(cacheKey, out exists))
                {
                    _logger.LogTrace(
                        "Кэш пропуск. Key={CacheKey}",
                        cacheKey);

                    exists = await _db.Products
                        .AnyAsync(p => EF.Property<string?>(p, "barcode") == barcode, cancellationToken);

                    _cache.Set(cacheKey, exists, _cacheDuration);

                    _logger.LogTrace(
                        "Сохранено в кэше. Key={CacheKey}, Exists={Exists}",
                        cacheKey,
                        exists);
                }
                else
                {
                    _logger.LogTrace(
                        "Найдено в кэше. Key={CacheKey}, Exists={Exists}",
                        cacheKey,
                        exists);
                }

                sw.Stop();
                _logger.LogTrace(
                    "ExistsByBarcodeAsync завершён. Barcode={Barcode}, Exists={Exists}, DurationMs={DurationMs}",
                    barcode,
                    exists,
                    sw.ElapsedMilliseconds);

                return exists;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Ошибка в ExistsByBarcodeAsync. Barcode={Barcode}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    barcode,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task<bool> ExistsByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogTrace(
                "Начало ExistsByIdAsync. Id={Id}",
                id);

            try
            {
                _logger.LogDebug(
                    "Проверка существования продукта по Id. Id={Id}",
                    id);

                if (string.IsNullOrWhiteSpace(id))
                {
                    _logger.LogTrace(
                        "ExistsByIdAsync: пустой Id, возвращаю false");
                    return false;
                }

                string cacheKey = $"Exists_Id_{id}";
                bool exists;

                if (!_cache.TryGetValue(cacheKey, out exists))
                {
                    _logger.LogTrace(
                        "Кэш пропуск. Key={CacheKey}",
                        cacheKey);

                    exists = await _db.Products
                        .AnyAsync(p => EF.Property<string>(p, "id") == id, cancellationToken);

                    _cache.Set(cacheKey, exists, _cacheDuration);

                    _logger.LogTrace(
                        "Сохранено в кэше. Key={CacheKey}, Exists={Exists}",
                        cacheKey,
                        exists);
                }
                else
                {
                    _logger.LogTrace(
                        "Найдено в кэше. Key={CacheKey}, Exists={Exists}",
                        cacheKey,
                        exists);
                }

                sw.Stop();
                _logger.LogTrace(
                    "ExistsByIdAsync завершён. Id={Id}, Exists={Exists}, DurationMs={DurationMs}",
                    id,
                    exists,
                    sw.ElapsedMilliseconds);

                return exists;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Ошибка в ExistsByIdAsync. Id={Id}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    id,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            var productId = product.GetId();
            var productName = product.GetName();

            // Вход в метод
            _logger.LogTrace(
                "Начало AddAsync. Id={ProductId}, Name={ProductName}",
                productId,
                productName);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Добавляем продукт
                await _db.Products.AddAsync(product, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);

                // Очищаем кэш
                ClearCacheForProduct(product);

                sw.Stop();
                _logger.LogTrace(
                    "AddAsync успешно завершён. Id={ProductId}, DurationMs={DurationMs}",
                    productId,
                    sw.ElapsedMilliseconds);
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    ex,
                    "ProductDomainException в AddAsync. ErrorCode={ErrorCode}, Field={Field}, Value={Value}, DurationMs={DurationMs}",
                    ex.Code,
                    ex.Field,
                    ex.Value,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (DomainException ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "DomainException в AddAsync. ErrorCode={ErrorCode}, Field={Field}, Value={Value}, DurationMs={DurationMs}",
                    ex.ErrorCode,
                    ex.Field,
                    ex.Value,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Неизвестная ошибка в AddAsync. Id={ProductId}, Name={ProductName}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    productId,
                    productName,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task AddRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            int count = products.Count();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogTrace(
                "Начало AddRangeAsync. Count={Count}",
                count);

            try
            {
                // Аудит-лог добавления
                _logger.LogInformation(
                    "Добавление списка продуктов. Count={Count}, IsAudit={IsAudit}",
                    count,
                    true);

                // Основная операция
                await _db.Products.AddRangeAsync(products, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);

                // Очистка кэша
                ClearCacheForProducts(products);

                sw.Stop();
                _logger.LogTrace(
                    "AddRangeAsync успешно завершён. Added={Count}, DurationMs={DurationMs}",
                    count,
                    sw.ElapsedMilliseconds);
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    ex,
                    "ProductDomainException в AddRangeAsync. ErrorCode={ErrorCode}, DurationMs={DurationMs}",
                    ex.Code,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (DomainException ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "DomainException в AddRangeAsync. ErrorCode={ErrorCode}, DurationMs={DurationMs}",
                    ex.ErrorCode,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Неизвестная ошибка в AddRangeAsync. DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            var productId = product.GetId();
            var productName = product.GetName();

            // Вход в метод
            _logger.LogTrace(
                "Начало UpdateAsync. Id={ProductId}, Name={ProductName}",
                productId,
                productName);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Аудит-лог операции обновления
                _logger.LogInformation(
                    "Обновление продукта. Id={ProductId}, Name={ProductName}, IsAudit={IsAudit}",
                    productId,
                    productName,
                    true);

                // Сохраняем изменения в базе
                _db.Products.Update(product);
                await _db.SaveChangesAsync(cancellationToken);

                // Очищаем кэш
                ClearCacheForProduct(product);

                sw.Stop();
                _logger.LogTrace(
                    "UpdateAsync успешно завершён. Id={ProductId}, DurationMs={DurationMs}",
                    productId,
                    sw.ElapsedMilliseconds);
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    ex,
                    "ProductDomainException в UpdateAsync. ErrorCode={ErrorCode}, Id={ProductId}, Name={ProductName}, DurationMs={DurationMs}",
                    ex.Code,
                    productId,
                    productName,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (DomainException ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "DomainException в UpdateAsync. ErrorCode={ErrorCode}, Id={ProductId}, Name={ProductName}, DurationMs={DurationMs}",
                    ex.ErrorCode,
                    productId,
                    productName,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Неизвестная ошибка в UpdateAsync. Id={ProductId}, Name={ProductName}, DurationMs={DurationMs}, ErrorCode={ErrorCode}",
                    productId,
                    productName,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task UpdateRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            int count = products.Count();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogTrace(
                "Начало UpdateRangeAsync. Count={Count}",
                count);

            try
            {
                // Аудит-лог обновления
                _logger.LogInformation(
                    "Обновление списка продуктов. Count={Count}, IsAudit={IsAudit}",
                    count,
                    true);

                // Основная операция
                _db.Products.UpdateRange(products);
                await _db.SaveChangesAsync(cancellationToken);

                // Очистка кэша
                ClearCacheForProducts(products);

                sw.Stop();
                _logger.LogTrace(
                    "UpdateRangeAsync успешно завершён. Updated={Count}, DurationMs={ElapsedMs}",
                    count,
                    sw.ElapsedMilliseconds);
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    ex,
                    "ProductDomainException в UpdateRangeAsync. ErrorCode={ErrorCode}, Count={Count}, DurationMs={ElapsedMs}",
                    ex.Code,
                    count,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (DomainException ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "DomainException в UpdateRangeAsync. ErrorCode={ErrorCode}, Count={Count}, DurationMs={ElapsedMs}",
                    ex.ErrorCode,
                    count,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Неизвестная ошибка в UpdateRangeAsync. Count={Count}, DurationMs={ElapsedMs}, ErrorCode={ErrorCode}",
                    count,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task RemoveAsync(Product product, CancellationToken cancellationToken = default)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            var productId = product.GetId();
            var productName = product.GetName();

            // Вход в метод
            _logger.LogTrace(
                "Начало RemoveAsync. Id={ProductId}, Name={Name}",
                productId,
                productName);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Аудит-лог операции удаления
                _logger.LogInformation(
                    "Удаление продукта. Id={ProductId}, Name={Name}, IsAudit={IsAudit}",
                    productId,
                    productName,
                    true);

                // Удаляем из БД
                _db.Products.Remove(product);
                await _db.SaveChangesAsync(cancellationToken);

                // Очищаем кэш
                ClearCacheForProduct(product);

                sw.Stop();
                _logger.LogTrace(
                    "RemoveAsync успешно завершён. Id={ProductId}, DurationMs={ElapsedMs}",
                    productId,
                    sw.ElapsedMilliseconds);
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    ex,
                    "ProductDomainException в RemoveAsync. ErrorCode={ErrorCode}, Id={ProductId}, Name={Name}, DurationMs={ElapsedMs}",
                    ex.Code,
                    productId,
                    productName,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (DomainException ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "DomainException в RemoveAsync. ErrorCode={ErrorCode}, Id={ProductId}, Name={Name}, DurationMs={ElapsedMs}",
                    ex.ErrorCode,
                    productId,
                    productName,
                    sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Неизвестная ошибка в RemoveAsync. Id={ProductId}, Name={Name}, DurationMs={ElapsedMs}, ErrorCode={ErrorCode}",
                    productId,
                    productName,
                    sw.ElapsedMilliseconds,
                    InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        public async Task RemoveRangeAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            int count = products.Count();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogTrace(
                "Начало RemoveRangeAsync. Count={Count}",
                count);

            try
            {
                // Аудит-лог с признаком IsAudit=true
                _logger.LogInformation(
                    "Удаление списка продуктов. Count={Count}, IsAudit={IsAudit}",
                    count, true);

                _db.Products.RemoveRange(products);
                await _db.SaveChangesAsync(cancellationToken);

                ClearCacheForProducts(products);

                sw.Stop();
                _logger.LogTrace(
                    "RemoveRangeAsync успешно завершён. Deleted={Count}, DurationMs={ElapsedMs}",
                    count, sw.ElapsedMilliseconds);
            }
            catch (ProductDomainException ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    ex,
                    "ProductDomainException в RemoveRangeAsync. ErrorCode={ErrorCode}, Count={Count}, DurationMs={ElapsedMs}",
                    ex.Code, count, sw.ElapsedMilliseconds);
                throw;
            }
            catch (DomainException ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "DomainException в RemoveRangeAsync. ErrorCode={ErrorCode}, Count={Count}, DurationMs={ElapsedMs}",
                    ex.ErrorCode, count, sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "Неизвестная ошибка в RemoveRangeAsync. Count={Count}, DurationMs={ElapsedMs}, ErrorCode={ErrorCode}",
                    count, sw.ElapsedMilliseconds, InfraErrorCodes.UnknownInfraError);
                throw;
            }
        }
        private void ClearCacheForProduct(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            // подготовка трассировки
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
            var sw = Stopwatch.StartNew();
            var id = product.GetId();
            var barcode = product.GetBarcode()?.ToString();

            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Начало очистки кэша. Id={ProductId}, Barcode={Barcode}",
                correlationId, id, barcode);

            // 1) общий кеш
            SafeRemove("AllProducts", correlationId);

            // 2) кеш по Id
            SafeRemove($"Product_Id_{id}", correlationId);

            // 3) кеш по штрихкоду
            if (!string.IsNullOrEmpty(barcode))
                SafeRemove($"Product_Barcode_{barcode}", correlationId);

            sw.Stop();
            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Очистка кэша завершена. DurationMs={DurationMs} мс",
                correlationId, sw.ElapsedMilliseconds);
        }

        private void SafeRemove(object key, string correlationId)
        {
            try
            {
                _cache.Remove(key);
                _logger.LogTrace(
                    "CorrelationId={CorrelationId} — Кэш для ключа '{Key}' очищен",
                    correlationId, key);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "CorrelationId={CorrelationId} — Ошибка при Remove('{Key}'): {ExceptionMessage}. ErrorCode={ErrorCode}",
                    correlationId, key, ex.Message, InfraErrorCodes.CacheError);
                // не пробрасываем дальше
            }
        }

        private void ClearCacheForProducts(IEnumerable<Product> products)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            // CorrelationId для трассировки
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            var list = products as IList<Product> ?? products.ToList();
            int count = list.Count;
            var sw = Stopwatch.StartNew();

            // Если вдруг слишком много продуктов – предупредим
            if (count > 10_000)
            {
                _logger.LogWarning(
                    "CorrelationId={CorrelationId} — Попытка массовой очистки кэша для {TotalCount} продуктов, операция может занять много времени",
                    correlationId, count);
            }

            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Начало массовой очистки кэша. Всего продуктов: {TotalCount}",
                correlationId, count);

            SafeRemove("AllProducts", correlationId);

            int success = 0, error = 0;

            foreach (var product in list)
            {
                try
                {
                    if (product == null)
                    {
                        _logger.LogWarning(
                            "CorrelationId={CorrelationId} — Пропущен null-продукт. ErrorCode={ErrorCode}",
                            correlationId, InfraErrorCodes.CacheError);
                        error++;
                        continue;
                    }

                    var productId = product.GetId();

                    if (!Guid.TryParse(productId, out _))
                    {
                        _logger.LogWarning("Некорректный формат ProductId, пропускаем очистку кэша. ProductId={ProductId}", productId);
                        error++;
                        continue;
                    }

                    _cache.Remove($"Product_Id_{productId}");
                    _logger.LogTrace(
                        "CorrelationId={CorrelationId} — Кэш 'Product_Id_{ProductId}' очищен",
                        correlationId, productId);

                    var barcode = product.GetBarcode()?.ToString();
                    if (!string.IsNullOrEmpty(barcode))
                    {
                        // TODO: проверка формата штрихкода
                        // if (!IsValidBarcode(barcode)) { ... continue; }

                        _cache.Remove($"Product_Barcode_{barcode}");
                        _logger.LogTrace(
                            "CorrelationId={CorrelationId} — Кэш 'Product_Barcode_{Barcode}' очищен",
                            correlationId, barcode);
                    }

                    success++;
                }
                catch (Exception ex)
                {
                    error++;
                    _logger.LogError(
                        ex,
                        "CorrelationId={CorrelationId} — Ошибка очистки кэша для продукта. Id={ProductId}, Barcode={Barcode}, ErrorCode={ErrorCode}",
                        correlationId,
                        product?.GetId(),
                        product?.GetBarcode()?.ToString(),
                        InfraErrorCodes.CacheError);
                }
            }

            sw.Stop();
            _logger.LogTrace(
                "CorrelationId={CorrelationId} — Массовая очистка кэша завершена. DurationMs={ElapsedMs}",
                correlationId, sw.ElapsedMilliseconds);

            if (error > 0)
            {
                _logger.LogWarning(
                    "CorrelationId={CorrelationId} — При массовой очистке кэша {TotalCount} продуктов произошло {ErrorCount} ошибок",
                    correlationId, count, error);
            }
        }
    }
}
