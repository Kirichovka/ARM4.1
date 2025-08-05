using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ARM4.Domain.Common
{
    public class OperationResult
    {
        public bool IsSuccess { get; private set; }

        protected readonly List<string> _errors = new();
        public IReadOnlyList<string> Errors => _errors.AsReadOnly();

        protected readonly List<string> _errorCodes = new();
        public IReadOnlyList<string> ErrorCodes => _errorCodes.AsReadOnly();

        private readonly ILogger<OperationResult>? _logger;

        // Конструктор с опциональным логгером (можно внедрять через DI)
        public OperationResult(ILogger<OperationResult>? logger = null)
        {
            IsSuccess = true;
            _logger = logger;
        }

        protected OperationResult(bool success, ILogger<OperationResult>? logger = null)
        {
            IsSuccess = success;
            _logger = logger;
        }

        public void AddError(string errorMessage, string? errorCode = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return;

            // Добавляем сообщение, если его ещё нет
            if (!_errors.Contains(errorMessage))
                _errors.Add(errorMessage);

            // Добавляем код, если он не пустой и ещё не добавлен
            if (!string.IsNullOrWhiteSpace(errorCode) && !_errorCodes.Contains(errorCode))
                _errorCodes.Add(errorCode);

            IsSuccess = false;

            if (_logger != null)
            {
                // Используем структурированные плейсхолдеры у Microsoft ILogger
                _logger.LogError(
                    "Operation failed. ErrorCode: {ErrorCode}. Message: {ErrorMessage}",
                    errorCode ?? "NO_CODE",
                    errorMessage);
            }
            else
            {
                // Фоллбэк через конкатенацию, чтобы не было $ и \ 
                string codeToShow = errorCode ?? "NO_CODE";
                Console.Error.WriteLine("[Error:" + codeToShow + "] " + errorMessage);
            }
        }
        public void Merge(OperationResult other)
        {
            if (other == null) return;

            // Проходим по парам «сообщение ↔ код», сохраняя связь
            int count = Math.Max(other.Errors.Count, other.ErrorCodes.Count);
            for (int i = 0; i < count; i++)
            {
                string msg = i < other.Errors.Count
                    ? other.Errors[i]
                    : "Unknown error";

                string? code = i < other.ErrorCodes.Count
                    ? other.ErrorCodes[i]
                    : null;

                // Добавит и сообщение, и код одновременно
                AddError(msg, code);
            }

            // Если в исходном результате были ошибки — наш результат тоже неудачен
            if (!other.IsSuccess)
                IsSuccess = false;
        }


        public static OperationResult Success(ILogger<OperationResult>? logger = null) => new(true, logger);

        public static OperationResult Failure(IEnumerable<(string Message, string? Code)> errors, ILogger<OperationResult>? logger = null)
        {
            var result = new OperationResult(false, logger);
            foreach (var (msg, code) in errors)
                result.AddError(msg, code);
            return result;
        }

        public static OperationResult Failure(string message, string? code = null, ILogger<OperationResult>? logger = null)
        {
            var result = new OperationResult(false, logger);
            result.AddError(message, code);
            return result;
        }

        protected virtual void LogError(string? message, string? code)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    if (_logger != null)
                    {
                        // Передаем код ошибки и сообщение как параметры
                        _logger.LogError("Ошибка операции. Код: {ErrorCode}. Сообщение: {Message}",
                            code ?? "NO_CODE", message);
                    }
                    else
                    {
                        Console.Error.WriteLine($"[Error{(code != null ? $":{code}" : "")}] {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LoggerFailure] Failed to log error: {ex}");
            }
        }



        public static implicit operator bool(OperationResult result) => result.IsSuccess;

        public override string ToString()
        {
            if (IsSuccess) return "Success";

            var sb = new StringBuilder();
            sb.AppendLine("Operation failed with errors:");

            for (int i = 0; i < _errors.Count; i++)
            {
                string code = i < _errorCodes.Count ? _errorCodes[i] : "NO_CODE";
                sb.AppendLine($"- [{code}] {_errors[i]}");
            }

            return sb.ToString();
        }
    }

    public class OperationResult<T> : OperationResult
    {
        public T? Value { get; private set; }

        public OperationResult(ILogger<OperationResult>? logger = null) : base(logger) { }

        protected OperationResult(bool success, ILogger<OperationResult>? logger = null, T? value = default) : base(success, logger)
        {
            Value = value;
        }

        public static OperationResult<T> Success(T value, ILogger<OperationResult>? logger = null) => new(true, logger, value);

        public new static OperationResult<T> Failure(
    IEnumerable<(string Message, string? Code)> errors,
    ILogger<OperationResult>? logger = null)
        {
            // 1. Валидация входных данных
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            // 2. Генерация CorrelationId
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            // 3. Прологовое логирование каждой ошибки
            foreach (var (msg, code) in errors)
            {
                if (string.IsNullOrWhiteSpace(msg))
                    continue;

                if (logger != null)
                {
                    logger.LogError(
                        "OperationResult<{TypeName}> FailureEntry. CorrelationId={CorrelationId}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                        typeof(T).Name,
                        correlationId,
                        code ?? "NO_CODE",
                        msg);
                }
                else
                {
                    // Фоллбэк в консоль
                    Console.Error.WriteLine(
                        "[" + DateTime.UtcNow.ToString("O") + "] "
                      + "[CorrelationId:" + correlationId + "] "
                      + "[Error:" + (code ?? "NO_CODE") + "] "
                      + msg);
                }
            }

            // 4. Создание и заполнение OperationResult<T>
            var result = new OperationResult<T>(false, logger);
            foreach (var (msg, code) in errors)
            {
                if (!string.IsNullOrWhiteSpace(msg))
                    result.AddError(msg, code);
            }

            return result;
        }

        public new static OperationResult<T> Failure(
            string message,
            string? code = null,
            ILogger<OperationResult>? logger = null)
        {
            // 1. Валидация
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Error message must be provided", nameof(message));

            // 2. Корреляционный идентификатор для трассировки
            string correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            // 3. Логирование факта создания Failure
            if (logger != null)
            {
                logger.LogError(
                    "OperationResult<{Type}> Failure. CorrelationId={CorrelationId}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                    typeof(T).Name,
                    correlationId,
                    code ?? "NO_CODE",
                    message);
            }
            else
            {
                // Фоллбэк в консоль: ISO-время + корреляция + код + сообщение
                Console.Error.WriteLine(
                    "[" + DateTime.UtcNow.ToString("O") + "] "
                  + "[CorrelationId:" + correlationId + "] "
                  + "[Error:" + (code ?? "NO_CODE") + "] "
                  + message);
            }

            // 4. Создаём результат и добавляем ошибку
            var result = new OperationResult<T>(false, logger);
            result.AddError(message, code);

            return result;
        }

    }
}
