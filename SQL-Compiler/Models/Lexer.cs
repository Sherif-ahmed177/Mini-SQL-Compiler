namespace SQL_Compiler.Models
{
    public class SqlToken
    {
        public string Type { get; set; } = string.Empty;
        public string Lexeme { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public class Lexer
    {
        private readonly HashSet<string> _keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES",
            "UPDATE", "SET", "DELETE", "CREATE", "TABLE", "DROP",
            "ALTER", "ADD", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
            "AND", "OR", "NOT", "NULL", "ORDER", "BY", "GROUP", "HAVING",
            "ASC", "DESC", "TRUE", "FALSE"
        };

        private readonly HashSet<string> _types = new(StringComparer.OrdinalIgnoreCase) { "INT", "FLOAT", "TEXT", "VARCHAR", "CHAR", "DATE", "DATETIME", "BOOLEAN", "BIGINT" };
        private readonly HashSet<char> _delimiters = new() { '(', ')', ',', ';', '.' };

        public List<SqlToken> Analyze(string code)
        {
            var tokens = new List<SqlToken>();
            if (string.IsNullOrWhiteSpace(code)) return tokens;

            int i = 0, line = 1, col = 1;

            while (i < code.Length)
            {
                char c = code[i];

                if (char.IsWhiteSpace(c))
                {
                    if (c == '\n') { line++; col = 1; }
                    else col++;
                    i++;
                    continue;
                }

                if (c == '-' && i + 1 < code.Length && code[i + 1] == '-')
                {
                    while (i < code.Length && code[i] != '\n') { i++; }
                    continue;
                }

                if (c == '#' && i + 1 < code.Length && code[i + 1] == '#')
                {
                    int startLine = line, startCol = col;
                    i += 2; col += 2; 
                    bool closed = false;
                    while (i < code.Length)
                    {
                        if (code[i] == '#' && i + 1 < code.Length && code[i + 1] == '#')
                        {
                            closed = true;
                            i += 2; col += 2; 
                            break;
                        }
                        if (code[i] == '\n') { line++; col = 1; i++; continue; }
                        i++; col++;
                    }
                    if (!closed)
                    {
                        tokens.Add(new SqlToken
                        {
                            Type = "ERROR",
                            Lexeme = $"Unclosed comment starting at line {startLine}, column {startCol}",
                            Line = startLine,
                            Column = startCol
                        });
                    }
                    continue;
                }

                if (char.IsLetter(c))
                {
                    int start = i, startCol = col;
                    while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                    {
                        i++; col++;
                    }

                    string word = code.Substring(start, i - start);

                    if (_keywords.Contains(word))
                        tokens.Add(new SqlToken { Type = word.ToUpper(), Lexeme = word, Line = line, Column = startCol });
                    else if (_types.Contains(word))
                        tokens.Add(new SqlToken { Type = "TYPE", Lexeme = word, Line = line, Column = startCol });
                    else
                        tokens.Add(new SqlToken { Type = "IDENTIFIER", Lexeme = word, Line = line, Column = startCol });

                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i, startCol = col;
                    while (i < code.Length && (char.IsDigit(code[i]) || code[i] == '.'))
                    { i++; col++; }

                    string num = code.Substring(start, i - start);
                    tokens.Add(new SqlToken { Type = "NUMBER", Lexeme = num, Line = line, Column = startCol });
                    continue;
                }

                if (c == '\'')
                {
                    int startCol = col;
                    int startLine = line;
                    i++; col++;
                    int start = i;
                    while (i < code.Length && code[i] != '\'')
                    {
                        if (code[i] == '\n') { line++; col = 1; }
                        i++; col++;
                    }

                    if (i >= code.Length)
                    {
                        tokens.Add(new SqlToken
                        {
                            Type = "ERROR",
                            Lexeme = $"Unclosed string starting at line {startLine}, column {startCol}",
                            Line = startLine,
                            Column = startCol
                        });
                        break;
                    }

                    string str = code.Substring(start, i - start);
                    tokens.Add(new SqlToken { Type = "STRING", Lexeme = $"'{str}'", Line = line, Column = startCol });
                    i++; col++;
                    continue;
                }

                string opType = "";
                string opLexeme = c.ToString();

                switch (c)
                {
                    case '=':
                        opType = "EQUAL";
                        break;

                    case '>':
                        if (i + 1 < code.Length && code[i + 1] == '=')
                        {
                            opType = "GREATER_EQUAL";
                            opLexeme = ">=";
                            i++;
                            col++;
                        }
                        else opType = "GREATER_THAN";
                        break;

                    case '<':
                        if (i + 1 < code.Length && code[i + 1] == '=')
                        {
                            opType = "LESS_EQUAL";
                            opLexeme = "<=";
                            i++;
                            col++;
                        }
                        else if (i + 1 < code.Length && code[i + 1] == '>')
                        {
                            opType = "NOT_EQUAL";
                            opLexeme = "<>";
                            i++;
                            col++;
                        }
                        else opType = "LESS_THAN";
                        break;

                    case '!':
                        if (i + 1 < code.Length && code[i + 1] == '=')
                        {
                            opType = "NOT_EQUAL";
                            opLexeme = "!=";
                            i++;
                            col++;
                        }
                        break;

                    case '+':
                        opType = "PLUS";
                        break;

                    case '-':
                        opType = "MINUS";
                        break;

                    case '*':
                        opType = "MULTIPLY";
                        break;

                    case '/':
                        opType = "DIVIDE";
                        break;
                }

                if (!string.IsNullOrEmpty(opType))
                {
                    tokens.Add(new SqlToken
                    {
                        Type = opType,
                        Lexeme = opLexeme,
                        Line = line,
                        Column = col
                    });
                    i++;
                    col++;
                    continue;
                }

                if (_delimiters.Contains(c))
                {
                    string type = c switch
                    {
                        '(' => "LEFT_PAREN",
                        ')' => "RIGHT_PAREN",
                        ',' => "COMMA",
                        ';' => "SEMICOLON",
                        _ => "DELIMITER"
                    };

                    tokens.Add(new SqlToken { Type = type, Lexeme = c.ToString(), Line = line, Column = col });
                    i++; col++;
                    continue;
                }

                tokens.Add(new SqlToken
                {
                    Type = "ERROR",
                    Lexeme = $"Invalid character '{c}' at line {line}, column {col}",
                    Line = line,
                    Column = col
                });
                i++; col++;
            }

            return tokens;
        }
    }
}
