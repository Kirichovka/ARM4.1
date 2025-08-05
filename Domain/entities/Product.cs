#nullable enable
using ARM4.Domain.Builders;
using ARM4.Domain.Common;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.ValueObjects;
using Domain.domainexceptions;
using Domain.entities.Category;
using Domain.entities.Manufacturer;
using Domain.entities.Supplier;
using System;
using System.Security.Cryptography;

namespace ARM4.Domain.Entities
{
    /// <summary>Доменная сущность «Продукт».</summary>
    public class Product
    {
        private const decimal MaxPrice = 1_000_000m;
        private const int MaxQuantity = 1_000_000;

        public enum Unit { Piece, Kilogram, Liter }

        // ======== поля ========
        private readonly string id;
        private readonly string displayCode;

        private string name = string.Empty;
        private string category = string.Empty;   // ← добавлено
        private int categoryId;
        private decimal wholesalePrice;
        private decimal salePrice;
        private int quantity;
        private BarcodeVO? barcode;
        private Unit productUnit;
        public int? supplierId;
        public Supplier? supplier { get; private set; }
        public int? manufacturerId;
        public Manufacturer? manufacturer { get; private set; }

        private DateTime arrivalDate;
        private DateTime? expirationDate;
        private bool isDeleted;
        private bool isArchived;
        private byte[] _rowVersion = Array.Empty<byte>();

        private readonly ITimeProvider timeProvider;

        // ======== конструкторы ========
        protected Product()
        {
            timeProvider = new SystemTimeProvider();
            id = Guid.NewGuid().ToString();
            displayCode = GenerateDisplayCode();
        }

        internal Product(ProductBuilder b, ITimeProvider? tp = null)
        {
            // Провайдер времени
            timeProvider = tp ?? new SystemTimeProvider();

            // Id / DisplayCode
            id = string.IsNullOrEmpty(b.Id)
                ? Guid.NewGuid().ToString()
                : b.Id;
            displayCode = string.IsNullOrEmpty(b.DisplayCode)
                ? GenerateDisplayCode()
                : b.DisplayCode;

            // Обязательные поля
            SetName(b.Name);
            SetCategory(b.Category);
            SetWholesalePrice(b.WholesalePrice);
            SetSalePrice(b.SalePrice);
            SetQuantity(b.Quantity);

            // Штрихкод (если задан)
            BarcodeVO? bc = b.Barcode;
            if (bc is not null)
                SetBarcode(bc);

            // Единица измерения
            SetProductUnit(b.ProductUnit);

            // Новые справочники
            SetSupplier(b.Supplier);
            SetManufacturer(b.Manufacturer);

            // Даты
            var arrival = b.ArrivalDate == DateTime.MinValue
                ? timeProvider.UtcNow
                : b.ArrivalDate;
            SetArrivalDate(arrival);

            SetExpirationDate(b.ExpirationDate);

            // Финальная проверка инвариантов
            ValidateInvariant();
        }


        // ======== навигация для EF ========
        public Category? Category { get; set; }

        // ======== геттеры ========
        public string Name => name;
        public string CategoryName => category;   // ← новое свойство
        public string GetId() => id;
        public string GetDisplayCode() => displayCode;
        public decimal GetWholesalePrice() => wholesalePrice;
        public decimal GetSalePrice() => salePrice;
        public int GetQuantity() => quantity;
        public BarcodeVO? GetBarcode() => barcode;
        public Unit GetProductUnit() => productUnit;
        public Supplier? GetSupplier() => this.supplier;
        public Manufacturer? GetManufacturer() => this.manufacturer;
        public DateTime GetArrivalDate() => arrivalDate;
        public DateTime? GetExpirationDate() => expirationDate;
        public bool GetIsDeleted() => isDeleted;
        public bool GetIsArchived() => isArchived;
        public byte[] RowVersion { get => _rowVersion; private set => _rowVersion = value; }

        // ======== сеттеры с проверками ========
        public void SetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw DomainException.Create(
                    DomainErrorCode.ProductNameInvalid.ToString(),
                    "Название продукта обязательно.",
                    nameof(value), value);

            var trimmed = value.Trim();
            if (trimmed.Length > 200)
                throw DomainException.Create(
                    DomainErrorCode.InvalidLength.ToString(),
                    "Название продукта слишком длинное (максимум 200 символов).",
                    nameof(value), value);

