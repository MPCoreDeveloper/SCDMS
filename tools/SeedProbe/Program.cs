using Microsoft.Extensions.Options;
using SharpCoreDB.Data.Provider;
using Scdms.Models;
using Scdms.Services;

// SeedProbe: validates that SampleDatabaseCatalog can seed the default "scdb"
// database and both sample databases end-to-end, and that the seeded row counts
// and NULL handling are correct. Run with: dotnet run --project SeedProbe

var failures = 0;

var probeRoot = Path.Combine(Path.GetTempPath(), "scdb-seed-probe-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(probeRoot);
Console.WriteLine($"Probe data root: {probeRoot}");

var options = Options.Create(new ScdmsOptions
{
    DefaultDatabaseName = "scdb",
    DefaultDatabasePath = string.Empty,
    SampleDatabasesDirectory = probeRoot
});

var catalog = new SampleDatabaseCatalog(options);

try
{
    failures += await ProbeDefaultDatabaseAsync().ConfigureAwait(false);
    failures += await ProbeSampleAsync(SampleDatabaseCatalog.ContosoSampleName, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["customers"] = 5,
        ["products"] = 6,
        ["orders"] = 5,
        ["order_items"] = 7,
        ["inventory"] = 6
    }).ConfigureAwait(false);
    failures += await ProbeSampleAsync(SampleDatabaseCatalog.AdventureWorksSampleName, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["product_categories"] = 4,
        ["products"] = 7,
        ["customers"] = 5,
        ["sales_territories"] = 4,
        ["sales_orders"] = 5,
        ["sales_order_details"] = 6
    }).ConfigureAwait(false);
    failures += await ProbeNullColorAsync().ConfigureAwait(false);
    failures += await ProbeIdempotencyAsync().ConfigureAwait(false);
}
finally
{
    try
    {
        Directory.Delete(probeRoot, recursive: true);
        Console.WriteLine("Probe data root cleaned up.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARN: could not delete probe root: {ex.Message}");
    }
}

if (failures == 0)
{
    Console.WriteLine("SEED PROBE: ALL CHECKS PASSED");
    return 0;
}

Console.WriteLine($"SEED PROBE: {failures} CHECK(S) FAILED");
return 1;

async Task<int> ProbeDefaultDatabaseAsync()
{
    var probeFailures = 0;
    await catalog.EnsureDefaultDatabaseAsync().ConfigureAwait(false);
    var path = catalog.GetDefaultDatabasePath();
    probeFailures += Check(File.Exists(Path.Combine(path, ".seeded")), "default database has .seeded marker");

    var count = await ScalarIntAsync(path, "SELECT COUNT(*) FROM welcome").ConfigureAwait(false);
    probeFailures += Check(count == 3, $"default database welcome row count = 3 (actual {count})");
    return probeFailures;
}

async Task<int> ProbeSampleAsync(string sampleName, IReadOnlyDictionary<string, int> expectedCounts)
{
    var probeFailures = 0;
    await catalog.EnsureSampleAsync(sampleName).ConfigureAwait(false);
    var path = catalog.GetSampleDatabasePath(sampleName);
    probeFailures += Check(File.Exists(Path.Combine(path, ".seeded")), $"sample '{sampleName}' has .seeded marker");

    foreach (var (table, expected) in expectedCounts)
    {
        var count = await ScalarIntAsync(path, $"SELECT COUNT(*) FROM {table}").ConfigureAwait(false);
        probeFailures += Check(count == expected, $"sample '{sampleName}' table '{table}' row count = {expected} (actual {count})");
    }

    return probeFailures;
}

async Task<int> ProbeNullColorAsync()
{
    var probeFailures = 0;
    var path = catalog.GetSampleDatabasePath(SampleDatabaseCatalog.AdventureWorksSampleName);
    var connectionString = new SharpCoreDBConnectionStringBuilder
    {
        Path = path,
        Password = options.Value.DefaultDatabasePassword,
        Cache = "Private"
    }.ConnectionString;

    await using var connection = new SharpCoreDBConnection(connectionString);
    await connection.OpenAsync().ConfigureAwait(false);
    await using var command = new SharpCoreDBCommand("SELECT * FROM products", connection);
    await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

    Console.WriteLine($"INFO: FieldCount={reader.FieldCount}");
    for (var i = 0; i < reader.FieldCount; i++)
    {
        Console.WriteLine($"INFO: column[{i}] = {reader.GetName(i)}");
    }

    var colorOrdinal = -1;
    for (var i = 0; i < reader.FieldCount; i++)
    {
        if (string.Equals(reader.GetName(i), "color", StringComparison.OrdinalIgnoreCase))
        {
            colorOrdinal = i;
        }
    }

    var foundNullColor = false;
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
        var id = Convert.ToInt32(reader.GetValue(0));
        if (id == 7)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var v = reader.GetValue(i);
                Console.WriteLine($"INFO: row7 [{reader.GetName(i)}] = '{v}' (IsDBNull={reader.IsDBNull(i)})");
            }

            foundNullColor = colorOrdinal >= 0 && reader.IsDBNull(colorOrdinal);
        }
    }

    probeFailures += Check(foundNullColor, "adventureworks product 7 (Water Bottle) color is SQL NULL (not the string 'NULL')");
    return probeFailures;
}

async Task<int> ProbeIdempotencyAsync()
{
    var probeFailures = 0;
    // Second run must short-circuit on the marker file without touching data.
    await catalog.EnsureSampleAsync(SampleDatabaseCatalog.ContosoSampleName).ConfigureAwait(false);
    var path = catalog.GetSampleDatabasePath(SampleDatabaseCatalog.ContosoSampleName);
    var count = await ScalarIntAsync(path, "SELECT COUNT(*) FROM customers").ConfigureAwait(false);
    probeFailures += Check(count == 5, $"re-seed is idempotent; customers row count still 5 (actual {count})");
    return probeFailures;
}

async Task<int> ScalarIntAsync(string databasePath, string sql)
{
    var connectionString = new SharpCoreDBConnectionStringBuilder
    {
        Path = databasePath,
        Password = options.Value.DefaultDatabasePassword,
        Cache = "Private"
    }.ConnectionString;

    await using var connection = new SharpCoreDBConnection(connectionString);
    await connection.OpenAsync().ConfigureAwait(false);
    await using var command = new SharpCoreDBCommand(sql, connection);
    var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
    return Convert.ToInt32(result);
}

int Check(bool condition, string description)
{
    if (condition)
    {
        Console.WriteLine($"PASS: {description}");
        return 0;
    }

    Console.WriteLine($"FAIL: {description}");
    return 1;
}
