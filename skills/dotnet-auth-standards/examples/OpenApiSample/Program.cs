using OpenApiSample;

if (args.Contains("selftest"))
{
    return await SelfTest.RunAsync();
}

SampleApi.Build(args).Run();
return 0;
