using System;
using System.Collections.Generic;

namespace SQL_Compiler.Models
{
    public class ParseTreeNode
    {
        public string Name { get; set; }
        public string Lexeme { get; set; } 
        public string? DataType { get; set; } 
        public string? SymbolTableRef { get; set; } 
        public int Line { get; set; } = 0;
        public int Column { get; set; } = 0;
        public List<ParseTreeNode> Children { get; set; } = new List<ParseTreeNode>();

        public ParseTreeNode(string name, string lexeme = "", int line = 0, int column = 0)
        {
            Name = name;
            Lexeme = lexeme;
            Line = line;
            Column = column;
        }

        public ParseTreeNode AddChild(ParseTreeNode node)
        {
            if (node != null)
                Children.Add(node);
            return node;
        }
    }

    public class Parser
    {
        private readonly List<SqlToken> _tokens;
        private int _current = 0;
        private readonly List<string> _errors = new List<string>();

        public Parser(List<SqlToken> tokens)
        {
            _tokens = tokens;
        }

        public List<string> Errors => _errors;

        private SqlToken Peek()
        {
            if (_current >= _tokens.Count)
            {
                var lastToken = _tokens.Count > 0 ? _tokens[_tokens.Count - 1] : null;
                return new SqlToken
                {
                    Type = "EOF",
                    Lexeme = "",
                    Line = lastToken?.Line ?? 1,
                    Column = lastToken?.Column ?? 0
                };
            }
            return _tokens[_current];
        }

        private SqlToken Previous()
        {
            return _tokens[_current - 1];
        }

        private bool IsAtEnd()
        {
            return Peek().Type == "EOF";
        }

