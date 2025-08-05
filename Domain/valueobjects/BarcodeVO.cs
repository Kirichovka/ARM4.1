using Domain.domainexceptions;
using System;
using System.Text.RegularExpressions;

namespace ARM4.Domain.ValueObjects
{
    /// <summary>
    /// Value Object для штрихкода продукта. 
    /// Гарантирует валидацию и сравнение по значению (EAN-13).
    /// </summary>
    public sealed class BarcodeVO : IEquatable<BarcodeVO>
    {
        /// <summary>
        /// Строковое представление штрихкода (13 цифр).
        /// </summary>
        public string Value { get; }
        
        /// <summary>
        /// Создаёт штрихкод после валидации.
        /// </summary>
        private static readonly Regex Ean13Regex = new(@"^\d{13}$", RegexOptions.Compiled);

        /// <summary>
        /// Создаёт штрихкод после валидации.
        /// </summary>
        /// <param name="value">Строка — 13 цифр (EAN-13)</param>
        /// <exception cref="ArgumentException">Если значение некорректно</exception>
        public BarcodeVO(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw DomainException.Create(
                    DomainErrorCode.BarcodeNullOrEmpty.ToString(),
                    "Штрихкод не может быть пустым.",
                    nameof(value), value);

            if (!Ean13Regex.IsMatch(value))
                throw DomainException.Create(
                    DomainErrorCode.BarcodeFormatInvalid.ToString(),
                    "Штрихкод должен содержать ровно 13 цифр (EAN-13).",
                    nameof(value), value);

            if (!ValidateEan13Checksum(value))
                throw DomainException.Create(
                    DomainErrorCode.BarcodeChecksumInvalid.ToString(),
                    "Неверная контрольная цифра EAN-13.",
                    nameof(value), value);

            Value = value;
        }
        private static bool ValidateEan13Checksum(string code)
        {
            // EAN-13: последняя цифра — контрольная.
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int digit = code[i] - '0';
                sum += (i % 2 == 0 ? digit : digit * 3);
            }
            int check = (10 - (sum % 10)) % 10;
            return check == (code[12] - '0');
        }

        public override string ToString() => Value;

        public override bool Equals(object? obj) => Equals(obj as BarcodeVO);
        /// <summary>
        /// Сравнивает два штрихкода по значению.
        /// </summary>
        public bool Equals(BarcodeVO? other) => other is not null && Value == other.Value;

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(BarcodeVO? left, BarcodeVO? right) => Equals(left, right);
        public static bool operator !=(BarcodeVO? left, BarcodeVO? right) => !Equals(left, right);
    }
}
