using ARM4.Domain.Entities;
using Domain.domainexceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Domain.entities.Supplier
{
    public class Supplier
    {
        public int Id { get; set; }
        public string name { get; set; } = string.Empty;
        private string? phone;
        private string? email;

        public DateTime DateAdded { get; private set; }
        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<SupplierHistory> History { get; set; } = new List<SupplierHistory>();
        public ICollection<Category.Category> SuppliedCategories { get; set; } = new List<Category.Category>();
        public ICollection<Manufacturer.Manufacturer> Brands { get; set; } = new List<Manufacturer.Manufacturer>();

        public Supplier()
        {
            DateAdded = DateTime.UtcNow;
        }

        public string Name
        {
            get => name;
            set
            {
                if (value is null)
                    throw DomainException.Create(
                        DomainErrorCode.SupplierNameNullOrEmpty.ToString(),
                        "Имя поставщика не может быть null.",
                        nameof(Name), value);
                var trimmed = value.Trim();
                if (trimmed.Length == 0)
                    throw DomainException.Create(
                        DomainErrorCode.SupplierNameNullOrEmpty.ToString(),
                        "Имя поставщика не может быть пустым.",
                        nameof(Name), value);
                if (trimmed.Length > 100)
                    throw DomainException.Create(
                        DomainErrorCode.InvalidLength.ToString(),
                        "Имя поставщика слишком длинное (максимум 100 символов).", nameof(Name), value);
                if (trimmed.Contains("  "))
                    throw DomainException.Create(
                        DomainErrorCode.InvalidFormat.ToString(),
                        "Имя поставщика не должно содержать подряд идущие пробелы.", nameof(Name), value);
                if (trimmed.Any(char.IsControl))
                    throw DomainException.Create(
                        DomainErrorCode.InvalidFormat.ToString(),
                        "Имя поставщика не должно содержать управляющие символы.", nameof(Name), value);
                var forbidden = new[] { '@', '#', '$', '%', '^', '&', '*', '<', '>', ';', '=', '\\', '/', '"', '\'' };
                if (trimmed.IndexOfAny(forbidden) >= 0)
                    throw DomainException.Create(
                        DomainErrorCode.InvalidFormat.ToString(),
                        "Имя поставщика содержит недопустимые символы.", nameof(Name), value);
                name = trimmed;
            }
        }

        public string? Phone
        {
            get => phone;
            set
            {
                if (value is null || value.Trim().Length == 0)
                {
                    phone = null;
                    return;
                }
                var cleaned = Regex.Replace(value, "[^0-9+]", "");
                if (!Regex.IsMatch(cleaned, "^\\+?[1-9][0-9]{9,14}$"))
                    throw DomainException.Create(
                        DomainErrorCode.InvalidFormat.ToString(),
                        "Телефон в формате E.164 (10–15 цифр, необязательный '+').", nameof(Phone), value);
                phone = cleaned;
            }
        }

        public string? Email
        {
            get => email;
            set
            {
                if (value is null || value.Trim().Length == 0)
                {
                    email = null;
                    return;
                }
                try
                {
                    var addr = new MailAddress(value);
                    var host = addr.Host;
                    if (!host.Contains('.') || host.StartsWith("-") || host.EndsWith("-"))
                        throw new FormatException();
                    email = addr.Address;
                }
                catch (FormatException)
                {
                    throw DomainException.Create(
                        DomainErrorCode.InvalidFormat.ToString(),
                        "Неверный формат электронной почты.", nameof(Email), value);
                }
            }
        }

        public int SuppliedItemCount => Products.Count;

        public void ValidateInvariant()
        {
            // Имя
            Name = name;
            // Телефон/почта
            if (phone != null) Phone = phone;
            if (email != null) Email = email;
            // Дата добавления не может быть в будущем
            if (DateAdded > DateTime.UtcNow)
                throw DomainException.Create(
                    DomainErrorCode.InvalidValue.ToString(),
                    "Дата добавления не может быть в будущем.", nameof(DateAdded), DateAdded);
            // Проверка категорий
            if (SuppliedCategories.Any(c => c == null))
                throw DomainException.Create(
                    DomainErrorCode.RequiredFieldMissing.ToString(),
                    "Список категорий не может содержать null.", nameof(SuppliedCategories), SuppliedCategories);
            if (SuppliedCategories.Select(c => c.Id).Distinct().Count() != SuppliedCategories.Count)
                throw DomainException.Create(
                    DomainErrorCode.NotUnique.ToString(),
                    "Категории поставщика должны быть уникальны.", nameof(SuppliedCategories), SuppliedCategories);
            // Проверка брендов
            if (Brands.Any(b => b == null))
                throw DomainException.Create(
                    DomainErrorCode.RequiredFieldMissing.ToString(),
                    "Список брендов не может содержать null.", nameof(Brands), Brands);
            if (Brands.Select(b => b.Id).Distinct().Count() != Brands.Count)
                throw DomainException.Create(
                    DomainErrorCode.NotUnique.ToString(),
                    "Бренды поставщика должны быть уникальны.", nameof(Brands), Brands);
        }
    }
}
