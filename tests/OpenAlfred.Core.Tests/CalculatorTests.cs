using OpenAlfred.Core.Calculation;
using Xunit;

namespace OpenAlfred.Core.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("1+2*3", 7)]                 // 优先级
    [InlineData("(1+2)*3", 9)]               // 括号
    [InlineData("10/4", 2.5)]                // 真除法
    [InlineData("2^10", 1024)]               // 幂
    [InlineData("2^3^2", 512)]               // 右结合
    [InlineData("-5+3", -2)]                 // 一元负号
    [InlineData("7%3", 1)]                   // 取模
    [InlineData("50%", 0.5)]                 // 后缀百分号
    [InlineData("20% of 80", 16)]            // 百分数换算
    [InlineData("sqrt(16)", 4)]
    [InlineData("sin(0)", 0)]
    [InlineData("log(1000)", 3)]
    [InlineData("1e3+1", 1001)]              // 科学计数法
    [InlineData("pi*2", 2 * Math.PI)]
    public void Evaluates_Expressions(string expression, double expected)
    {
        var result = Calculator.Evaluate(expression);
        Assert.Equal(CalcStatus.Ok, result.Status);
        Assert.Equal(expected, result.Value, 6);
    }

    [Theory]
    [InlineData("1+")]                       // 尾部运算符
    [InlineData("(1+2")]                     // 括号未闭合
    [InlineData("")]
    [InlineData("sqrt(")]
    public void Incomplete_Expressions_Are_Silent(string expression)
    {
        Assert.Equal(CalcStatus.Incomplete, Calculator.Evaluate(expression).Status);
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("2 @ 3")]
    [InlineData("foo(3)")]
    [InlineData("sqrt(-1)")]
    public void Bad_Expressions_Report_Error(string expression)
    {
        var result = Calculator.Evaluate(expression);
        Assert.Equal(CalcStatus.Error, result.Status);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(448, "448")]
    [InlineData(2.5, "2.5")]
    [InlineData(1.0 / 3, "0.3333333333")]
    public void Formats_Numbers_Cleanly(double value, string expected)
    {
        Assert.Equal(expected, Calculator.Format(value));
    }

    [Theory]
    [InlineData("1+2*3", true)]
    [InlineData("sqrt(9)", true)]
    [InlineData("hello", false)]
    [InlineData("readme.txt", false)]
    public void Detects_Expression_Looking_Input(string input, bool expected)
    {
        Assert.Equal(expected, Calculator.LooksLikeExpression(input));
    }
}
