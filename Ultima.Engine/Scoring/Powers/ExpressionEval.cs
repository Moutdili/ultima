namespace Ultima.Engine.Scoring.Powers;

/// <summary>
/// Évaluateur minimal des expressions du catalogue de pouvoirs.
///
/// Grammaire supportée (et rien d'autre) :
/// <code>
///   expr   := term (('+' | '-') term)*
///   term   := factor (('*' | '/') factor)*
///   factor := ('+' | '-')? (number | 'Amount' | 'In' | '(' expr ')')
///   number := chiffres ('.' chiffres)? ('m' | 'M')?
/// </code>
///
/// <c>Amount</c> = piles du pouvoir (dégâts/bloc/énergie selon le hook), <c>In</c> = valeur
/// reçue par le hook (cartes à piocher, énergie, nombre de jets…). Aucune fonction, aucun
/// accès à un membre, aucun <c>Eval</c> dynamique : tout ce qui sort de cette grammaire est
/// refusé (<c>false</c>) — jamais remplacé par une valeur par défaut.
/// </summary>
public static class ExpressionEval
{
    /// <summary>Garde-fou : une expression de catalogue tient en quelques dizaines de caractères.</summary>
    public const int MaxLength = 400;

    /// <summary>Évalue une expression sans valeur d'entrée (<c>In</c> vaut 0).</summary>
    public static bool TryEval(string? expression, decimal amount, out decimal value) =>
        TryEval(expression, amount, 0m, out value);

    /// <summary>
    /// Évalue <paramref name="expression"/> avec <paramref name="amount"/> piles et
    /// <paramref name="input"/> en entrée. Rend <c>false</c> si l'expression est absente,
    /// mal formée, trop longue, ou contient autre chose que la grammaire ci-dessus.
    /// </summary>
    public static bool TryEval(string? expression, decimal amount, decimal input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        if (expression.Length > MaxLength) return false;

        var parser = new Parser(expression, amount, input);
        if (!parser.TryParseExpression(out value)) return false;
        return parser.AtEnd;
    }

    /// <summary>Évalue et rend <c>null</c> quand l'expression n'est pas évaluable (pas de repli).</summary>
    public static decimal? Eval(string? expression, decimal amount, decimal input = 0m) =>
        TryEval(expression, amount, input, out var value) ? value : null;

    /// <summary>Même chose en <see cref="double"/> pour le scoring, <c>null</c> si non évaluable.</summary>
    public static double? EvalDouble(string? expression, double amount, double input = 0) =>
        TryEval(expression, (decimal)amount, (decimal)input, out var value) ? (double)value : null;

    private sealed class Parser
    {
        private readonly string _text;
        private readonly decimal _amount;
        private readonly decimal _input;
        private int _pos;

        public Parser(string text, decimal amount, decimal input)
        {
            _text = text;
            _amount = amount;
            _input = input;
        }

        public bool AtEnd
        {
            get
            {
                SkipSpaces();
                return _pos >= _text.Length;
            }
        }

        public bool TryParseExpression(out decimal value)
        {
            if (!TryParseTerm(out value)) return false;
            while (true)
            {
                var op = PeekOperator('+', '-');
                if (op is null) return true;
                if (!TryParseTerm(out var right)) return false;
                value = op == '+' ? value + right : value - right;
            }
        }

        private bool TryParseTerm(out decimal value)
        {
            if (!TryParseFactor(out value)) return false;
            while (true)
            {
                var op = PeekOperator('*', '/');
                if (op is null) return true;
                if (!TryParseFactor(out var right)) return false;
                if (op == '*')
                {
                    value *= right;
                }
                else
                {
                    if (right == 0m) return false; // division par zéro : non évaluable, pas d'exception
                    value /= right;
                }
            }
        }

        private bool TryParseFactor(out decimal value)
        {
            value = 0m;
            SkipSpaces();
            if (_pos >= _text.Length) return false;

            if (_text[_pos] == '+' || _text[_pos] == '-')
            {
                var sign = _text[_pos] == '-' ? -1m : 1m;
                _pos++;
                if (!TryParseFactor(out var inner)) return false;
                value = sign * inner;
                return true;
            }

            if (_text[_pos] == '(')
            {
                _pos++;
                if (!TryParseExpression(out value)) return false;
                SkipSpaces();
                if (_pos >= _text.Length || _text[_pos] != ')') return false;
                _pos++;
                return true;
            }

            if (char.IsDigit(_text[_pos])) return TryParseNumber(out value);
            return TryParseSymbol(out value);
        }

        private bool TryParseNumber(out decimal value)
        {
            value = 0m;
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
            if (_pos < _text.Length && (_text[_pos] == 'm' || _text[_pos] == 'M')) _pos++;

            var span = _text[start.._pos];
            if (span.EndsWith("m", StringComparison.OrdinalIgnoreCase)) span = span[..^1];
            if (span.Count(c => c == '.') > 1) return false;
            return decimal.TryParse(
                span,
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private bool TryParseSymbol(out decimal value)
        {
            value = 0m;
            if (Match("Amount"))
            {
                // `AmountFoo` n'est pas un symbole connu
                if (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) return false;
                value = _amount;
                return true;
            }
            if (Match("In"))
            {
                if (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) return false;
                value = _input;
                return true;
            }
            return false;
        }

        private bool Match(string word)
        {
            if (_pos + word.Length > _text.Length) return false;
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) return false;
            _pos += word.Length;
            return true;
        }

        private char? PeekOperator(char first, char second)
        {
            SkipSpaces();
            if (_pos >= _text.Length) return null;
            var ch = _text[_pos];
            if (ch != first && ch != second) return null;
            _pos++;
            return ch;
        }

        private void SkipSpaces()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }
    }
}
