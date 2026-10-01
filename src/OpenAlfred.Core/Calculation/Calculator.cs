using System.Globalization;

namespace OpenAlfred.Core.Calculation;

public enum CalcStatus
{
    /// <summary>求值成功。</summary>
    Ok,
    /// <summary>表达式不完整（如以运算符结尾、括号未闭合），应静默不显示结果。</summary>
    Incomplete,
    /// <summary>语义错误（除零、未知函数等），显示红色提示。</summary>
    Error,
}

public readonly record struct CalcResult(CalcStatus Status, double Value, string? Error);

/// <summary>
/// 递归下降表达式计算器。
/// 文法：expr := term (('+'|'-') term)*
///       term := unary (('*'|'/'|'%'|'of') unary)*
///       unary := '-'? power
///       power := postfix ('^' power)?        // 右结合
///       postfix := primary '%'*              // 百分号后缀，x% = x/100
///       primary := number | const | func '(' expr ')' | '(' expr ')'
/// </summary>
public static class Calculator
{
    private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sin"] = Math.Sin,
        ["cos"] = Math.Cos,
        ["tan"] = Math.Tan,
        ["sqrt"] = Math.Sqrt,
        ["abs"] = Math.Abs,
        ["ln"] = Math.Log,
        ["log"] = Math.Log10,
        ["floor"] = Math.Floor,
        ["ceil"] = Math.Ceiling,
        ["round"] = Math.Round,
    };

    private static readonly Dictionary<string, double> Constants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pi"] = Math.PI,
        ["e"] = Math.E,
    };

    /// <summary>判断输入是否像算式（含数字且含运算符/函数/括号），供路由决策使用。</summary>
    public static bool LooksLikeExpression(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        if (!input.Any(char.IsDigit) && !Constants.Keys.Any(input.Contains)) return false;
        return input.Any(c => "+-*/%^()".Contains(c))
            || Functions.Keys.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase))
            || input.Contains("of", StringComparison.OrdinalIgnoreCase);
    }

    public static CalcResult Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new CalcResult(CalcStatus.Incomplete, 0, null);

        try
        {
            var parser = new Parser(expression);
            var value = parser.ParseExpression();
            parser.ExpectEnd();
            if (double.IsNaN(value) || double.IsInfinity(value))
                return new CalcResult(CalcStatus.Error, 0, "结果不是有限数值");
            return new CalcResult(CalcStatus.Ok, value, null);
        }
        catch (CalcParseException ex) when (ex.Incomplete)
        {
            return new CalcResult(CalcStatus.Incomplete, 0, null);
        }
        catch (CalcParseException ex)
        {
            return new CalcResult(CalcStatus.Error, 0, ex.Message);
        }
    }

    /// <summary>把数值格式化为易读文本：整数去掉小数尾巴，超大/超小用科学计数法。</summary>
    public static string Format(double value)
    {
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture);

        var abs = Math.Abs(value);
        if (abs >= 1e15 || (abs > 0 && abs < 1e-9))
            return value.ToString("G6", CultureInfo.InvariantCulture);

        // 最多 10 位有效小数并去除尾零
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private sealed class CalcParseException : Exception
    {
        public bool Incomplete { get; }
        public CalcParseException(string message, bool incomplete = false) : base(message)
            => Incomplete = incomplete;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text) => _text = text;

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        private bool Eof
        {
            get { SkipWhitespace(); return _pos >= _text.Length; }
        }

        public void ExpectEnd()
        {
            if (!Eof)
                throw new CalcParseException($"无法识别的字符 '{_text[_pos]}'");
        }

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    value *= ParseUnary();
                }
                else if (Match('/'))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0) throw new CalcParseException("除数不能为 0");
                    value /= divisor;
                }
                else if (_pos < _text.Length && _text[_pos] == '%' && PeekIsModulo())
                {
                    _pos++;
                    var modulus = ParseUnary();
                    if (modulus == 0) throw new CalcParseException("取模的除数不能为 0");
                    value %= modulus;
                }
                else if (MatchWord("of"))
                {
                    // "20% of 80" 形态：左侧已是百分数值
                    value *= ParseUnary();
                }
                else return value;
            }
        }

        /// <summary>% 后接数字且后面还有内容时视为二元取模（7%2），否则是后缀百分号。</summary>
        private bool PeekIsModulo()
        {
            var i = _pos + 1;
            while (i < _text.Length && char.IsWhiteSpace(_text[i])) i++;
            return i < _text.Length && (char.IsDigit(_text[i]) || _text[i] == '(');
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Match('-')) return -ParseUnary();
            if (Match('+')) return ParseUnary();
            return ParsePower();
        }

        private double ParsePower()
        {
            var baseValue = ParsePostfix();
            SkipWhitespace();
            if (Match('^'))
                return Math.Pow(baseValue, ParseUnary()); // 右结合
            return baseValue;
        }

        private double ParsePostfix()
        {
            var value = ParsePrimary();
            SkipWhitespace();
            while (_pos < _text.Length && _text[_pos] == '%' && !PeekIsModuloAfterValue())
            {
                _pos++;
                value /= 100;
                SkipWhitespace();
            }
            return value;
        }

        /// <summary>已有左值时，"a % b" 的 % 交给 ParseTerm 处理为取模。</summary>
        private bool PeekIsModuloAfterValue() => PeekIsModulo();

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Eof) throw new CalcParseException("表达式不完整", incomplete: true);

            if (Match('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (_pos >= _text.Length) throw new CalcParseException("缺少右括号", incomplete: true);
                if (!Match(')')) throw new CalcParseException("缺少右括号");
                return value;
            }

            if (char.IsDigit(_text[_pos]) || _text[_pos] == '.')
                return ParseNumber();

            if (char.IsLetter(_text[_pos]))
                return ParseIdentifier();

            throw new CalcParseException($"无法识别的字符 '{_text[_pos]}'");
        }

        private double ParseNumber()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                _pos++;

            // 科学计数法 1e3 / 2.5e-4
            if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                var save = _pos;
                _pos++;
                if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                if (_pos < _text.Length && char.IsDigit(_text[_pos]))
                {
                    while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                }
                else
                {
                    _pos = save;
                }
            }

            var token = _text[start.._pos];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new CalcParseException($"无法解析数字 \"{token}\"");
            return value;
        }

        private double ParseIdentifier()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                _pos++;
            var name = _text[start.._pos];

            if (Constants.TryGetValue(name, out var constant))
                return constant;

            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '(')
            {
                if (!Functions.TryGetValue(name, out var function))
                    throw new CalcParseException($"未知函数 \"{name}\"");
                _pos++;
                var argument = ParseExpression();
                SkipWhitespace();
                if (_pos >= _text.Length) throw new CalcParseException("缺少右括号", incomplete: true);
                if (!Match(')')) throw new CalcParseException("缺少右括号");
                var result = function(argument);
                if (double.IsNaN(result))
                    throw new CalcParseException($"函数 \"{name}\" 的参数超出定义域");
                return result;
            }

            throw new CalcParseException($"未知标识符 \"{name}\"");
        }

        private bool Match(char c)
        {
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private bool MatchWord(string word)
        {
            SkipWhitespace();
            if (_pos + word.Length > _text.Length) return false;
            if (string.Compare(_text, _pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            var after = _pos + word.Length;
            if (after < _text.Length && char.IsLetterOrDigit(_text[after])) return false;
            _pos = after;
            return true;
        }
    }
}
