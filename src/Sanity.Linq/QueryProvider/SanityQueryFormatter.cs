namespace Sanity.Linq.QueryProvider;

public static class SanityQueryFormatter
{
    /// <summary>
    ///     Formats a given Sanity query string by applying proper indentation and formatting rules.
    /// </summary>
    /// <param name="query">The Sanity query string to format. Can be null, empty, or contain whitespace.</param>
    /// <returns>
    ///     A formatted version of the query string with appropriate indentation.
    ///     If the input query is null, empty, or consists of whitespace, it returns the input as is.
    /// </returns>
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
            if ((c != SanityConstants.CHAR_QUOTE && c != SanityConstants.CHAR_SINGLE_QUOTE) || (_currentIndex != 0 && query[_currentIndex - 1] == SanityConstants.CHAR_BACKSLASH)) return;

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
                case SanityConstants.CHAR_OPEN_BRACE:
                    HandleOpenBrace();
                    break;
                case SanityConstants.CHAR_CLOSE_BRACE:
                    HandleCloseBrace();
                    break;
                case SanityConstants.CHAR_COMMA:
                    HandleComma();
                    break;
                case SanityConstants.CHAR_OPEN_BRACKET:
                    _bracketLevel++;
                    _sb.Append(c);
                    break;
                case SanityConstants.CHAR_CLOSE_BRACKET:
                    _bracketLevel--;
                    _sb.Append(c);
                    break;
                case SanityConstants.CHAR_OPEN_PAREN:
                    _parenLevel++;
                    _sb.Append(c);
                    break;
                case SanityConstants.CHAR_CLOSE_PAREN:
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
            _sb.Append(SanityConstants.CHAR_OPEN_BRACE);
            AppendNewlineAndIndent(++_indentLevel);
        }

        private bool TryHandleSpreadOnly()
        {
            var nextIndex = _currentIndex + 1;
            while (nextIndex < query.Length && char.IsWhiteSpace(query[nextIndex])) nextIndex++;

            if (nextIndex + 2 >= query.Length || query[nextIndex] != SanityConstants.CHAR_DOT || query[nextIndex + 1] != SanityConstants.CHAR_DOT || query[nextIndex + 2] != SanityConstants.CHAR_DOT) return false;

            var afterSpread = nextIndex + 3;
            while (afterSpread < query.Length && char.IsWhiteSpace(query[afterSpread])) afterSpread++;

            if (afterSpread >= query.Length || query[afterSpread] != SanityConstants.CHAR_CLOSE_BRACE) return false;

            NormalizeSpace();
            _sb.Append(SanityConstants.CHAR_OPEN_BRACE).Append(SanityConstants.SPACE).Append(SanityConstants.SPREAD_OPERATOR).Append(SanityConstants.SPACE).Append(SanityConstants.CHAR_CLOSE_BRACE);
            _currentIndex = afterSpread;
            return true;
        }

        private void HandleCloseBrace()
        {
            TrimTrailingWhitespace();
            AppendNewlineAndIndent(--_indentLevel);
            _sb.Append(SanityConstants.CHAR_CLOSE_BRACE);
        }

        private void HandleComma()
        {
            TrimTrailingWhitespace();
            _sb.Append(SanityConstants.CHAR_COMMA);
            if (_bracketLevel != 0 || _parenLevel != 0 || _indentLevel <= 0) return;

            AppendNewlineAndIndent(_indentLevel);
            // Skip the next whitespace if we just added a newline
            if (_currentIndex + 1 < query.Length && char.IsWhiteSpace(query[_currentIndex + 1])) _currentIndex++;
        }

        private void HandleDefault(char c)
        {
            if (!char.IsWhiteSpace(c))
            {
                _sb.Append(c);
                return;
            }

            if (_sb.Length > 0 && !char.IsWhiteSpace(_sb[^1]) && _sb[^1] != SanityConstants.CHAR_NEWLINE) _sb.Append(SanityConstants.CHAR_SPACE);
        }

        private void NormalizeSpace()
        {
            var hadSpace = _sb.Length > 0 && char.IsWhiteSpace(_sb[^1]);
            TrimTrailingWhitespace();
            if (hadSpace && _sb.Length > 0 && _sb[^1] != SanityConstants.CHAR_NEWLINE) _sb.Append(SanityConstants.CHAR_SPACE);
        }

        private void TrimTrailingWhitespace()
        {
            while (_sb.Length > 0 && char.IsWhiteSpace(_sb[^1])) _sb.Length--;
        }

        private void AppendNewlineAndIndent(int level)
        {
            _sb.AppendLine();
            for (var i = 0; i < level; i++) _sb.Append(SanityConstants.INDENT);
        }
    }
}