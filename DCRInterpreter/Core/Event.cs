using MessagePack;
using MessagePack.Formatters;
using System;

[MessagePackObject]
[MessagePackFormatter(typeof(DCRFastInterpreter.EventFormatter))] // explicitly bind
public class Event
{
    [Key(660)]
    public string Id { get; set; }
    [Key(661)]
    public bool Executed { get; set; } = false;
    [Key(662)]
    public bool Included { get; set; } = false;
    [Key(663)]
    public bool Pending { get; set; } = false;
    [IgnoreMember]
    public int? InstanceId { get; set; } // Unique per spawn
    [IgnoreMember]
    public bool IsTemplateEvent => InstanceId != null;

    [Key(664)]
    public string? Label { get; set; }
    [Key(665)]
    public string? Description { get; set; }
    [Key(666)]
    public EventType Type { get; set; } = EventType.Task;
    [Key(667)]
    public object? Data { get; set; }
    [Key(668)]
    public List<string> Roles { get; set; } = new();
    [Key(669)]
    public List<string> ReadRoles { get; set; } = new();
    [Key(670)]
    public List<string> kIds { get; set; } = new();
    [IgnoreMember]
    public List<Event> Children { get; set; } = new();
    [IgnoreMember]
    public Event? Parent { get; set; }
    [IgnoreMember]
    public Func<DCRGraph, string?, List<string>> CompiledLogic { get; set; } = null!;
    [Key(671)]
    public DCRGraph? Template { get; set; }
    public Event(string id)
    {
        Id = id;
    }
    public Event CloneWithId(string newId, int instanceId, object? newData = null)
    {
        return new Event(newId)
        {
            InstanceId = instanceId,
            Label = this.Label,
            Description = this.Description,
            Type = this.Type,
            Data = newData ?? this.Data,
            Roles = this.Roles.ToList(),
            ReadRoles = this.ReadRoles.ToList(),
            Included = this.Included,
            Pending = this.Pending,
            Executed = this.Executed,
            Children = this.Children.ToList(),
            Parent = this.Parent
        };
    }

    
    public bool IsRobot()
    {
        if (this.Roles.Any(role => role.ToLower().Equals("robot")))
        {
            return true;
        }
        return false;
    }
}

public enum EventType
{
    Task,
    Form,
    Template
}

