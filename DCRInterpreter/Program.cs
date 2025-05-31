using DCR.Workflow;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Xml.Linq;

class Program
{
    static string SpawnData = "";
    static string SpawnI = "";
    static void Main(string[] args)
    {
        string xmlFilePath = "xml/spawn_bench.xml";
        string xmlFilePath1 = "xml/job_interview.xml";
        string xmlFilePath2 = "xml/DCR-interpreter.xml";
        string xmlFilePath3 = "xml/the_ultimate_test.xml";
        string jsonDummyData = "json/mock_data.json";

        if (!File.Exists(xmlFilePath))
        {
            Console.WriteLine($"Error: File '{xmlFilePath}' not found.");
            return;
        }
        if(!File.Exists(jsonDummyData))
        {
            Console.WriteLine($"Error: File '{jsonDummyData}' not found.");
            return;
        }

        SpawnData = File.ReadAllText(jsonDummyData);

        DoParseBenchmark(XDocument.Load(xmlFilePath3));
        DoExecBenchmark(XDocument.Load(xmlFilePath2));
        DoBenchmark(XDocument.Load(xmlFilePath));
        

        Console.ReadKey();
    }

    static void DoParseBenchmark(XDocument doc)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ParseBin(doc, 10000);
        Thread.Sleep(2000);
        ParseRuntime(doc, 3000);
        Thread.Sleep(2000);
        ParseInterpret(doc, 10000);
        Thread.Sleep(2000);
    }
    static void DoExecBenchmark(XDocument doc)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        BenchInterpExec(doc);
        Thread.Sleep(2000);
        BenchRuntimeExec(doc);
        Thread.Sleep(2000);
    }
    static void DoBenchmark(XDocument doc)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        BenchBinary(doc);
        Thread.Sleep(2000);
        BenchInterp(doc);
        Thread.Sleep(2000);
    }

    static void ParseBin(XDocument original, int maxtime)
    {
        DCRGraph pregraph = DCRInterpreter.ParseDCRGraphFromXml(original);
        var bin = DCRFastInterpreter.Serialize(pregraph);
        var parse = new Stopwatch();
        int count = 0;
        while (count <= maxtime)
        {
            parse.Start();
            var graph = DCRFastInterpreter.Deserialize(bin);
            graph.Initialize();
            parse.Stop();
            count++;
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"TurboBit Deserialize:  {parse.Elapsed.TotalNanoseconds / count} ns");
    }
    static void ParseRuntime(XDocument original, int maxtime)
    {
        var runtime = DCR.Workflow.Runtime.Create(builder =>
        {
            builder.WithOptions(options =>
                options.UpdateModelLog = true
            );
        });

        var parse = new Stopwatch();
        int count = 0;
        while (count <= maxtime)
        {
            parse.Start();
            var model = runtime.Parse(original);
            parse.Stop();
            count++;
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Runtime Deserialize:  {parse.Elapsed.TotalNanoseconds / count} ns");
    }

    static void ParseInterpret(XDocument original, int maxtime)
    {
        var parse = new Stopwatch();
        int count = 0;
        while (count <= maxtime)
        {
            parse.Start();
            DCRGraph graph = DCRInterpreter.ParseDCRGraphFromXml(original);
            graph.Initialize();
            parse.Stop();
            count++;
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Interpreter Deserialize:  {parse.Elapsed.TotalNanoseconds / count} ns");
    }

    static void BenchBinary(XDocument original)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("DCR TurboBit Spawn Benchmark");
        Console.WriteLine("====================================");
        Console.WriteLine($"{"I",4}  {"Mean",13}  {"Error",13}  {"StdDev",13}  {"Allocated",13}");

        DCRGraph pregraph = DCRInterpreter.ParseDCRGraphFromXml(original);

        var bin = DCRFastInterpreter.Serialize(pregraph);
        var durations = new List<double>();
        var allocations = new List<long>();

        foreach (var i in new int[] { 0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 })
        {
            var spawnData = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(SpawnData)
                    ?? new List<Dictionary<string, object?>>();
            SpawnI = JsonConvert.SerializeObject(spawnData.Take(i));

            for (int j = 0; j < 10; j++) // 10 runs for stats
            {
                var execute = new Stopwatch();

                var graph = DCRFastInterpreter.Deserialize(bin);

                graph.Relationships
                    .Where(x => x.Type == RelationshipType.Spawn)
                    .ToList()
                    .ForEach(x => x.SpawnData = SpawnI);
                graph.Initialize();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

                execute.Start();
                graph.ExecuteEvent("listspawn");
                execute.Stop();

                long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

                durations.Add(execute.Elapsed.TotalMilliseconds);
                allocations.Add(afterAlloc - beforeAlloc);

                //if (j == 0)
                //{
                //    var bin2 = DCRFastInterpreter.Serialize(graph);

                //    // Get project root (relative to current directory)
                //    var dumpDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "_dump");
                //    Directory.CreateDirectory(dumpDir); // Creates if it doesn't exist

                //    var filePath = Path.Combine(dumpDir, $"graph_dump_{i}.bin");
                //    File.WriteAllBytes(filePath, bin2);

                //    var fileInfo = new FileInfo(filePath);

                //    // File size in bytes
                //    long sizeBytes = fileInfo.Length;

                //    // Convert to kilobytes (rounded to 2 decimal places)
                //    double sizeKB = sizeBytes / 1024.0;

                //    // Assume typical 4KB cluster size
                //    int clusterSize = 4096;
                //    long sizeOnDiskBytes = ((sizeBytes + clusterSize - 1) / clusterSize) * clusterSize;
                //    double sizeOnDiskKB = sizeOnDiskBytes / 1024.0;

                //    Console.WriteLine($"File size: {sizeKB:F2} KB");
                //}
            }

            PrintStats(ConsoleColor.Cyan, i, durations, allocations);
        }
    }

    static void BenchRuntime(Runtime runtime, XDocument original, int maxtime, int i)
    {
        Stopwatch execute = new Stopwatch();
        Stopwatch parse = new Stopwatch();

        int loop = 0;
        int count = 0;

        while (execute.ElapsedMilliseconds <= maxtime)
        {
            parse.Start();
            var model = runtime.Parse(original);
            parse.Stop();


            execute.Start();

            runtime.Execute(model, model["employee_name"], DCR.Core.Data.value.NewString("Jim Bean"));
            runtime.Execute(model, model["employee_email"], DCR.Core.Data.value.NewString("jim@bean.org"));
            runtime.Execute(model, model["start_date"], DCR.Core.Data.value.NewDate(DateTimeOffset.MinValue));
            runtime.Execute(model, model["end_date"], DCR.Core.Data.value.NewDate(DateTimeOffset.Now));
            runtime.Execute(model, model["reason"], DCR.Core.Data.value.NewString("Tired"));
            runtime.Execute(model, model["vacation_request"]);
            runtime.Execute(model, model["approved"], DCR.Core.Data.value.NewBool(true));
            runtime.Execute(model, model["review_request"]);
            runtime.Execute(model, model["submit_to_hr"]);

            execute.Stop();
            loop++;
        }
        Measure(ConsoleColor.Yellow, "Workflow", execute.Elapsed, parse.Elapsed, loop, count, i);
    }

    static void BenchInterp(XDocument original)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("DCR Interpreter Spawn Benchmark");
        Console.WriteLine("====================================");
        Console.WriteLine($"{"I",4}  {"Mean",13}  {"Error",13}  {"StdDev",13}  {"Allocated",13}");

        var durations = new List<double>();
        var allocations = new List<long>();

        foreach (var i in new int[] { 0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 })
        {
            var spawnData = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(SpawnData)
                    ?? new List<Dictionary<string, object?>>();
            SpawnI = JsonConvert.SerializeObject(spawnData.Take(i));

            for (int j = 0; j < 10; j++) // 10 runs for stats
            {
                var execute = new Stopwatch();

                DCRGraph graph = DCRInterpreter.ParseDCRGraphFromXml(original);
                graph.Relationships
                    .Where(x => x.Type == RelationshipType.Spawn)
                    .ToList()
                    .ForEach(x => x.SpawnData = SpawnI);
                graph.Initialize();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

                execute.Start();
                graph.ExecuteEvent("listspawn");
                execute.Stop();

                long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

                durations.Add(execute.Elapsed.TotalMilliseconds);
                allocations.Add(afterAlloc - beforeAlloc);
            }

            PrintStats(ConsoleColor.Green, i, durations, allocations);
        }
    }

    static void BenchInterpExec(XDocument original)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("DCR TurboBit Exec  Benchmark");
        Console.WriteLine("====================================");
        Console.WriteLine($"{"I",4}  {"Mean",13}  {"Error",13}  {"StdDev",13}  {"Allocated",13}");

        var durations = new List<double>();
        var allocations = new List<long>();

        foreach (var i in new int[] { 0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 })
        {
            for (int j = 0; j < 10; j++)
            {
                var graphs = new List<DCRGraph>();
                for (int k = 0; k <= i-1; k+=10)
                {
                    DCRGraph graph = DCRInterpreter.ParseDCRGraphFromXml(original);
                    graph.Initialize();
                    graphs.Add(graph);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var execute = new Stopwatch();
                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
                
                execute.Start();

                //Execute I events
                foreach (var graph in graphs)
                {
                    graph.ExecuteEvent("employee_name", "Jim Bean");
                    graph.ExecuteEvent("employee_email", "jim@bean.org");
                    graph.ExecuteEvent("start_date", DateTimeOffset.MinValue.ToString());
                    graph.ExecuteEvent("end_date", DateTimeOffset.Now.ToString());
                    graph.ExecuteEvent("reason", "Tired");
                    graph.ExecuteEvent("vacation_request");
                    graph.ExecuteEvent("approved", "true");
                    
                    graph.ExecuteEvent("start_date", DateTimeOffset.MinValue.ToString());
                    graph.ExecuteEvent("end_date", DateTimeOffset.Now.ToString());
                    graph.ExecuteEvent("reason", "Tired");
                }

                execute.Stop();
                long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
                
                durations.Add(execute.Elapsed.TotalMilliseconds);
                allocations.Add(afterAlloc - beforeAlloc);
            }
            PrintStats(ConsoleColor.DarkGreen, i, durations, allocations);
        }
    }

    static void BenchRuntimeExec(XDocument original)
    {
        var runtime = DCR.Workflow.Runtime.Create(builder =>
        {
            builder.WithOptions(options =>
                options.UpdateModelLog = true
            );
        });
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("DCR Runtime Exec  Benchmark");
        Console.WriteLine("====================================");
        Console.WriteLine($"{"I",4}  {"Mean",13}  {"Error",13}  {"StdDev",13}  {"Allocated",13}");

        var durations = new List<double>();
        var allocations = new List<long>();

        foreach (var i in new int[] { 0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 })
        {
            for (int j = 0; j < 10; j++)
            {
                var graphs = new List<Model>();
                for (int k = 0; k <= i - 1; k += 10)
                {
                    var model = runtime.Parse(original);
                    graphs.Add(model);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var execute = new Stopwatch();
                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

                execute.Start();

                //Execute I events
                foreach (var graph in graphs)
                {
                    runtime.Execute(graph, graph["employee_name"], DCR.Core.Data.value.NewString("Jim Bean"));
                    runtime.Execute(graph, graph["employee_email"], DCR.Core.Data.value.NewString("jim@bean.org"));
                    runtime.Execute(graph, graph["start_date"], DCR.Core.Data.value.NewDate(DateTimeOffset.MinValue));
                    runtime.Execute(graph, graph["end_date"], DCR.Core.Data.value.NewDate(DateTimeOffset.Now));
                    runtime.Execute(graph, graph["reason"], DCR.Core.Data.value.NewString("Tired"));
                    runtime.Execute(graph, graph["vacation_request"]);
                    runtime.Execute(graph, graph["approved"], DCR.Core.Data.value.NewBool(true));
                    runtime.Execute(graph, graph["start_date"], DCR.Core.Data.value.NewDate(DateTimeOffset.MinValue));
                    runtime.Execute(graph, graph["end_date"], DCR.Core.Data.value.NewDate(DateTimeOffset.Now));
                    runtime.Execute(graph, graph["reason"], DCR.Core.Data.value.NewString("Tired"));
                }
                execute.Stop();
                long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

                durations.Add(execute.Elapsed.TotalMilliseconds);
                allocations.Add(afterAlloc - beforeAlloc);
            }
            PrintStats(ConsoleColor.DarkGreen, i, durations, allocations);
        }
    }

    static void Measure(ConsoleColor consoleColor, string ExecutionName, TimeSpan execution, TimeSpan parsing, int loop, int eventsperloop, int spawnCount)
    {
        string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            execution.Hours, execution.Minutes, execution.Seconds, execution.Milliseconds / 10);

        string elapsedParseTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            parsing.Hours, parsing.Minutes, parsing.Seconds, parsing.Milliseconds / 10);
        var executedCount = loop * eventsperloop;
        Console.ForegroundColor = consoleColor;
        Console.WriteLine($"{ExecutionName} I {spawnCount} executed:");
        // Console.WriteLine($"        the entire graph {loop} times!");
        // Console.WriteLine($"        that is {executedCount} total activities");
        // Console.WriteLine($"        and took {elapsedTime} to finish");
        Console.WriteLine($"        averaging {execution.TotalMilliseconds / executedCount}ms per event");
        // Console.WriteLine($"            *parsing time was not included in this time");
        // Console.WriteLine();
        // Console.WriteLine($"Parsing {loop} times took {elapsedParseTime} to finish");
        // Console.WriteLine($"        which means {parsing.TotalMilliseconds / loop}ms per parse");
        Console.WriteLine();
    }
    static void PrintStats(ConsoleColor consoleColor, int i, List<double> times, List<long> allocs)
    {
        var mean = times.Average();
        var stddev = Math.Sqrt(times.Select(t => Math.Pow(t - mean, 2)).Average());
        var error = stddev / Math.Sqrt(times.Count);

        var memAvg = allocs.Average() / 1024 / 1024;
        //Console.ForegroundColor = consoleColor;
        Console.WriteLine($"{i,4}  {mean,10:F2} ms  {error,10:F2} ms  {stddev,10:F2} ms  {memAvg,10:F2} MB");

    }
}