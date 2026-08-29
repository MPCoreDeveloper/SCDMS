using SeedProbe;

// SeedProbe: validates that SampleDatabaseCatalog can seed the default "scdb"
// database and both sample databases end-to-end, and that the seeded row counts
// and NULL handling are correct. Run with: dotnet run --project SeedProbe

return await ProbeRunner.RunAsync().ConfigureAwait(false);
