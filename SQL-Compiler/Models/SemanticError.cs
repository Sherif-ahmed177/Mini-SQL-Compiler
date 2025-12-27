using System;

namespace SQL_Compiler.Models
{
    public class SemanticError
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = string.Empty;

        public SemanticError(int line, int column, string message)
        {
            Line = line;
            Column = column;
            Message = message;
        }

        public override string ToString()
        {
            return $"Semantic Error at line {Line}, column {Column}: {Message}";
        }
    }
}
