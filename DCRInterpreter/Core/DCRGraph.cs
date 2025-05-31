using System.Collections.Concurrent;
using MessagePack;

[MessagePackObject]
public class DCRGraph
{

    [Key(0)]
    public string Title { get; set; }
    [Key(1)]
    public string Id { get; set; } = "0";
    [Key(2)]
    public ConcurrentBag<Relationship> Relationships { get; set; } = new();
    [Key(3)]
    public ConcurrentDictionary<string, Event> Events { get; set; } = new();
    [Key(4)]
    public Dictionary<string, DcrExpression> Expressions { get; set; } = new Dictionary<string, DcrExpression>();
    [Key(5)]
    public ConcurrentDictionary<int, List<string>> SpawnedInstances { get; set; } = new(); // InstanceID -> [EventIDs]

    [IgnoreMember]
    public HashSet<string> IncludedEvents => Events.Values.Where(e => e.Included).Select(e => e.Id).ToHashSet(); // I'm not manually updating this in IL
    [IgnoreMember]
    public HashSet<string> PendingEvents => Events.Values.Where(e => e.Pending).Select(e => e.Id).ToHashSet();
    [IgnoreMember]
    public HashSet<string> ExecutedEvents => Events.Values.Where(e => e.Executed).Select(e => e.Id).ToHashSet();
    [IgnoreMember]
    private StateUpdateCompiler UpdateCompiler = null!;
    [IgnoreMember]
    public IEnumerable<Event> Robots => Events.Where(e => e.Value.Included && e.Value.Pending && e.Value.IsRobot()).Select(e => e.Value);
    [IgnoreMember]
    public IEnumerable<Relationship> Conditions => Relationships.Where(r => r.Type is RelationshipType.Condition);
    [IgnoreMember]
    public IEnumerable<Relationship> Responses => Relationships.Where(r => r.Type is RelationshipType.Response);
    [IgnoreMember]
    public IEnumerable<Relationship> Inclusions => Relationships.Where(r => r.Type is RelationshipType.Include);
    [IgnoreMember]
    public IEnumerable<Relationship> Exclusions => Relationships.Where(r => r.Type is RelationshipType.Exclude);
    [IgnoreMember]
    public IEnumerable<Relationship> Milestones => Relationships.Where(r => r.Type is RelationshipType.Milestone);
    [IgnoreMember]
    public IEnumerable<Relationship> Updates => Relationships.Where(r => r.Type is RelationshipType.Update);
    [IgnoreMember]
    public IEnumerable<Relationship> Spawns => Relationships.Where(r => r.Type is RelationshipType.Spawn);
    [IgnoreMember]
    public Dictionary<string, DCRGraph> Templates => Events.Where(e => e.Value.Template != null)
        .ToDictionary(e => e.Key, e => e.Value.Template!);
    [IgnoreMember]
    public Dictionary<string, object?> Values
    {
        get
        {
            var values = new Dictionary<string, object?>();
            foreach (var e in Events)
            {
                values.Add(e.Key, e.Value.Data);
            }
            return values;
        }
    }
    public DCRGraph(string title)
    {
        this.Title = title;
    }

    public DCRGraph(DCRGraph other)
    {
        this.Title = other.Title;
        this.Id = other.Id;
        this.Relationships = new(other.Relationships);
        this.Expressions = new(other.Expressions);
        this.Events = new(other.Events);
        Initialize();
    }
    public void Initialize()
    {
        UpdateCompiler = new StateUpdateCompiler(this);

        // Precompile logic for each event
        Parallel.ForEach(
                Events.Values,
                e =>
                {
                    e.CompiledLogic = UpdateCompiler.GenerateLogicForEvent(e.Id);
                });

    }


    public static bool EvaluateExpression(string expressionValue, Dictionary<string, object?>? variables)
    {
        Dictionary<string, object?>? paramList = new();
        if (variables != null)
            foreach (var item in variables)
            {
                if (item.Value == null)
                {
                    paramList.Add(item.Key, null);
                    continue;
                }
                ;
                if (int.TryParse(item.Value.ToString(), out var i))
                {
                    paramList.Add(item.Key, i);
                    continue;
                }
                if (bool.TryParse(item.Value.ToString(), out var b))
                {
                    paramList.Add(item.Key, b);
                    continue;
                }
                if (float.TryParse(item.Value.ToString(), out var f))
                {
                    paramList.Add(item.Key, f);
                    continue;
                }
                if (DateTimeOffset.TryParse(item.Value.ToString(), out var dt))
                {
                    paramList.Add(item.Key, dt);
                    continue;
                }
                paramList.Add(item.Key, item.Value);

            }
        Dictionary<string, object?>? newParamList = new();
        foreach (var item in paramList)
        {
            if (item.Key.Contains(":"))
            {
                var newKey = item.Key.Split(':')[1];
                newParamList.Add(newKey, item.Value);
                expressionValue = expressionValue.Replace(item.Key, newKey);
            }
            newParamList.Add(item.Key, item.Value);
        }
        var expression = new NCalc.Expression(expressionValue, NCalc.ExpressionOptions.AllowNullOrEmptyExpressions);
        expression.Parameters = newParamList;
        var x = (bool)expression.Evaluate();
        return x;
    }

