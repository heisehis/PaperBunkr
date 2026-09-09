using BenchmarkDotNet.Running;

// dotnet run -c Release --project src/Paperbunkr.Benchmarks -- --filter *ReaderPipeline*
BenchmarkSwitcher.FromAssembly(typeof(Paperbunkr.Benchmarks.ReaderPipelineBenchmarks).Assembly).Run(args);
