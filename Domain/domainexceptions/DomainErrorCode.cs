using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.domainexceptions
{
    public enum DomainErrorCode
    {
        // Общие
        Unknown,
        NullValue,
        InvalidType,
        InvalidLength,
        NotUnique,
        ForbiddenSymbol,
        InvalidFormat,
        InvalidState,
        ValidationFailed,
        NotFound,
        AlreadyExists,
        RequiredFieldMissing,
        PrecisionExceeded,
        PermissionDenied,
        InvariantViolation,
        Overflow,
        OutOfRange,
        InvalidValue,


        // Supplier-specific
        SupplierNameNullOrEmpty,
        SupplierNameTooLong,
        SupplierNameInvalidChars,
        SupplierDuplicate,

        // Product-specific
        ProductNameInvalid,
        ProductCategoryInvalid,
        ProductPriceNegative,
        ProductPriceTooLarge,
        ProductPricePrecisionExceeded,
        ProductPriceBelowCost,
        ProductQuantityNegative,
        ProductQuantityTooLarge,
        ProductQuantityOverflow,
        ProductArrivalDateInvalid,
        ProductArrivalDateInFuture,
        ProductExpirationBeforeArrival,
        ProductExpirationTooFar,

        // Manufacturer-specific
        ManufacturerNameNullOrEmpty,
        ManufacturerNameTooLong,
        ManufacturerNameInvalidChars,
        ManufacturerDuplicate,

        // Category-specific
        CategoryNameNullOrEmpty,
        CategoryNameTooLong,
        CategoryPathInvalid,
        CategoryHierarchyLoop,
        CategoryHistoryNotFound,

        // Product-specific (могут перекрываться с твоими ProductErrorCode)
        ProductNameNullOrEmpty,
        ProductNameTooLong,
        ProductQuantityInvalid,
        ProductBarcodeInvalid,
        ProductPriceInvalid,
        // Barcode-specific
        BarcodeNullOrEmpty,
        BarcodeFormatInvalid,
        BarcodeChecksumInvalid,
    }

}
