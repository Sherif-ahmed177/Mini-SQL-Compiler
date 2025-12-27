using System;
using System.Collections.Generic;
using System.Linq;

namespace SQL_Compiler.Models
{
    public class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty; 

        public ColumnInfo(string name, string dataType)
        {
            Name = name;
            DataType = dataType;
        }

        public override string ToString()
        {
            return $"{Name}: {DataType}";
        }
    }

    public class TableInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();

        public TableInfo(string name)
        {
            Name = name;
        }

        public TableInfo(string name, List<ColumnInfo> columns)
        {
            Name = name;
            Columns = columns;
        }

        public bool HasColumn(string columnName)
        {
            return Columns.Any(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        }

        public string? GetColumnType(string columnName)
        {
            var column = Columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            return column?.DataType;
        }

        public ColumnInfo? GetColumn(string columnName)
        {
            return Columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString()
        {
            return $"{Name} ({string.Join(", ", Columns)})";
        }
    }

    public class SymbolTable
    {
        private readonly Dictionary<string, TableInfo> _tables;

        public SymbolTable()
        {
            _tables = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        }

        public bool AddTable(string tableName, List<ColumnInfo> columns)
        {
            if (_tables.ContainsKey(tableName))
            {
                return false; 
            }

            _tables[tableName] = new TableInfo(tableName, columns);
            return true;
        }

        public bool TableExists(string tableName)
        {
            return _tables.ContainsKey(tableName);
        }

        public TableInfo? GetTable(string tableName)
        {
            _tables.TryGetValue(tableName, out var table);
            return table;
        }

        public bool ColumnExists(string tableName, string columnName)
        {
            var table = GetTable(tableName);
            return table?.HasColumn(columnName) ?? false;
        }

        public string? GetColumnType(string tableName, string columnName)
        {
            var table = GetTable(tableName);
            return table?.GetColumnType(columnName);
        }

        public List<TableInfo> GetAllTables()
        {
            return _tables.Values.ToList();
        }

        public void Clear()
        {
            _tables.Clear();
        }

        public int Count => _tables.Count;

        public override string ToString()
        {
            if (_tables.Count == 0)
                return "Symbol Table: (empty)";

            return $"Symbol Table:\n{string.Join("\n", _tables.Values.Select(t => $"  - {t}"))}";
        }
    }
}
