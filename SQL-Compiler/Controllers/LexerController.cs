using Microsoft.AspNetCore.Mvc;
using SQL_Compiler.Models;
using System.Collections.Generic;
using System.Linq;

namespace SQL_Compiler.Controllers
{
    public class LexerController : Controller
    {
        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Analyze([FromBody] string inputCode)
        {
            if (string.IsNullOrWhiteSpace(inputCode))
                return BadRequest("Please enter SQL-like code.");

            var lexer = new Lexer();
            var tokens = lexer.Analyze(inputCode);

            var parser = new Parser(tokens);
            var parseTree = parser.Parse();

            var semanticAnalyzer = new SemanticAnalyzer(parseTree);
            semanticAnalyzer.Analyze();

            var symbolTable = semanticAnalyzer.GetSymbolTable();
            var semanticErrors = semanticAnalyzer.GetErrors();
            var annotatedTree = semanticAnalyzer.GetAnnotatedTree();

            var result = new
            {
                tokens = tokens.Select(t => new
                {
                    type = t.Type.ToString(),   
                    lexeme = t.Lexeme,
                    line = t.Line,
                    column = t.Column
                }).ToList(),
                
                tree = parseTree,
                syntaxErrors = parser.Errors,
                
                symbolTable = symbolTable.GetAllTables().Select(table => new
                {
                    name = table.Name,
                    columns = table.Columns.Select(col => new
                    {
                        name = col.Name,
                        dataType = col.DataType
                    }).ToList()
                }).ToList(),
                semanticErrors = semanticErrors.Select(e => new
                {
                    line = e.Line,
                    column = e.Column,
                    message = e.Message
                }).ToList(),
                annotatedTree = annotatedTree,
                hasSemanticErrors = semanticAnalyzer.HasErrors()
            };

            return Json(result);
        }
    }
}
