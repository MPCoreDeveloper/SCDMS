using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpCoreDB.Data.Provider;
using Scdms.Models;

namespace Scdms.Services;

/// <summary>
/// Executes SQL editor requests against the active SharpCoreDB session.
/// </summary>
public sealed class ViewerQueryService(
    IViewerConnectionService viewerConnectionService,
    IViewerTransactionService transactionService,
    IOptions<ScdmsOptions> options) : IViewerQueryService
{
    private readonly IViewerConnectionService _viewerConnectionService = viewerConnectionService;
    private readonly IViewerTransactionService _transactionService = transactionService;
    private readonly ScdmsOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<QueryExecutionResult> ExecuteAsync(QueryExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Sql);

        var session = _viewerConnectionService.GetCurrentSession()
            ?? throw new InvalidOperationException("No active database connection is available.");

        var statements = SplitSqlStatements(request.Sql)
            .Select(StripComments)
            .Where(static statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        if (statements.Length == 0)
        {
            throw new ArgumentException("Provide at least one SQL statement.", nameof(request));
        }

        var parameters = ParseParameters(request.ParametersJson);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.QueryTimeoutSeconds)));
        var executionToken = timeoutCts.Token;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var queryState = session.ConnectionMode switch
            {
                ViewerConnectionMode.Server => await ExecuteServerStatementsAsync(session, statements, parameters, executionToken).ConfigureAwait(false),
                _ => await ExecuteLocalStatementsAsync(session, statements, parameters, executionToken).ConfigureAwait(false)
            };

            stopwatch.Stop();

            return new QueryExecutionResult
            {
                Columns = queryState.Columns,
                Rows = queryState.Rows,
                StatementCount = statements.Length,
                ResultRowCount = queryState.Rows.Count,
                NonQueryStatementCount = queryState.NonQueryStatementCount,
                Truncated = queryState.Truncated,
                SchemaChanged = queryState.SchemaChanged,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Summary = BuildSummary(statements.Length, queryState.Rows.Count, queryState.NonQueryStatementCount, queryState.Truncated, stopwatch.ElapsedMilliseconds)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"SQL execution exceeded {_options.QueryTimeoutSeconds} seconds.");
        }
    }

    private async Task<QueryExecutionState> ExecuteLocalStatementsAsync(
        ViewerSessionState session,
        IReadOnlyList<string> statements,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (_transactionService.TryGetLocalExecutionConnection(out var transactionConnection) && transactionConnection is not null)
        {
            return await ExecuteLocalStatementsCoreAsync(transactionConnection, statements, parameters, cancellationToken).ConfigureAwait(false);
        }

        using var ephemeralConnection = new SharpCoreDBConnection(_viewerConnectionService.BuildLocalConnectionString(session));
        await ephemeralConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteLocalStatementsCoreAsync(ephemeralConnection, statements, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<QueryExecutionState> ExecuteServerStatementsAsync(
        ViewerSessionState session,
        IReadOnlyList<string> statements,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (_transactionService.TryGetServerExecutionConnection(out var transactionConnection) && transactionConnection is not null)
        {
            return await ExecuteServerStatementsCoreAsync(transactionConnection, statements, parameters, cancellationToken).ConfigureAwait(false);
        }

        await using var ephemeralConnection = new SharpCoreDB.Client.SharpCoreDBConnection(_viewerConnectionService.BuildServerConnectionString(session));
        await ephemeralConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteServerStatementsCoreAsync(ephemeralConnection, statements, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<QueryExecutionState> ExecuteLocalStatementsCoreAsync(
        SharpCoreDBConnection connection,
        IReadOnlyList<string> statements,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var state = new QueryExecutionState();

        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var command = new SharpCoreDBCommand(statement, connection)
            {
                CommandTimeout = Math.Max(1, _options.QueryTimeoutSeconds)
            };

            ApplyLocalParameters(command, parameters);

            if (IsRowReturningStatement(statement))
            {
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
                await ReadGridFromDataReaderAsync(reader, state, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                state.NonQueryStatementCount++;
            }

            if (IsSchemaChangingStatement(statement))
            {
                state.SchemaChanged = true;
            }
        }

        return state;
    }

    private async Task<QueryExecutionState> ExecuteServerStatementsCoreAsync(
        SharpCoreDB.Client.SharpCoreDBConnection connection,
        IReadOnlyList<string> statements,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var state = new QueryExecutionState();

        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var command = connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = Math.Max(1000, _options.QueryTimeoutSeconds * 1000);

            ApplyServerParameters(command, parameters);

            if (IsRowReturningStatement(statement))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await ReadGridFromDataReaderAsync(reader, state, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                state.NonQueryStatementCount++;
            }

            if (IsSchemaChangingStatement(statement))
            {
                state.SchemaChanged = true;
            }
        }

        return state;
    }

    private async Task ReadGridFromDataReaderAsync(DbDataReader reader, QueryExecutionState state, CancellationToken cancellationToken)
    {
        state.Columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();

        state.Rows.Clear();
        state.Truncated = false;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (state.Rows.Count >= _options.ResultRowLimit)
            {
                state.Truncated = true;
                break;
            }

            var values = new string?[reader.FieldCount];
            for (int index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                values[index] = value is DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            state.Rows.Add(new QueryResultRow
            {
                Values = values
            });
        }
    }

    private static IReadOnlyDictionary<string, object?> ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            using var document = JsonDocument.Parse(parametersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Parameters JSON must be an object with key/value entries.");
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = ConvertJsonElement(property.Value);
            }

            return values;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid parameters JSON: {ex.Message}", ex);
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var int32) => int32,
            JsonValueKind.Number when element.TryGetInt64(out var int64) => int64,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }

    private static void ApplyLocalParameters(SharpCoreDBCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var normalizedName = name.StartsWith('@') ? name : $"@{name}";
            command.Parameters.Add(normalizedName, value);
        }
    }

    private static void ApplyServerParameters(SharpCoreDB.Client.SharpCoreDBCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var normalizedName = name.StartsWith('@') ? name : $"@{name}";
            command.AddParameter(normalizedName, value);
        }
    }

    private static string BuildSummary(int statementCount, int rowCount, int nonQueryStatementCount, bool truncated, long executionTimeMs)
    {
        var resultSummary = rowCount > 0
            ? $"Returned {rowCount} row(s) from the last SELECT"
            : "No result rows returned";

        if (truncated)
        {
            resultSummary += " (truncated)";
        }

        return $"Executed {statementCount} statement(s) in {executionTimeMs} ms. {resultSummary}. Non-query statements: {nonQueryStatementCount}.";
    }

    /// <summary>
    /// Appends a quoted literal (string, identifier or backtick) starting at <paramref name="start"/>
    /// (the opening quote) to <paramref name="builder"/>, handling doubled-quote escapes.
    /// Returns the index of the closing quote, or the end of the input for unterminated literals.
    /// </summary>
    private static int AppendQuotedLiteral(StringBuilder builder, string sql, int start, char quote)
    {
        var i = start;
        builder.Append(sql[i]); // opening quote
        i++;

        while (i < sql.Length)
        {
            var ch = sql[i];
            if (ch == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    builder.Append(quote).Append(quote);
                    i += 2;
                    continue;
                }

                builder.Append(ch); // closing quote
                return i;
            }

            builder.Append(ch);
            i++;
        }

        return i;
    }

    /// <summary>
    /// Splits a batch into individual statements on semicolons that are outside string
    /// literals ('…', "…", `…'), line comments (-- …) and block comments (/* … */).
    /// </summary>
    private static string[] SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder();

        var i = 0;
        while (i < sql.Length)
        {
            var ch = sql[i];
            if (ch is '\'' or '"' or '`')
            {
                i = AppendQuotedLiteral(current, sql, i, ch);
                i++;
            }
            else if (ch == '-' && IsLineCommentStart(sql, i))
            {
                i = AppendLineComment(current, sql, i);
            }
            else if (ch == '/' && IsBlockCommentStart(sql, i))
            {
                i = AppendBlockComment(current, sql, i);
            }
            else if (ch == ';')
            {
                FlushStatement(statements, current);
                i++;
            }
            else
            {
                current.Append(ch);
                i++;
            }
        }

        FlushStatement(statements, current);
        return [.. statements];
    }

    private static void FlushStatement(List<string> statements, StringBuilder current)
    {
        var statement = current.ToString().Trim();
        if (statement.Length > 0)
        {
            statements.Add(statement);
        }

        current.Clear();
    }

    private static bool IsLineCommentStart(string sql, int index) =>
        index + 1 < sql.Length && sql[index + 1] == '-';

    private static bool IsBlockCommentStart(string sql, int index) =>
        index + 1 < sql.Length && sql[index + 1] == '*';

    private static int AppendLineComment(StringBuilder builder, string sql, int start)
    {
        var i = start;
        while (i < sql.Length && sql[i] != '\n')
        {
            builder.Append(sql[i]);
            i++;
        }

        return i;
    }

    private static int AppendBlockComment(StringBuilder builder, string sql, int start)
    {
        var i = start;
        while (i < sql.Length)
        {
            if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
            {
                builder.Append('*').Append('/');
                return i + 2;
            }

            builder.Append(sql[i]);
            i++;
        }

        return i;
    }

    private static int SkipLineComment(string sql, int start)
    {
        var i = start;
        while (i < sql.Length && sql[i] != '\n')
        {
            i++;
        }

        return i;
    }

    private static int StripBlockComment(StringBuilder result, string sql, int start)
    {
        var i = start + 2;
        while (i < sql.Length)
        {
            if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
            {
                result.Append(' ');
                return i + 2;
            }

            i++;
        }

        return i;
    }

    /// <summary>
    /// Removes line comments (-- …) and block comments (/* … */) from SQL while preserving
    /// the content of string literals and quoted identifiers. The SharpCoreDB engine does not
    /// strip SQL comments itself, so leading/embedded comments would otherwise break parsing.
    /// </summary>
    private static string StripComments(string sql)
    {
        var result = new StringBuilder(sql.Length);

        var i = 0;
        while (i < sql.Length)
        {
            var ch = sql[i];
            if (ch is '\'' or '"' or '`')
            {
                i = AppendQuotedLiteral(result, sql, i, ch);
                i++;
            }
            else if (ch == '-' && IsLineCommentStart(sql, i))
            {
                i = SkipLineComment(sql, i);
            }
            else if (ch == '/' && IsBlockCommentStart(sql, i))
            {
                i = StripBlockComment(result, sql, i);
            }
            else
            {
                result.Append(ch);
                i++;
            }
        }

        return result.ToString();
    }

    private static bool IsRowReturningStatement(string statement)
    {
        var trimmed = statement.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) // CTE: WITH cte AS (...) SELECT ...
            || trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase) // EXPLAIN [QUERY PLAN] SELECT ...
            || trimmed.StartsWith("VALUES", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSchemaChangingStatement(string statement)
    {
        var trimmed = statement.TrimStart();
        return trimmed.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ALTER ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DROP ", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class QueryExecutionState
    {
        public IReadOnlyList<string> Columns { get; set; } = [];

        public List<QueryResultRow> Rows { get; } = [];

        public int NonQueryStatementCount { get; set; }

        public bool Truncated { get; set; }

        public bool SchemaChanged { get; set; }
    }
}