            if (trimmed.Any(char.IsControl))
                throw DomainException.Create(
                    DomainErrorCode.InvalidFormat.ToString(),
                    "Название продукта содержит недопустимые управляющие символы.",
                    nameof(value), value);

            if (trimmed.Contains("  "))
                throw DomainException.Create(
                    DomainErrorCode.InvalidFormat.ToString(),
                    "Название продукта не должно содержать подряд идущие пробелы.",
                    nameof(value), value);

            name = trimmed;
        }

        public void SetCategory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw DomainException.Create(
                    DomainErrorCode.ProductCategoryInvalid.ToString(),
                    "Категория обязательна.",
                    nameof(value), value);

            var trimmed = value.Trim();
            if (trimmed.Length > 200)
                throw DomainException.Create(
                    DomainErrorCode.InvalidLength.ToString(),
                    "Название категории слишком длинное (≤200).",
                    nameof(value), value);

            category = trimmed;
        }

        public void SetWholesalePrice(decimal value)
        {
            if (value < 0)
                throw DomainException.Create(
                    DomainErrorCode.ProductPriceNegative.ToString(),
                    "Оптовая цена не может быть отрицательной.",
                    nameof(value), value);

            if (value > MaxPrice)
                throw DomainException.Create(
                    DomainErrorCode.ProductPriceTooLarge.ToString(),
                    $"Оптовая цена не может превышать {MaxPrice}.",
                    nameof(value), value);

            if (DecimalPlaces(value) > 2)
                throw DomainException.Create(
                    DomainErrorCode.PrecisionExceeded.ToString(),
                    "Оптовая цена не может иметь более двух десятичных знаков.",
                    nameof(value), value);
            wholesalePrice = value;
        }

        public void SetSalePrice(decimal value)
        {
            if (value < 0)
                throw DomainException.Create(
                    DomainErrorCode.ProductPriceNegative.ToString(),
                    "Цена продажи не может быть отрицательной.",
                    nameof(value), value);

            if (value > MaxPrice)
                throw DomainException.Create(
                    DomainErrorCode.ProductPriceTooLarge.ToString(),
                    $"Цена продажи не может превышать {MaxPrice}.",
                    nameof(value), value);

            if (value < wholesalePrice)
                throw DomainException.Create(
                    DomainErrorCode.ProductPriceBelowCost.ToString(),
                    "Цена продажи не может быть ниже оптовой.",
                    nameof(value), value);

            if (DecimalPlaces(value) > 2)
                throw DomainException.Create(
                    DomainErrorCode.PrecisionExceeded.ToString(),
                    "Цена продажи не может иметь более двух десятичных знаков.",
                    nameof(value), value);
            salePrice = value;
        }
        private static int DecimalPlaces(decimal x)
        {
            x = Math.Abs(x);
            int count = 0;
            while (x != Math.Truncate(x) && count < 10)
            {
                x *= 10;
                count++;
            }
            return count;
        }

        public void SetQuantity(int value)
        {
            if (value < 0)
                throw DomainException.Create(
                    DomainErrorCode.ProductQuantityNegative.ToString(),
                    "Количество не может быть отрицательным.",
                    nameof(value), value);

            if (value > MaxQuantity)
                throw DomainException.Create(
                    DomainErrorCode.ProductQuantityTooLarge.ToString(),
                    $"Количество не может превышать {MaxQuantity}.",
                    nameof(value), value);
            quantity = value;
        }

        public void SetBarcode(BarcodeVO? value) => barcode = value;

        public void SetProductUnit(Unit value)
        {
            if (!Enum.IsDefined(value))
                throw new ProductDomainException(ProductErrorCode.InvalidUnit,
                    $"Недопустимое значение Unit: {value}.", nameof(productUnit), value);
            productUnit = value;
        }

        public void SetSupplier(Supplier? supplier)
        {
            this.supplier = supplier;
            this.supplierId = supplier?.Id;
        }

        public void SetManufacturer(Manufacturer? manufacturer)
        {
            this.manufacturer = manufacturer;
            this.manufacturerId = manufacturer?.Id;
        }

        public void SetArrivalDate(DateTime value)
        {
            if (value == default)
                throw DomainException.Create(
                    DomainErrorCode.ProductArrivalDateInvalid.ToString(),
                    "Дата поступления обязательна.",
                    nameof(value), value);

            var utcNow = timeProvider.UtcNow;
            if (value > utcNow.AddMinutes(1))
                throw DomainException.Create(
                    DomainErrorCode.ProductArrivalDateInFuture.ToString(),
                    "Дата поступления не может быть в будущем.",
                    nameof(value), value);

            arrivalDate = value;
        }

        public void SetExpirationDate(DateTime? value)
        {
            if (value is DateTime dt)
            {
                if (dt < arrivalDate)
                    throw DomainException.Create(
                        DomainErrorCode.ProductExpirationBeforeArrival.ToString(),
                        "Дата годности не может быть раньше даты поступления.",
                        nameof(value), value);

                if (dt > arrivalDate.AddYears(20))
                    throw DomainException.Create(
                        DomainErrorCode.ProductExpirationTooFar.ToString(),
                        "Срок годности слишком далёк в будущем (более 20 лет).",
                        nameof(value), value);
            }

            this.expirationDate = value;
        }

        // ======== проверка инвариантов ========
        public void ValidateInvariant()
        {
            // 1) Все базовые проверки через сеттеры/конструктор VO.
            //    При любом сбое сеттер бросит DomainException с корректным кодом.
            SetName(this.name);
            SetCategory(this.category);
            SetWholesalePrice(this.wholesalePrice);
            SetSalePrice(this.salePrice);
            SetQuantity(this.quantity);

            if (this.barcode is not null)
                _ = new BarcodeVO(barcode.Value);

            SetProductUnit(productUnit);
            SetSupplier(this.supplier);
            SetManufacturer(this.manufacturer);
            SetArrivalDate(this.arrivalDate);
            SetExpirationDate(this.expirationDate);

            // 2) Специфический инвариант: если isArchived == true, то обязательно isDeleted == true
            if (isArchived && !isDeleted)
            {
                throw DomainException.Create(
                    DomainErrorCode.InvalidState.ToString(),        // или DomainErrorCode.InvariantViolation
                    "Архивный продукт должен быть помечен как удалённый.",
                    nameof(isArchived),
                    isArchived);
            }
        }

        // ======== бизнес-методы ========
        public void MarkAsDeleted() => isDeleted = true;
        public void MarkAsArchived() => isArchived = true;

        public bool IsExpired() => expirationDate is { } dt && dt < timeProvider.UtcNow;
        public bool IsAvailable() => quantity > 0 && !IsExpired() && !isDeleted && !isArchived;

        public void ReduceQuantity(int count)
        {
            if (count <= 0)
                throw DomainException.Create(
                    DomainErrorCode.InvalidValue.ToString(),
                    "Для списания количество должно быть положительным.",
                    nameof(count), count);

            if (count > quantity)
                throw DomainException.Create(
                    DomainErrorCode.OutOfRange.ToString(),
                    "Нельзя списать больше, чем есть на складе.",
                    nameof(count), count);

            quantity -= count;
        }

        public void IncreaseQuantity(int count)
        {
            if (count <= 0)
                throw DomainException.Create(
                    DomainErrorCode.InvalidValue.ToString(),
                    "Для добавления количество должно быть положительным.",
                    nameof(count), count);

            if ((long)quantity + count > MaxQuantity)
                throw DomainException.Create(
                    DomainErrorCode.Overflow.ToString(),
                    "После добавления количество превысит максимально допустимое.",
                    nameof(count), count);

            quantity += count;
        }

        // ======== helpers ========
        private static string GenerateDisplayCode()
        {
            Span<byte> bytes = stackalloc byte[4];
            RandomNumberGenerator.Fill(bytes);
            int num = BitConverter.ToInt32(bytes) & 0x7FFFFFFF % 100_000_000;
            return num.ToString("D8");
        }

        public static Product CreateTestProduct(ITimeProvider? tp = null)
        {
            // Тестовые справочники
            var supplier = new Supplier
            {
                Name = "Test supplier"
            };
            var manufacturer = new Manufacturer
            {
                Name = "Test manufacturer"
            };

            return new ProductBuilder(
                    name: "Test product",
                    category: "Default category",
                    wholesalePrice: 10m,
                    salePrice: 15m,
                    quantity: 100)
                .SetBarcode("1234567890123")
                .SetSupplier(supplier)
                .SetManufacturer(manufacturer)
                .SetArrivalDate(DateTime.UtcNow)
                .Build(tp);
        }
    }
}
