using FluentAssertions;

namespace SportRental.Admin.Tests;

public class SampleTests
{
    [Fact]
    public void BasicTest_ShouldPass()
    {
        // Arrange
        var expected = 4;

        // Act
        var result = 2 + 2;

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 3, 5)]
    [InlineData(-1, 1, 0)]
    public void Addition_ShouldReturnCorrectSum(int a, int b, int expected)
    {
        // Act
        var result = a + b;

        // Assert
        result.Should().Be(expected);
    }
}
