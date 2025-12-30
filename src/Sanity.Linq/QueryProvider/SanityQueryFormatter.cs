namespace Sanity.Linq.QueryProvider;

public static class SanityQueryFormatter
{
    public static string Format(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;

        return new QueryFormatter(query).Format();
    }

    private sealed class QueryFormatter(string query)
    {
        private readonly StringBuilder _sb = new();
        private int _bracketLevel;
        private int _currentIndex;
        private int _indentLevel;
        private bool _inQuotes;
        private int _parenLevel;
        private char _quoteChar;

        public string Format()
        {
            for (_currentIndex = 0; _currentIndex < query.Length; _currentIndex++)
            {
                var c = query[_currentIndex];

                UpdateQuoteState(c);

                if (_inQuotes)
                {
                    _sb.Append(c);
                    continue;
                }

                ProcessCharacter(c);
            }

            return _sb.ToString().Trim();
        }

        private void UpdateQuoteState(char c)
        {
            if (c is not ('"' or '\'') || (_currentIndex != 0 && query[_currentIndex - 1] == '\\')) return;

            if (!_inQuotes)
            {
                _inQuotes = true;
                _quoteChar = c;
            }
            else if (c == _quoteChar)
            {
                _inQuotes = false;
            }
        }

        private void ProcessCharacter(char c)
        {
            switch (c)
            {
                case '{':
                    HandleOpenBrace();
                    break;
                case '}':
                    HandleCloseBrace();
                    break;
                case ',':
                    HandleComma();
                    break;
                case '[':
                    _bracketLevel++;
                    _sb.Append(c);
                    break;
                case ']':
                    _bracketLevel--;
                    _sb.Append(c);
                    break;
                case '(':
                    _parenLevel++;
                    _sb.Append(c);
                    break;
                case ')':
                    _parenLevel--;
                    _sb.Append(c);
                    break;
                default:
                    HandleDefault(c);
                    break;
            }
        }

        private void HandleOpenBrace()
        {
            if (TryHandleSpreadOnly()) return;

            NormalizeSpace();
            _sb.Append('{');
            AppendNewlineAndIndent(++_indentLevel);
        }

        private bool TryHandleSpreadOnly()
        {
            var nextIndex = _currentIndex + 1;
            while (nextIndex < query.Length && char.IsWhiteSpace(query[nextIndex])) nextIndex++;

            if (nextIndex + 2 >= query.Length || query[nextIndex] != '.' || query[nextIndex + 1] != '.' || query[nextIndex + 2] != '.') return false;

            var afterSpread = nextIndex + 3;
            while (afterSpread < query.Length && char.IsWhiteSpace(query[afterSpread])) afterSpread++;

            if (afterSpread >= query.Length || query[afterSpread] != '}') return false;

            NormalizeSpace();
            _sb.Append("{...}");
            _currentIndex = afterSpread;
            return true;
        }

        private void HandleCloseBrace()
        {
            TrimTrailingWhitespace();
            AppendNewlineAndIndent(--_indentLevel);
            _sb.Append('}');
        }

        private void HandleComma()
        {
            TrimTrailingWhitespace();
            _sb.Append(',');
            if (_bracketLevel != 0 || _parenLevel != 0 || _indentLevel <= 0) return;

            AppendNewlineAndIndent(_indentLevel);
            // Skip the next whitespace if we just added a newline
            if (_currentIndex + 1 < query.Length && char.IsWhiteSpace(query[_currentIndex + 1])) _currentIndex++;
        }

        private void HandleDefault(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                if (_sb.Length > 0 && !char.IsWhiteSpace(_sb[^1]) && _sb[^1] != '\n') _sb.Append(' ');
            }
            else
            {
                _sb.Append(c);
            }
        }

        private void NormalizeSpace()
        {
            var hadSpace = _sb.Length > 0 && char.IsWhiteSpace(_sb[^1]);
            TrimTrailingWhitespace();
            if (hadSpace && _sb.Length > 0 && _sb[^1] != '\n') _sb.Append(' ');
        }

        private void TrimTrailingWhitespace()
        {
            while (_sb.Length > 0 && char.IsWhiteSpace(_sb[^1])) _sb.Length--;
        }

        private void AppendNewlineAndIndent(int level)
        {
            _sb.AppendLine();
            _sb.Append(new string(' ', level * 2));
        }
    }
}