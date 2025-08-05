// tests/ARM4.Tests/Common/OperationResultTests.cs
using System;
using System.Collections.Generic;
using Xunit;
using ARM4.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace ARM4.Tests.Common
{
    public class OperationResultTests
    {
        [Fact]
        public void Success_HasNoErrors_AndImplicitBoolTrue()
        {
            var result = OperationResult.Success();
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.Empty(result.ErrorCodes);
            // implicit operator bool
            Assert.True(result);
            Assert.Equal("Success", result.ToString().Trim());
        }

        [Fact]
        public void Failure_WithSingleError_SetsProperties_AndImplicitBoolFalse()
        {
            var result = OperationResult.Failure("oops", "ERR");
            Assert.False(result.IsSuccess);
            Assert.Contains("oops", result.Errors);
            Assert.Contains("ERR", result.ErrorCodes);
            Assert.False(result);
            var text = result.ToString();
            Assert.Contains("Operation failed with errors:", text);
            Assert.Contains("- oops", text);
        }

        [Fact]
        public void Failure_WithMultipleErrors_SetsAll()
        {
            var errors = new (string Message, string? Code)[]
                {
                    (Message: "e1", Code: "C1"),
                    (Message: "e2", Code: "C2")
                };
            var result = OperationResult.Failure(errors);

            Assert.False(result);
            Assert.Equal(2, result.Errors.Count);
            Assert.Equal(2, result.ErrorCodes.Count);
        }

        [Fact]
        public void AddError_DoesNotDuplicateMessagesOrCodes()
        {
            var result = OperationResult.Success();
            result.AddError("dup", "C");
            result.AddError("dup", "C");
            Assert.False(result.IsSuccess);
            Assert.Single(result.Errors);
            Assert.Single(result.ErrorCodes);
        }

        [Fact]
        public void Merge_CombinesErrorsAndCodes()
        {
            var r1 = OperationResult.Failure("first", "CODE1");
            var r2 = OperationResult.Failure("second", "CODE2");
            r1.Merge(r2);
            Assert.False(r1);
            Assert.Contains("first", r1.Errors);
            Assert.Contains("second", r1.Errors);
            Assert.Contains("CODE1", r1.ErrorCodes);
            Assert.Contains("CODE2", r1.ErrorCodes);
        }
    }

    public class OperationResultOfTests
    {
        [Fact]
        public void GenericSuccess_HasValue_NoErrors()
        {
            var value = 42;
            var result = OperationResult<int>.Success(value);
            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
            Assert.Empty(result.Errors);
            Assert.Empty(result.ErrorCodes);
            Assert.True(result);
        }

        [Fact]
        public void GenericFailure_HasNoValue_AndErrors()
        {
            var result = OperationResult<string>.Failure("fail", "CODE");
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Contains("fail", result.Errors);
            Assert.Contains("CODE", result.ErrorCodes);
            Assert.False(result);
        }

        [Fact]
        public void GenericMerge_CombinesErrorsAndCodes()
        {
            var r1 = OperationResult<int>.Failure("e1", "C1");
            var r2 = OperationResult<int>.Failure("e2", "C2");
            r1.Merge(r2);
            Assert.False(r1);
            Assert.Contains("e1", r1.Errors);
            Assert.Contains("e2", r1.Errors);
            Assert.Contains("C1", r1.ErrorCodes);
            Assert.Contains("C2", r1.ErrorCodes);
        }
    }
}
