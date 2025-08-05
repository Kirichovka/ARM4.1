using ARM4.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.entities.Category
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = "";
        public int? ParentCategoryId { get; set; }
        public Category? ParentCategory { get; set; }
        public ICollection<Category> Children { get; set; } = new List<Category>();
        public DateTime CreatedAt { get; set; }
        public ICollection<Product> Products { get; set; } = new List<Product>();

        public int ProductCount { get; set; }
        public ICollection<CategoryHistory> History { get; set; } = new List<CategoryHistory>();

        public Category()
        {
            CreatedAt = DateTime.UtcNow;
        }

    }
}
