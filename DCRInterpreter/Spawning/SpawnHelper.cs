using Newtonsoft.Json;

public static class SpawnHelper
{
    public static void SpawnEachA(DCRGraph graph, string templateId, string spawnData)
    {
        if (spawnData.StartsWith("$"))
        {
            spawnData = graph.Values[spawnData]?.ToString() ?? throw new ArgumentException($"Value for {spawnData} not found");
        }
        var entries = ParseSpawnData(spawnData);

        // Generate all instance IDs upfront
        var instanceIds = Enumerable.Range(0, entries.Count)
            .Select(_ => graph.GetNextInstanceId())
            .ToList();

        Parallel.ForEach(entries.Zip(instanceIds), (pair, token) =>
        {
            ProcessSpawnEntry(graph, templateId, pair.First, pair.Second);
        });
    }
    private static void ProcessSpawnEntry(DCRGraph graph, string templateId,
    Dictionary<string, object?> data, int instanceId)
    {
       // No Task.Run
        DCRGraph template = graph.Templates[templateId];
        graph.AddSpawnedInstance(template, templateId, instanceId);
        graph.ApplySpawnData(template, templateId, instanceId, data);
        graph.CompileSpawnedInstance(template, templateId, instanceId);

    }
    public static void SpawnEach(DCRGraph graph, string templateId, string? spawnData)
    {
        if (spawnData == null)
        {
            graph.AddSpawnWithData(templateId);
        }
        else
        {
            try
            {
                SpawnEachA(graph, templateId, spawnData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SpawnEach: {ex.Message}");
            }
        }
    }




    public static List<Dictionary<string, object?>> ParseSpawnData(string spawnData)
    {
        try
        {
            return JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(spawnData)
                ?? new List<Dictionary<string, object?>>();
        }
        catch
        {
            return new List<Dictionary<string, object?>>();
        }
    }
}