    public List<string> ExecuteEvent(string eventId, string? data = null)
    {
        if (!Events.ContainsKey(eventId))
            throw new ArgumentException($"Event {eventId} not found.");

        var e = Events[eventId];
        // Execute precompiled logic using DynamicMethod
        return e.CompiledLogic(this, data);
    }
    [IgnoreMember]
    private int _instanceIdCounter = 0;
    public void AddSpawnWithData(string templateId)
    {
        int instanceId;
        lock (SpawnedInstances)
        {
            instanceId = _instanceIdCounter;
            var template = Templates[templateId];
            AddSpawnedInstance(template, templateId, instanceId); // Now uses atomic counter
        }

    }
    public int GetNextInstanceId() => Interlocked.Increment(ref _instanceIdCounter) - 1;

    public void ApplySpawnData(DCRGraph template, string templateId, int instanceId, Dictionary<string, object?> data)
    {
        if (data == null || data.Count == 0) return;

        Parallel.ForEach(data.Keys, key =>
        {
            var eventId = $"{templateId}:{instanceId}:{key}";

            if (!Events.TryGetValue(eventId, out var existingEvent))
            {
                if (!template.Events.ContainsKey(key)) return;
                throw new ArgumentException($"Event {eventId} not found.");
            }

            var updatedEvent = existingEvent.CloneWithId(eventId, instanceId, data[key]);

            Events[eventId] = updatedEvent; // Thread-safe if Events is a ConcurrentDictionary
        });
    }


    public void CompileSpawnedInstance(DCRGraph template, string templateId, int instanceId)
    {
        Parallel.ForEach(template.Events.Values, e =>
        {
            var newID = $"{templateId}:{instanceId}:{e.Id}";
            if (Events.TryGetValue(newID, out var evt))
            {
                evt.CompiledLogic = UpdateCompiler.GenerateLogicForEvent(newID);
            }
        });
    }

    public void AddSpawnedInstance(DCRGraph template, string templateId, int instanceId)
    {
        var instanceEvents = new ConcurrentBag<string>(); // Thread-safe
        var eventIdMap = new ConcurrentDictionary<string, string>();

        // Step 1: Clone events in parallel
        Parallel.ForEach(template.Events.Values, e =>
        {
            var newID = $"{templateId}:{instanceId}:{e.Id}";
            Events[newID] = e.CloneWithId(newID, instanceId);
            instanceEvents.Add(newID);
            eventIdMap[e.Id] = newID;
        });

        // Step 2: Save the instanceEvents list (converting ConcurrentBag to List)
        SpawnedInstances[instanceId] = instanceEvents.ToList();

        // Step 3: Copy relationships in parallel

        Parallel.ForEach(template.Relationships, r =>
        {
            var targetId = eventIdMap.TryGetValue(r.TargetId, out var tid)
                ? tid
                : $"{templateId}:{instanceId}:{r.TargetId}";

            var sourceId = eventIdMap.TryGetValue(r.SourceId, out var sid)
                ? sid
                : $"{templateId}:{instanceId}:{r.SourceId}";

            var newRelationship = new Relationship(targetId, sourceId, r.Type)
            {
                GuardExpression = r.GuardExpression
            };

            Relationships.Add(newRelationship);
        });

    }
}

[MessagePackObject]
public class Relationship
{
    [Key(0)]
    public string SourceId { get; set; }
    [Key(1)]
    public string TargetId { get; set; }
    [Key(2)]
    public RelationshipType Type { get; set; }
    [IgnoreMember]
    public string? GuardExpressionId => GuardExpression?.Id;
    [Key(3)]
    public DcrExpression? GuardExpression { get; set; }
    [Key(4)]
    public string? SpawnData { get; set; } // Data to be passed to the spawned event

    public Relationship(string source, string target, RelationshipType relationshipType)
    {
        SourceId = source;
        TargetId = target;
        Type = relationshipType;
    }
}
[MessagePackObject]
public class DcrExpression
{
    [Key(0)]
    public string Id { get; set; } // Unique identifier for the expression
    [Key(1)]
    public string Value { get; set; } // The actual expression (e.g., "count(global) > 1")

    public bool Evaluate(DCRGraph graph)
    {
        var values = graph.Values.Where(v => Value.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
        return DCRGraph.EvaluateExpression(Value, values);
    }
}


public enum RelationshipType
{
    Condition,
    Response,
    Include,
    Exclude,
    Milestone,
    Update,
    Spawn
}
