using ARM4.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.entities.Manufacturer
{
    public class Manufacturer
    {
        public int Id { get; set; }
        public string name = string.Empty;
        public ICollection<Product> Products { get; set; } = new List<Product>();
        // Можно добавить сайт, страну, ИНН и др.
        public ICollection<ManufacturerHistory> History { get; set; } = new List<ManufacturerHistory>();

        public string Name
        {
            get => this.name;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Имя производителя не может быть пустым.", nameof(value));
                if (value.Length > 100)
                    throw new ArgumentException("Имя производителя слишком длинное.", nameof(value));
                Name = value;
            }
        }
    }
}
