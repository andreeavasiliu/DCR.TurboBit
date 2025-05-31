using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

public class StateUpdateCompiler
{
    private DCRGraph Graph;

    public StateUpdateCompiler(DCRGraph graph)
    {
        Graph = graph;
    }

    // Generate and compile a DynamicMethod for an event
    public Func<DCRGraph, string?, List<string>> GenerateLogicForEvent(string eventId)
    {
        var method = new DynamicMethod(
            $"Execute_{eventId}",
            typeof(List<string>),                      // Return type
            new[] { typeof(DCRGraph), typeof(string) },        // Parameter types
            typeof(DCRGraph).Module            // Owner module
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
        il.Emit(OpCodes.Ldstr, eventId);
        il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Data").SetMethod);

        // Generate IL for relationship rules 

        List<string> targets = new List<string>();

        void ExecuteEvent(string eventId)
        {
            // Clear pending state for the executed event
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
            il.Emit(OpCodes.Ldstr, eventId);
            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
            il.Emit(OpCodes.Ldc_I4_0); // Load constant false (Pending = false)
            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Pending").SetMethod);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
            il.Emit(OpCodes.Ldstr, eventId);
            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
            il.Emit(OpCodes.Ldc_I4_1); // Load constant false (Executed = true)
            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Executed").SetMethod);
            foreach (var relation in Graph.Relationships)
            {
                if (relation.SourceId == eventId)
                {
                    Label continueLabel = il.DefineLabel();
                    if (relation.GuardExpressionId != null)
                    {
                        // Load DCRGraph parameter
                        il.Emit(OpCodes.Ldarg_0);

                        // Load Expressions property
                        il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Expressions")?.GetGetMethod()
                            ?? throw new Exception("Expressions property not found"));

                        // Load guard expression ID
                        il.Emit(OpCodes.Ldstr, relation.GuardExpressionId);

                        // Call get_Item on Dictionary<string, DcrExpression>
                        var getItemMethod = typeof(Dictionary<string, DcrExpression>)
                            .GetMethod("get_Item", new[] { typeof(string) })
                            ?? throw new Exception("Dictionary get_Item(string) method not found");

                        il.Emit(OpCodes.Callvirt, getItemMethod);

                        // Load DCRGraph instance again as an argument for Evaluate()
                        il.Emit(OpCodes.Ldarg_0);

                        // Call Evaluate(DCRGraph)
                        var evalMethod = typeof(DcrExpression).GetMethod("Evaluate", new[] { typeof(DCRGraph) })
                            ?? throw new Exception("Evaluate(DCRGraph) method not found");

                        il.Emit(OpCodes.Callvirt, evalMethod);

                        // Branch if Evaluate() returns false
                        il.Emit(OpCodes.Brfalse, continueLabel);
                    }
                    //I know it looks silly. but i'm guessing the relationships that are not covered here
                    //have incomplete pieces of code that super-break everything
                    switch (relation.Type)
                    {
                        case RelationshipType.Response:
                            targets.Add(relation.TargetId);

                            il.Emit(OpCodes.Ldarg_0); // Load DCRGraph parameter
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.TargetId); // Load target ID
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Ldc_I4_1); // Load constant true (Pending = true)
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Pending").SetMethod);
                            break;

                        case RelationshipType.Include:
                            targets.Add(relation.TargetId);

                            il.Emit(OpCodes.Ldarg_0); // Load DCRGraph parameter
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.TargetId); // Load target ID
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Ldc_I4_1); // Load constant true (Included = true)
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Included").SetMethod);
                            break;

                        case RelationshipType.Exclude:
                            targets.Add(relation.TargetId);

                            il.Emit(OpCodes.Ldarg_0); // Load DCRGraph parameter
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.TargetId); // Load target ID
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Ldc_I4_0); // Load constant false (Included = false)
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Included").SetMethod);
                            break;

                        case RelationshipType.Update:
                            targets.Add(relation.TargetId);

                            //Copy Data from guard source to relation target
                            il.Emit(OpCodes.Ldarg_0); // Load DCRGraph parameter
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.GuardExpression.Value); // Load target ID
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Data").GetGetMethod());

                            LocalBuilder sourceData = il.DeclareLocal(typeof(object)); //works with null
                            il.Emit(OpCodes.Stloc, sourceData);

                            il.Emit(OpCodes.Ldarg_0); // Load DCRGraph parameter
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.TargetId); // Load target ID
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Ldloc, sourceData);
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Data").GetSetMethod());


                            // Clear pending state for the executed target
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.TargetId);
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Ldc_I4_0); // Load constant false (Pending = false)
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Pending").SetMethod);

                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Callvirt, typeof(DCRGraph).GetProperty("Events").GetGetMethod());
                            il.Emit(OpCodes.Ldstr, relation.TargetId);
                            il.Emit(OpCodes.Callvirt, typeof(ConcurrentDictionary<string, Event>).GetMethod("get_Item"));
                            il.Emit(OpCodes.Ldc_I4_1); // Load constant false (Executed = true)
                            il.Emit(OpCodes.Callvirt, typeof(Event).GetProperty("Executed").SetMethod);

                            break;
                        case RelationshipType.Spawn:
                            targets.Add(relation.TargetId);
                            il.Emit(OpCodes.Ldarg_0); // DCRGraph
                            il.Emit(OpCodes.Ldstr, relation.TargetId); // template ID
                            if (relation.SpawnData == null)
                            {
                                il.Emit(OpCodes.Ldnull); // null
                            }
                            else
                                il.Emit(OpCodes.Ldstr, relation.SpawnData ); // JSON
                            il.Emit(OpCodes.Call, typeof(SpawnHelper).GetMethod("SpawnEach"));

                            break;

                    }
                    il.MarkLabel(continueLabel);
                }
            }
        }

        ExecuteEvent(eventId);


        ConstructorInfo listCtor = typeof(List<string>).GetConstructor(Type.EmptyTypes);
        il.Emit(OpCodes.Newobj, listCtor); // new List<string>()

        // (Optional) Store the new list in a local variable.
        LocalBuilder listLocal = il.DeclareLocal(typeof(List<string>));
        il.Emit(OpCodes.Stloc, listLocal);

        // Return from the method
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);

        return (Func<DCRGraph, string?, List<string>>)method.CreateDelegate(typeof(Func<DCRGraph, string?, List<string>>));
    }
}
