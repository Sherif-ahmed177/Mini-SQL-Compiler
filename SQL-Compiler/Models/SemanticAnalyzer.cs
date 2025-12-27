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

            if (_errors.Count == 0)
            {
                AnnotateTree(_parseTree);
            }
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
                    AddError(0, 0, "CREATE TABLE missing table name");
                    return;
                }

                string tableName = tableNameNode.Lexeme;

                if (_symbolTable.TableExists(tableName))
                {
                    AddError(0, 0, $"Table '{tableName}' already exists");
                    return;
                }

                var fieldListNode = createNode.Children.FirstOrDefault(c => c.Name == "FieldList");
                if (fieldListNode == null)
                {
                    AddError(0, 0, $"CREATE TABLE '{tableName}' missing field list");
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
                AddError(0, 0, $"Error processing CREATE TABLE: {ex.Message}");
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

                if (!AreTypesCompatible(expectedType, actualType))
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

                    var valueNode = assignNode.Children.LastOrDefault(c => c.Name != "IDENTIFIER" && c.Name != "OPERATOR" && c.Name != "DELIMITER");
                    if (valueNode != null)
                    {
                        string expectedType = _symbolTable.GetColumnType(tableName, columnName) ?? "UNKNOWN";
                        string actualType = InferType(valueNode);

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

            if (node.Name == "Condition" && node.Children.Count >= 3)
            {
                var left = node.Children[0];
                var op = node.Children[1];
                var right = node.Children[2];

                if (op.Name == "OPERATOR" && IsRelationalOperator(op.Lexeme))
                {
                    string leftType = GetExpressionType(left, tableName);
                    string rightType = GetExpressionType(right, tableName);

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
            }

            foreach (var child in node.Children)
            {
                AnalyzeCondition(child, tableName);
            }
        }


        private void AnnotateTree(ParseTreeNode node)
        {
            if (node == null) return;

            switch (node.Name)
            {
                case "NUMBER":
                    node.DataType = InferType(node);
                    break;
                case "STRING":
                    node.DataType = "TEXT";
                    break;
                case "IDENTIFIER":
                    AnnotateIdentifier(node);
                    break;
            }

            foreach (var child in node.Children)
            {
                AnnotateTree(child);
            }
        }

        private void AnnotateIdentifier(ParseTreeNode identifierNode)
        {
            foreach (var table in _symbolTable.GetAllTables())
            {
                if (table.HasColumn(identifierNode.Lexeme))
                {
                    identifierNode.DataType = table.GetColumnType(identifierNode.Lexeme);
                    identifierNode.SymbolTableRef = $"{table.Name}.{identifierNode.Lexeme}";
                    break;
                }
            }
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
                case "IDENTIFIER":
                    return _symbolTable.GetColumnType(tableName, exprNode.Lexeme) ?? "UNKNOWN";
                default:
                    return "UNKNOWN";
            }
        }

        private bool AreTypesCompatible(string type1, string type2)
        {
            if (type1 == "UNKNOWN" || type2 == "UNKNOWN") return true; 

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
