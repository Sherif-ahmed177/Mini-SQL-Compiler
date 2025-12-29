using System;
using System.Collections.Generic;
using System.Linq;

namespace SQL_Compiler.Models
{
    public class SemanticAnalyzer
    {
        private readonly ParseTreeNode _parseTree;
        private readonly SymbolTable _symbolTable;
        private readonly List<SemanticError> _errors;
        private readonly HashSet<string> _validTypes = new(StringComparer.OrdinalIgnoreCase) 
        { 
            "INT", "FLOAT", "TEXT", "VARCHAR", "CHAR", "DATE", "DATETIME", "BOOLEAN", "BIGINT" 
        };

        public SemanticAnalyzer(ParseTreeNode parseTree)
        {
            _parseTree = parseTree;
            _symbolTable = new SymbolTable();
            _errors = new List<SemanticError>();
        }

        public void Analyze()
        {
            if (_parseTree == null) return;

            BuildSymbolTable(_parseTree);

            AnalyzeStatements(_parseTree);

        }

        public SymbolTable GetSymbolTable() => _symbolTable;
        public List<SemanticError> GetErrors() => _errors;
        public ParseTreeNode GetAnnotatedTree() => _parseTree;
        public bool HasErrors() => _errors.Count > 0;

        private void BuildSymbolTable(ParseTreeNode node)
        {
            if (node == null) return;

            if (node.Name == "CreateStmt")
            {
                ProcessCreateTable(node);
            }

            foreach (var child in node.Children)
            {
                BuildSymbolTable(child);
            }
        }

