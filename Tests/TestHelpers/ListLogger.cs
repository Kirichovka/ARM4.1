using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;

namespace Tests.TestHelpers
{
    public sealed class ListLogger<T> : ILogger<T>, IDisposable
    {
        private readonly List<(LogLevel lvl, string msg)> _entries = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => this;
        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(LogLevel level, EventId id,
                                TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
            => _entries.Add((level, formatter(state, exception)));

        public void ShouldContain(LogLevel level, params string[] fragments) =>
            _entries.Any(e => e.lvl == level &&
                              fragments.All(f => e.msg.Contains(f)))
                     .Should().BeTrue($"лог {level} должен содержать: {string.Join(", ", fragments)}");

        public void Dispose() { }
    }
}
