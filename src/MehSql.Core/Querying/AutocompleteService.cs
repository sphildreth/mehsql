using MehSql.Core.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MehSql.Core.Querying;

/// <summary>
/// Generates autocomplete suggestions based on SQL context and schema cache.
/// </summary>
public interface IAutocompleteService
{
    /// <summary>
    /// Returns autocomplete suggestions for the given SQL at the cursor position.
    /// </summary>
    Task<List<AutocompleteItem>> GetSuggestionsAsync(
        string sql,
        int cursorPosition,
        AutocompleteCache cache,
        CancellationToken ct = default);
}

/// <inheritdoc />
public class AutocompleteService : IAutocompleteService
{
    private readonly ISqlParser _parser;

    public AutocompleteService(ISqlParser parser)
    {
        _parser = parser;
    }

    public Task<List<AutocompleteItem>> GetSuggestionsAsync(
        string sql,
        int cursorPosition,
        AutocompleteCache cache,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var context = _parser.GetContextAtPosition(sql, cursorPosition);
            var candidates = new List<AutocompleteItem>();

            // Case 1: User typed "alias." — show columns for that table
            if (!string.IsNullOrEmpty(context.CurrentAlias))
            {
                var tableName = context.Aliases[context.CurrentAlias];
                var columns = cache.GetColumnsForTable(tableName);

                candidates.AddRange(columns.Select(col => new AutocompleteItem
                {
                    DisplayText = col.Name,
                    InsertText = col.Name,
                    Type = AutocompleteItemType.Column,
                    Description = col.DataType,
                    Priority = col.IsPrimaryKey ? 200 : 100
                }));
            }
            // Case 2: In FROM clause — show tables
            else if (context.CurrentClause == SqlClauseType.From)
            {
                candidates.AddRange(cache.GetAllTables());
            }
            // Case 3: Default — show keywords
            else
            {
                candidates.AddRange(cache.GetAllKeywords());
            }

            // Filter by partial word
            if (!string.IsNullOrEmpty(context.PartialWord))
            {
                candidates = candidates
                    .Where(c => c.DisplayText.StartsWith(context.PartialWord,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Sort by priority and limit results
            return candidates
                .OrderByDescending(c => c.Priority)
                .ThenBy(c => c.DisplayText)
                .Take(100)
                .ToList();
        }, ct);
    }
}
