using ProductionSamples;

if (args.Contains("selftest")) return await SelfTest.RunAsync();
Console.WriteLine("ProductionSamples: run `dotnet run -- selftest` (in-memory TestServer, no ports are opened).");
return 0;
