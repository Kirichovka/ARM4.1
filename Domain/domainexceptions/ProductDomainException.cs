using System;

namespace ARM4.Domain.DomainExceptions
{
    /// <summary>
    /// Коды ошибок для доменной сущности Product.
    /// Используется в ProductDomainException для точной идентификации причин сбоя.
    /// </summary>
    public enum ProductErrorCode
    {
        Unknown,
        InvalidName,
        InvalidCategory,
        InvalidWholesalePrice,
        InvalidSalePrice,
        InvalidQuantity,
        InvalidBarcode,
        DuplicateBarcode,
        InvalidSupplier,
        InvalidManufacturer,
        InvalidUnit,
        InvalidArrivalDate,
        InvalidExpirationDate,
        InvalidOperation,
        NotEnoughStock,
        ProductArchived,
        ProductDeleted,
        InactiveProduct,
        UnavailableProduct,
        InvalidStateTransition,
        ValidationFailed
    }

    /// <summary>
    /// Исключение доменной логики для операций с продуктом.
    /// </summary>
    public class ProductDomainException : DomainException
    {
        public ProductErrorCode Code { get; }

        public ProductDomainException(
            ProductErrorCode code,
            string message,
            string? field = null,
            object? value = null,
            Exception? innerException = null)
            : base(message, code.ToString(), field, value, innerException)
        {
            Code = code;
        }

        /// <summary>
        /// Переопределяем для расширения структурированного лога.
        /// </summary>
        public override object ToLogObject()
        {
            return new
            {
                ErrorCode = Code.ToString(),
                Field,
                Value,
                Message,
                ExceptionType = GetType().Name
            };
        }
    }
}