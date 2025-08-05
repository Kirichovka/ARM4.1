using ARM4.Domain.Common;
using ARM4.Domain.DomainExceptions;
using ARM4.Domain.Entities;
using ARM4.Domain.ValueObjects;
using Domain.domainexceptions;
using Domain.entities; // <-- здесь находятся Supplier и Manufacturer
using Domain.entities.Manufacturer;
using Domain.entities.Supplier;
using System;

namespace ARM4.Domain.Builders
{
    /// <summary>
    /// Флюент-билдер для создания экземпляров Product с валидацией параметров.
    /// </summary>
    public class ProductBuilder
    {
        public string Id { get; private set; } = "";
        public string DisplayCode { get; private set; } = "";

        public string Name { get; }
        public string Category { get; }
        public decimal WholesalePrice { get; }
        public decimal SalePrice { get; }
        public int Quantity { get; }

        public BarcodeVO? Barcode { get; private set; }
        public Product.Unit ProductUnit { get; private set; } = Product.Unit.Piece;
        public Supplier? Supplier { get; private set; }
        public Manufacturer? Manufacturer { get; private set; }
        public DateTime ArrivalDate { get; private set; } = DateTime.MinValue;
        public DateTime? ExpirationDate { get; private set; }

        public ProductBuilder(
            string name,
            string category,
            decimal wholesalePrice,
            decimal salePrice,
            int quantity)
        {
            Name = name;
            Category = category;
            WholesalePrice = wholesalePrice;
            SalePrice = salePrice;
            Quantity = quantity;
        }

        public ProductBuilder SetId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(
                    "Id продукта не может быть пустым.",
                    DomainErrorCode.InvalidValue.ToString(),
                    field: nameof(Id),
                    value: value);

            if (value.Length > 64)
                throw new DomainException(
                    "Id продукта слишком длинный (максимум 64).",
                    DomainErrorCode.InvalidLength.ToString(),
                    field: nameof(Id),
                    value: value);

            Id = value;
            return this;
        }

        public ProductBuilder SetDisplayCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(
                    "DisplayCode не может быть пустым.",
                    DomainErrorCode.InvalidValue.ToString(),
                    field: nameof(DisplayCode),
                    value: value);

            if (value.Length > 32)
                throw new DomainException(
                    "DisplayCode слишком длинный (максимум 32).",
                    DomainErrorCode.InvalidLength.ToString(),
                    field: nameof(DisplayCode),
                    value: value);

            DisplayCode = value;
            return this;
        }

        /// <summary>
        /// Устанавливает штрихкод для продукта (опционально).
        /// </summary>
        public ProductBuilder SetBarcode(string? barcode)
        {
            Barcode = string.IsNullOrWhiteSpace(barcode)
                ? null
                : new BarcodeVO(barcode);
            return this;
        }

        public ProductBuilder SetProductUnit(Product.Unit unit)
        {
            ProductUnit = unit;
            return this;
        }

        /// <summary>
        /// Устанавливает поставщика (ключ-справочник).
        /// </summary>
        public ProductBuilder SetSupplier(Supplier? supplier)
        {
            Supplier = supplier;
            return this;
        }

        /// <summary>
        /// Устанавливает производителя (ключ-справочник).
        /// </summary>
        public ProductBuilder SetManufacturer(Manufacturer? manufacturer)
        {
            Manufacturer = manufacturer;
            return this;
        }

        public ProductBuilder SetArrivalDate(DateTime date)
        {
            ArrivalDate = date;
            return this;
        }

        public ProductBuilder SetExpirationDate(DateTime? date)
        {
            ExpirationDate = date;
            return this;
        }

        /// <summary>
        /// Собирает доменную сущность Product из параметров билдера.
        /// </summary>
        public Product Build(ITimeProvider? timeProvider = null)
        {
            return new Product(this, timeProvider);
        }
    }
}
