using System;
using Xunit;
using ARM4.Domain.ValueObjects;

namespace ARM4.Tests.ValueObjects
{
    public class BarcodeVOTests
    {
        [Theory]
        [InlineData("0000000000000")]
        [InlineData("1234567890123")]
        [InlineData("9999999999999")]
        public void Constructor_ValidEan13_DoesNotThrow(string value)
        {
            var vo = new BarcodeVO(value);
            Assert.Equal(value, vo.Value);
            Assert.Equal(value, vo.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("             ")]
        public void Constructor_NullOrEmpty_ThrowsArgumentException(string value)
        {
            var ex = Assert.Throws<ArgumentException>(() => new BarcodeVO(value!));
            Assert.Contains("не может быть пустым", ex.Message);
        }

        [Theory]
        [InlineData("123")]                // слишком коротко
        [InlineData("12345678901234")]     // слишком длинно
        [InlineData("ABCDEFGHIJKLM")]      // буквы
        [InlineData("12345-7890123")]      // дефис
        public void Constructor_InvalidFormat_ThrowsArgumentException(string value)
        {
            var ex = Assert.Throws<ArgumentException>(() => new BarcodeVO(value));
            Assert.Contains("должен состоять из 13 цифр", ex.Message);
        }

        [Fact]
        public void Equals_SameValue_ReturnsTrue()
        {
            var a = new BarcodeVO("1234567890123");
            var b = new BarcodeVO("1234567890123");
            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equals_DifferentValue_ReturnsFalse()
        {
            var a = new BarcodeVO("1234567890123");
            var b = new BarcodeVO("0000000000000");
            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void Equals_ObjectOverride_WorksCorrectly()
        {
            var a = new BarcodeVO("1234567890123");
            object b = new BarcodeVO("1234567890123");
            object c = "some string";

            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
        }
    }
}
