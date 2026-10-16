using E_commerce_backend.Examples;

namespace E_commerce_backend.Tests.Unit;

public class CalculatorTests
{
    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(-2, 3, 1)]
    [InlineData(0, 0, 0)]
    public void Add_TwoNumbers_ReturnsTheirSum(int first, int second, int expected)
    {
        var result = Calculator.Add(first, second);

        Assert.Equal(expected, result);
    }
}