        private void ProcessCreateTable(ParseTreeNode createNode)
        {
            try
            {
                var tableNameNode = createNode.Children.FirstOrDefault(c => c.Name == "IDENTIFIER");
                if (tableNameNode == null)
                {
                    var (l0,c0) = BestPos(createNode);
                    AddError(l0, c0, "CREATE TABLE missing table name");
                    return;
                }

                string tableName = tableNameNode.Lexeme;

                // annotate table identifier
                tableNameNode.DataType = "TABLE";
                tableNameNode.SymbolRef = tableName;

                if (_symbolTable.TableExists(tableName))
                {
                    AddError(tableNameNode.Line, tableNameNode.Column, $"Table '{tableName}' already exists");
                    return;
                }

                var fieldListNode = createNode.Children.FirstOrDefault(c => c.Name == "FieldList");
                if (fieldListNode == null)
                {
                    AddError(tableNameNode.Line, tableNameNode.Column, $"CREATE TABLE '{tableName}' missing field list");
                    return;
                }

                var columns = new List<ColumnInfo>();
                foreach (var fieldDefNode in fieldListNode.Children.Where(c => c.Name == "FieldDef"))
                {
                    var columnNameNode = fieldDefNode.Children.FirstOrDefault(c => c.Name == "IDENTIFIER");
                    var columnTypeNode = fieldDefNode.Children.FirstOrDefault(c => c.Name == "TYPE");

                    if (columnNameNode != null && columnTypeNode != null)
                    {
                        string columnName = columnNameNode.Lexeme;
                        string columnType = columnTypeNode.Lexeme.ToUpper();

                        if (!_validTypes.Contains(columnType))
                        {
                            AddError(columnTypeNode.Line, columnTypeNode.Column, $"Invalid data type '{columnType}' in table '{tableName}'. Supported types: INT, FLOAT, TEXT");
                            continue;
                        }

                         // annotate column definition nodes + add semantic link
                        columnNameNode.DataType = columnType;
                        columnNameNode.SymbolRef = $"{tableName}.{columnName}";
                        columnTypeNode.DataType = "TYPE";

                        if (columns.Any(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
                        {
                            AddError(columnNameNode.Line, columnNameNode.Column, $"Duplicate column '{columnName}' in table '{tableName}'");
                            continue;
                        }

                        columns.Add(new ColumnInfo(columnName, columnType));
                    }
                }

                if (columns.Count > 0)
                {
                    _symbolTable.AddTable(tableName, columns);
                }
            }
            catch (Exception ex)
            {
                var (le, ce) = BestPos(createNode);
                AddError(le, ce, $"Error processing CREATE TABLE: {ex.Message}");
            }
        }


        private void AnalyzeStatements(ParseTreeNode node)
        {
            if (node == null) return;

            switch (node.Name)
            {
                case "SelectStmt":
                    AnalyzeSelectStatement(node);
                    break;
                case "InsertStmt":
                    AnalyzeInsertStatement(node);
                    break;
                case "UpdateStmt":
                    AnalyzeUpdateStatement(node);
                    break;
                case "DeleteStmt":
                    AnalyzeDeleteStatement(node);
                    break;
            }

            foreach (var child in node.Children)
            {
                AnalyzeStatements(child);
            }
        }

        private void AnalyzeSelectStatement(ParseTreeNode selectNode)
        {
            var tableNode = GetTableNameFromStatement(selectNode);
            if (tableNode == null) return;

            string tableName = tableNode.Lexeme;

            // annotate table identifier
            tableNode.DataType = "TABLE";
            tableNode.SymbolRef = tableName;

            if (!_symbolTable.TableExists(tableName))
            {
                AddError(tableNode.Line, tableNode.Column, $"Table '{tableName}' does not exist");
                return;
            }

            var selectListNode = selectNode.Children.FirstOrDefault(c => c.Name == "SelectList");
            if (selectListNode != null)
            {
                if (!selectListNode.Children.Any(c => c.Name == "OPERATOR" && c.Lexeme == "*"))
                {
                    foreach (var columnNode in selectListNode.Children.Where(c => c.Name == "IDENTIFIER"))
                    {
                        if (!_symbolTable.ColumnExists(tableName, columnNode.Lexeme))
                        {
                            AddError(columnNode.Line, columnNode.Column, $"Column '{columnNode.Lexeme}' does not exist in table '{tableName}'");
                        }
                    }
                }
            }

            var whereNode = selectNode.Children.FirstOrDefault(c => c.Name == "WhereClause");
            if (whereNode != null)
            {
                AnalyzeWhereClause(whereNode, tableName);
            }
        }

        private void AnalyzeInsertStatement(ParseTreeNode insertNode)
        {
            var tableNode = GetTableNameFromStatement(insertNode);
            if (tableNode == null) return;

            string tableName = tableNode.Lexeme;

            // annotate table identifier
            tableNode.DataType = "TABLE";
            tableNode.SymbolRef = tableName;

            if (!_symbolTable.TableExists(tableName))
            {
                AddError(tableNode.Line, tableNode.Column, $"Table '{tableName}' does not exist");
                return;
            }

            var table = _symbolTable.GetTable(tableName);
            if (table == null) return;

            var valueListNode = insertNode.Children.FirstOrDefault(c => c.Name == "ValueList");
            if (valueListNode == null) return;

            var values = valueListNode.Children.Where(c => c.Name != "DELIMITER").ToList();

            if (values.Count != table.Columns.Count)
            {
                AddError(tableNode.Line, tableNode.Column, $"INSERT INTO '{tableName}' expects {table.Columns.Count} values but got {values.Count}");
                return;
            }

            for (int i = 0; i < values.Count && i < table.Columns.Count; i++)
            {
                var valueNode = values[i];
                var column = table.Columns[i];
                string expectedType = column.DataType;
                
                string actualType;
                if (valueNode.Name == "IDENTIFIER")
                {
                    actualType = "IDENTIFIER";
                }
                else
                {
                    actualType = InferType(valueNode);
                }

                

                
                valueNode.DataType = actualType;if (!AreTypesCompatible(expectedType, actualType))
                {
                    if (expectedType == "TEXT" && actualType == "IDENTIFIER")
                    {
                        AddError(valueNode.Line, valueNode.Column, $"Invalid value '{valueNode.Lexeme}' for column '{column.Name}'. String literals must be enclosed in single quotes.");
                    }
                    else
                    {
                        AddError(valueNode.Line, valueNode.Column, $"Type mismatch in INSERT. Column '{column.Name}' expects {expectedType} but got {actualType}");
                    }
                }
            }
        }

        private void AnalyzeUpdateStatement(ParseTreeNode updateNode)
        {
            var tableNode = GetTableNameFromStatement(updateNode);
            if (tableNode == null) return;

            string tableName = tableNode.Lexeme;

            // annotate table identifier
            tableNode.DataType = "TABLE";
            tableNode.SymbolRef = tableName;

            if (!_symbolTable.TableExists(tableName))
            {
                AddError(tableNode.Line, tableNode.Column, $"Table '{tableName}' does not exist");
                return;
            }

            var assignListNode = updateNode.Children.FirstOrDefault(c => c.Name == "AssignList");
            if (assignListNode != null)
            {
                foreach (var assignNode in assignListNode.Children.Where(c => c.Name == "Assignment"))
                {
                    var columnNode = assignNode.Children.FirstOrDefault(c => c.Name == "IDENTIFIER");
                    if (columnNode == null) continue;

                    string columnName = columnNode.Lexeme;

                    if (!_symbolTable.ColumnExists(tableName, columnName))
                    {
                        AddError(columnNode.Line, columnNode.Column, $"Column '{columnName}' does not exist in table '{tableName}'");
                        continue;
                    }

                    // semantic link + annotate LHS
                    LinkIdentifier(columnNode, tableName);

                    var valueNode = assignNode.Children.LastOrDefault(c => c.Name != "OPERATOR" && c.Name != "DELIMITER");
                    if (valueNode != null)
                    {
                        string expectedType = _symbolTable.GetColumnType(tableName, columnName) ?? "UNKNOWN";
                        string actualType = AnnotateExpression(valueNode, tableName);

                        if (!AreTypesCompatible(expectedType, actualType))
                        {
                            AddError(valueNode.Line, valueNode.Column, $"Type mismatch in UPDATE. Column '{columnName}' expects {expectedType} but got {actualType}");
                        }
                    }
                }
            }

            var whereNode = updateNode.Children.FirstOrDefault(c => c.Name == "WhereClause");
            if (whereNode != null)
            {
                AnalyzeWhereClause(whereNode, tableName);
            }
        }

        private void AnalyzeDeleteStatement(ParseTreeNode deleteNode)
        {
            var tableNode = GetTableNameFromStatement(deleteNode);
            if (tableNode == null) return;

            string tableName = tableNode.Lexeme;

            
            tableNode.DataType = "TABLE";
            tableNode.SymbolRef = tableName;

            if (!_symbolTable.TableExists(tableName))
            {
                AddError(tableNode.Line, tableNode.Column, $"Table '{tableName}' does not exist");
                return;
            }

            var whereNode = deleteNode.Children.FirstOrDefault(c => c.Name == "WhereClause");
            if (whereNode != null)
            {
                AnalyzeWhereClause(whereNode, tableName);
            }
        }

        private void AnalyzeWhereClause(ParseTreeNode whereNode, string tableName)
        {
            AnalyzeCondition(whereNode, tableName);
        }

        private void AnalyzeCondition(ParseTreeNode node, string tableName)
        {
            if (node == null) return;

            if (node.Name == "Condition")
            {
                
                if (node.Children.Count >= 2)
                {
                    var op0 = node.Children[0];
                    
                    if (op0 != null && op0.Name == "OPERATOR" && op0.Lexeme.Equals("NOT", StringComparison.OrdinalIgnoreCase))
                    {
                        node.DataType = "BOOLEAN";
                    }
                }
            }

            if (node.Name == "Condition" && node.Children.Count >= 3)
            {
                var left = node.Children[0];
                var op = node.Children[1];
                var right = node.Children[2];

                if (op.Name == "OPERATOR" && IsRelationalOperator(op.Lexeme))
                {
                    string leftType = AnnotateExpression(left, tableName);
                    string rightType = AnnotateExpression(right, tableName);

                   
                    node.DataType = "BOOLEAN";

                    if (!AreTypesCompatible(leftType, rightType))
                    {
                        AddError(op.Line, op.Column, $"Type mismatch in WHERE clause. Cannot compare {leftType} with {rightType}");
                    }
                }
            }

            if (node.Name == "IDENTIFIER")
            {
                if (!_symbolTable.ColumnExists(tableName, node.Lexeme))
                {
                    AddError(node.Line, node.Column, $"Column '{node.Lexeme}' does not exist in table '{tableName}'");
                }
                else
                {
                    LinkIdentifier(node, tableName);
                }
            }

            foreach (var child in node.Children)
            {
                AnalyzeCondition(child, tableName);
            }
        }


        

        private void SetNodeType(ParseTreeNode node, string? dataType)
        {
            if (node == null) return;
            node.DataType = dataType;
        }

        private void LinkIdentifier(ParseTreeNode idNode, string tableName)
        {
            if (idNode == null) return;
            if (idNode.Name != "IDENTIFIER") return;

            var colType = _symbolTable.GetColumnType(tableName, idNode.Lexeme);
            if (colType != null)
            {
                idNode.DataType = colType;
                idNode.SymbolRef = $"{tableName}.{idNode.Lexeme}";
            }
        }

        private (int line, int col) BestPos(ParseTreeNode? node, int fallbackLine = 0, int fallbackCol = 0)
        {
            if (node == null) return (fallbackLine, fallbackCol);
            if (node.Line != 0 || node.Column != 0) return (node.Line, node.Column);
            var first = node.Children?.FirstOrDefault(c => c != null && (c.Line != 0 || c.Column != 0));
            if (first != null) return (first.Line, first.Column);
            return (fallbackLine, fallbackCol);
        }

        private string AnnotateExpression(ParseTreeNode exprNode, string tableName)
        {
            
            if (exprNode == null) return "UNKNOWN";

            string t = GetExpressionType(exprNode, tableName);
            exprNode.DataType = t;

            if (exprNode.Name == "IDENTIFIER")
            {
                LinkIdentifier(exprNode, tableName);
            }

            return t;
        }

private ParseTreeNode? GetTableNameFromStatement(ParseTreeNode statementNode)
        {
            return statementNode.Children.FirstOrDefault(c => c.Name == "IDENTIFIER");
        }

        private string InferType(ParseTreeNode valueNode)
        {
            switch (valueNode.Name)
            {
                case "NUMBER":
                    return valueNode.Lexeme.Contains('.') ? "FLOAT" : "INT";
                case "STRING":
                    return "TEXT";
                case "BOOLEAN_LITERAL":
                    return "BOOLEAN";
                case "KEYWORD":
                    if (valueNode.Lexeme.ToUpper() == "NULL") return "NULL";
                    return "UNKNOWN";
                case "IDENTIFIER":
                    return valueNode.DataType ?? "UNKNOWN";
                default:
                    return "UNKNOWN";
            }
        }

        private string GetExpressionType(ParseTreeNode exprNode, string tableName)
        {
            switch (exprNode.Name)
            {
                case "NUMBER":
                    return exprNode.Lexeme.Contains('.') ? "FLOAT" : "INT";
                case "STRING":
                    return "TEXT";
                case "BOOLEAN_LITERAL":
                    return "BOOLEAN";
                case "KEYWORD":
                    if (exprNode.Lexeme.ToUpper() == "NULL") return "NULL";
                    return "UNKNOWN";
                case "IDENTIFIER":
                    return _symbolTable.GetColumnType(tableName, exprNode.Lexeme) ?? "UNKNOWN";
                default:
                    return "UNKNOWN";
            }
        }

        private bool AreTypesCompatible(string type1, string type2)
        {
            if (type1 == "UNKNOWN" || type2 == "UNKNOWN") return true; 
            if (type1 == "NULL" || type2 == "NULL") return true;

            if (type1.Equals(type2, StringComparison.OrdinalIgnoreCase)) return true;

            if ((type1 == "INT" && type2 == "FLOAT") || (type1 == "FLOAT" && type2 == "INT"))
                return true;

            return false;
        }

        private bool IsRelationalOperator(string op)
        {
            return op == "=" || op == "<>" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=";
        }

        private void AddError(int line, int column, string message)
        {
            _errors.Add(new SemanticError(line, column, message));
        }

    }
}
