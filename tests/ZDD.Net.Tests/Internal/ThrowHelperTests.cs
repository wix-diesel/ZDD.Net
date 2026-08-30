using System;
using Xunit;
using ZDD.Net.Internal;

namespace ZDD.Net.Tests.Internal
{
    public class ThrowHelperTests
    {
        [Fact]
        public void ThrowIfNullThrowsForNullArgument()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => ThrowHelper.ThrowIfNull(null, "argument"));

            Assert.Equal("argument", exception.ParamName);
        }

        [Fact]
        public void ThrowIfNullDoesNotThrowForNonNullArgument()
        {
            ThrowHelper.ThrowIfNull(new object(), "argument");
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ThrowIfNegativeThrowsForNegativeValues(int value)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrowHelper.ThrowIfNegative(value, "value"));

            Assert.Equal("value", exception.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(int.MaxValue)]
        public void ThrowIfNegativeDoesNotThrowForNonNegativeValues(int value)
        {
            ThrowHelper.ThrowIfNegative(value, "value");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ThrowIfNegativeOrZeroThrowsForZeroOrNegativeValues(int value)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrowHelper.ThrowIfNegativeOrZero(value, "value"));

            Assert.Equal("value", exception.ParamName);
        }

        [Fact]
        public void ThrowIfNegativeOrZeroDoesNotThrowForPositiveValues()
        {
            ThrowHelper.ThrowIfNegativeOrZero(1, "value");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(6)]
        public void ThrowIfNotPositivePowerOfTwoThrowsForInvalidValues(int value)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrowHelper.ThrowIfNotPositivePowerOfTwo(value, "value"));

            Assert.Equal("value", exception.ParamName);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(1024)]
        public void ThrowIfNotPositivePowerOfTwoDoesNotThrowForValidValues(int value)
        {
            ThrowHelper.ThrowIfNotPositivePowerOfTwo(value, "value");
        }

        [Fact]
        public void ThrowArgumentNullExceptionThrowsWithGivenParamName()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => ThrowHelper.ThrowArgumentNullException("argument"));

            Assert.Equal("argument", exception.ParamName);
        }

        [Fact]
        public void ThrowArgumentOutOfRangeExceptionThrowsWithGivenParamNameAndMessage()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrowHelper.ThrowArgumentOutOfRangeException("argument", "must be positive"));

            Assert.Equal("argument", exception.ParamName);
            Assert.Contains("must be positive", exception.Message);
        }

        [Fact]
        public void ThrowInvalidOperationExceptionThrowsWithGivenMessage()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ThrowHelper.ThrowInvalidOperationException("not allowed"));

            Assert.Equal("not allowed", exception.Message);
        }

        [Fact]
        public void ThrowObjectDisposedExceptionThrowsWithGivenObjectName()
        {
            ObjectDisposedException exception = Assert.Throws<ObjectDisposedException>(
                () => ThrowHelper.ThrowObjectDisposedException("ZddManager"));

            Assert.Equal("ZddManager", exception.ObjectName);
        }
    }
}