        private SqlToken Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool Check(string type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        private bool Match(params string[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private SqlToken Consume(string type, string message)
        {
            if (Check(type)) return Advance();

            SyntaxError(message, Peek());
            return new SqlToken { Type = "ERROR", Lexeme = "" }; 
        }

        private SqlToken _lastErrorToken;

        private void SyntaxError(string message, SqlToken token)
        {
            if (_lastErrorToken != null && 
                _lastErrorToken.Line == token.Line && 
                _lastErrorToken.Column == token.Column)
            {
                return;
            }

            _lastErrorToken = token;
            _errors.Add($"Syntax Error: {message} at line {token.Line}, column {token.Column}, found '{token.Lexeme}'");
        }

        private void Synchronize()
        {
            Advance();

            while (!IsAtEnd())
            {
                if (Previous().Type == "SEMICOLON") return;

                switch (Peek().Type)
                {
                    case "SELECT":
                    case "INSERT":
                    case "UPDATE":
                    case "DELETE":
                    case "CREATE":
                        return;
                }

                Advance();
            }
        }

        public ParseTreeNode Parse()
        {
            var root = new ParseTreeNode("Program");
            while (!IsAtEnd())
            {
                try 
                {
                    var stmt = ParseStatement();
                    if (stmt != null) root.AddChild(stmt);
                }
                catch (Exception)
                {
                    Synchronize();
                }
            }
            return root;
        }

        private ParseTreeNode ParseStatement()
        {
            if (Match("SELECT")) return ParseSelectStmt();
            if (Match("INSERT")) return ParseInsertStmt();
            if (Match("UPDATE")) return ParseUpdateStmt();
            if (Match("DELETE")) return ParseDeleteStmt();
            if (Match("CREATE")) return ParseCreateStmt();

            if (!IsAtEnd())
            {
                SyntaxError("Expected statement (SELECT, INSERT, UPDATE, DELETE, CREATE)", Peek());
                Synchronize();
            }
            return null;
        }

        private ParseTreeNode ParseCreateStmt()
        {
            var createNode = new ParseTreeNode("CreateStmt");
            
            createNode.AddChild(new ParseTreeNode("KEYWORD", "CREATE", Previous().Line, Previous().Column)); 

            Consume("TABLE", "Expected 'TABLE' after 'CREATE'");
            createNode.AddChild(new ParseTreeNode("KEYWORD", "TABLE", Previous().Line, Previous().Column));

            var tableName = Consume("IDENTIFIER", "Expected table name");
            createNode.AddChild(new ParseTreeNode("IDENTIFIER", tableName.Lexeme, tableName.Line, tableName.Column));

            Consume("LEFT_PAREN", "Expected '(' after table name");
            createNode.AddChild(new ParseTreeNode("DELIMITER", "(", Previous().Line, Previous().Column));

            createNode.AddChild(ParseFieldList());

            Consume("RIGHT_PAREN", "Expected ')' after field list");
            createNode.AddChild(new ParseTreeNode("DELIMITER", ")", Previous().Line, Previous().Column));

            Consume("SEMICOLON", "Expected ';' at end of statement");
            createNode.AddChild(new ParseTreeNode("DELIMITER", ";", Previous().Line, Previous().Column));

            return createNode;
        }

        private ParseTreeNode ParseFieldList()
        {
            var listNode = new ParseTreeNode("FieldList");
            do
            {
                listNode.AddChild(ParseFieldDef());
            } while (Match("COMMA") && (listNode.AddChild(new ParseTreeNode("DELIMITER", ",", Previous().Line, Previous().Column)) != null)); 
            return listNode;
        }

        private ParseTreeNode ParseFieldDef()
        {
            var fieldNode = new ParseTreeNode("FieldDef");
            
            var name = Consume("IDENTIFIER", "Expected column name");
            fieldNode.AddChild(new ParseTreeNode("IDENTIFIER", name.Lexeme, name.Line, name.Column));

            var type = Consume("TYPE", "Expected column type");
            fieldNode.AddChild(new ParseTreeNode("TYPE", type.Lexeme, type.Line, type.Column));

            if (Match("PRIMARY"))
            {
                fieldNode.AddChild(new ParseTreeNode("KEYWORD", "PRIMARY", Previous().Line, Previous().Column));
                Consume("KEY", "Expected 'KEY' after 'PRIMARY'");
                fieldNode.AddChild(new ParseTreeNode("KEYWORD", "KEY", Previous().Line, Previous().Column));
            }

            return fieldNode;
        }

        private ParseTreeNode ParseWhereClause()
        {
            var whereNode = new ParseTreeNode("WhereClause");
            whereNode.AddChild(new ParseTreeNode("KEYWORD", "WHERE", Previous().Line, Previous().Column)); 
            whereNode.AddChild(ParseCondition());
            return whereNode;
        }

        private ParseTreeNode ParseCondition()
        {
            return ParseOrExpression();
        }

        private ParseTreeNode ParseOrExpression()
        {
            var left = ParseAndExpression();
            
            while (Match("OR"))
            {
                var orNode = new ParseTreeNode("Condition"); 
                orNode.AddChild(left);
                orNode.AddChild(new ParseTreeNode("OPERATOR", "OR", Previous().Line, Previous().Column));
                orNode.AddChild(ParseAndExpression());
                left = orNode;
            }
            return left;
        }

        private ParseTreeNode ParseAndExpression()
        {
            var left = ParseNotExpression();

            while (Match("AND"))
            {
                var andNode = new ParseTreeNode("Condition"); 
                andNode.AddChild(left);
                andNode.AddChild(new ParseTreeNode("OPERATOR", "AND", Previous().Line, Previous().Column));
                andNode.AddChild(ParseNotExpression());
                left = andNode;
            }
            return left;
        }

        private ParseTreeNode ParseNotExpression()
        {
            if (Match("NOT"))
            {
                var notNode = new ParseTreeNode("Condition");
                notNode.AddChild(new ParseTreeNode("OPERATOR", "NOT", Previous().Line, Previous().Column));
                notNode.AddChild(ParseNotExpression());
                return notNode;
            }
            return ParseRelExpression();
        }

        private ParseTreeNode ParseRelExpression()
        {
            var left = ParseTerm();

            if (Match("EQUAL", "NOT_EQUAL", "LESS_THAN", "GREATER_THAN", "LESS_EQUAL", "GREATER_EQUAL", "LIKE"))
            {
                var opToken = Previous();
                var relNode = new ParseTreeNode("Condition"); 
                relNode.AddChild(left);
                relNode.AddChild(new ParseTreeNode("OPERATOR", opToken.Lexeme, opToken.Line, opToken.Column));
                relNode.AddChild(ParseTerm());
                return relNode;
            }
            
            return left;
        }

        private ParseTreeNode ParseTerm()
        {
            if (Match("IDENTIFIER"))
                return new ParseTreeNode("IDENTIFIER", Previous().Lexeme, Previous().Line, Previous().Column);
            
            if (Match("NUMBER"))
                return new ParseTreeNode("NUMBER", Previous().Lexeme, Previous().Line, Previous().Column);
            
            if (Match("STRING"))
                return new ParseTreeNode("STRING", Previous().Lexeme, Previous().Line, Previous().Column);

            if (Match("NULL"))
                return new ParseTreeNode("KEYWORD", "NULL", Previous().Line, Previous().Column);

            if (Match("TRUE"))
                return new ParseTreeNode("BOOLEAN_LITERAL", "TRUE", Previous().Line, Previous().Column);

            if (Match("FALSE"))
                return new ParseTreeNode("BOOLEAN_LITERAL", "FALSE", Previous().Line, Previous().Column);

            if (Match("LEFT_PAREN"))
            {
                var expr = ParseCondition();
                Consume("RIGHT_PAREN", "Expected ')' after expression");
                return expr; 
            }

            SyntaxError("Expected expression (identifier, value, or parenthesis)", Peek());
            return new ParseTreeNode("ERROR", "Missing Term");
        }

        private ParseTreeNode ParseSelectStmt()
        {
            var node = new ParseTreeNode("SelectStmt");
            node.AddChild(new ParseTreeNode("KEYWORD", "SELECT", Previous().Line, Previous().Column)); 

            node.AddChild(ParseSelectList());

            Consume("FROM", "Expected 'FROM' clause after SelectList");
            node.AddChild(new ParseTreeNode("KEYWORD", "FROM", Previous().Line, Previous().Column));

            var table = Consume("IDENTIFIER", "Expected table name");
            node.AddChild(new ParseTreeNode("IDENTIFIER", table.Lexeme, table.Line, table.Column));

            if (Match("WHERE"))
            {
                node.AddChild(ParseWhereClause());
            }

            if (Match("ORDER"))
            {
                 var orderNode = new ParseTreeNode("OrderClause");
                 orderNode.AddChild(new ParseTreeNode("KEYWORD", "ORDER", Previous().Line, Previous().Column));
                 Consume("BY", "Expected 'BY' after 'ORDER'");
                 orderNode.AddChild(new ParseTreeNode("KEYWORD", "BY", Previous().Line, Previous().Column));
                 
                 var col = Consume("IDENTIFIER", "Expected column name in ORDER BY");
                 orderNode.AddChild(new ParseTreeNode("IDENTIFIER", col.Lexeme, col.Line, col.Column));

                 if (Match("ASC", "DESC"))
                 {
                     orderNode.AddChild(new ParseTreeNode("KEYWORD", Previous().Lexeme, Previous().Line, Previous().Column));
                 }
                 node.AddChild(orderNode);
            }

            Consume("SEMICOLON", "Expected ';' at end of query");
            node.AddChild(new ParseTreeNode("DELIMITER", ";", Previous().Line, Previous().Column));

            return node;
        }

        private ParseTreeNode ParseSelectList()
        {
            var listNode = new ParseTreeNode("SelectList");
            if (Match("MULTIPLY"))
            {
                listNode.AddChild(new ParseTreeNode("OPERATOR", "*", Previous().Line, Previous().Column));
                return listNode;
            }

            do
            {
                var id = Consume("IDENTIFIER", "Expected column name");
                listNode.AddChild(new ParseTreeNode("IDENTIFIER", id.Lexeme, id.Line, id.Column));
            } while (Match("COMMA") && (listNode.AddChild(new ParseTreeNode("DELIMITER", ",", Previous().Line, Previous().Column)) != null));

            return listNode;
        }

        private ParseTreeNode ParseInsertStmt()
        {
            var node = new ParseTreeNode("InsertStmt");
            node.AddChild(new ParseTreeNode("KEYWORD", "INSERT", Previous().Line, Previous().Column)); 

            Consume("INTO", "Expected 'INTO' after 'INSERT'");
            node.AddChild(new ParseTreeNode("KEYWORD", "INTO", Previous().Line, Previous().Column));

            var table = Consume("IDENTIFIER", "Expected table name");
            node.AddChild(new ParseTreeNode("IDENTIFIER", table.Lexeme, table.Line, table.Column));

            Consume("VALUES", "Expected 'VALUES' keyword");
            node.AddChild(new ParseTreeNode("KEYWORD", "VALUES", Previous().Line, Previous().Column));

            Consume("LEFT_PAREN", "Expected '(' before values");
            node.AddChild(new ParseTreeNode("DELIMITER", "(", Previous().Line, Previous().Column));

            node.AddChild(ParseValueList());

            Consume("RIGHT_PAREN", "Expected ')' after values");
            node.AddChild(new ParseTreeNode("DELIMITER", ")", Previous().Line, Previous().Column));

            Consume("SEMICOLON", "Expected ';' at end");
            node.AddChild(new ParseTreeNode("DELIMITER", ";", Previous().Line, Previous().Column));

            return node;
        }

        private ParseTreeNode ParseValueList()
        {
            var listNode = new ParseTreeNode("ValueList");
            do
            {
               listNode.AddChild(ParseTerm()); 
            } while (Match("COMMA") && (listNode.AddChild(new ParseTreeNode("DELIMITER", ",", Previous().Line, Previous().Column)) != null));
            return listNode;
        }

        private ParseTreeNode ParseUpdateStmt()
        {
            var node = new ParseTreeNode("UpdateStmt");
            node.AddChild(new ParseTreeNode("KEYWORD", "UPDATE", Previous().Line, Previous().Column));

            var table = Consume("IDENTIFIER", "Expected table name");
            node.AddChild(new ParseTreeNode("IDENTIFIER", table.Lexeme, table.Line, table.Column));

            Consume("SET", "Expected 'SET' keyword");
            node.AddChild(new ParseTreeNode("KEYWORD", "SET", Previous().Line, Previous().Column));

            node.AddChild(ParseAssignList());

            if (Match("WHERE"))
            {
                node.AddChild(ParseWhereClause());
            }

            Consume("SEMICOLON", "Expected ';' at end");
            node.AddChild(new ParseTreeNode("DELIMITER", ";", Previous().Line, Previous().Column));
            return node;
        }

        private ParseTreeNode ParseAssignList()
        {
            var listNode = new ParseTreeNode("AssignList");
            do 
            {
                var id = Consume("IDENTIFIER", "Expected column name");
                var assignNode = new ParseTreeNode("Assignment");
                assignNode.AddChild(new ParseTreeNode("IDENTIFIER", id.Lexeme, id.Line, id.Column));

                Consume("EQUAL", "Expected '=' in assignment");
                assignNode.AddChild(new ParseTreeNode("OPERATOR", "=", Previous().Line, Previous().Column));

                assignNode.AddChild(ParseTerm());
                listNode.AddChild(assignNode);

            } while (Match("COMMA") && (listNode.AddChild(new ParseTreeNode("DELIMITER", ",")) != null));

            return listNode;
        }

        private ParseTreeNode ParseDeleteStmt()
        {
             var node = new ParseTreeNode("DeleteStmt");
             node.AddChild(new ParseTreeNode("KEYWORD", "DELETE"));
             
             Consume("FROM", "Expected 'FROM' after 'DELETE'");
             node.AddChild(new ParseTreeNode("KEYWORD", "FROM"));

             var table = Consume("IDENTIFIER", "Expected table name");
             node.AddChild(new ParseTreeNode("IDENTIFIER", table.Lexeme));

             if (Match("WHERE"))
             {
                 node.AddChild(ParseWhereClause());
             }

             Consume("SEMICOLON", "Expected ';' at end");
             node.AddChild(new ParseTreeNode("DELIMITER", ";"));

             return node;
        }
    }
}
