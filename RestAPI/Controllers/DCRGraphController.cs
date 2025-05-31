using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace RestAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DCRGraphController : ControllerBase
    {
        private readonly ILogger<DCRGraphController> _logger;
        private readonly GremlinClient _gremlinClient;
        public DCRGraphController(ILogger<DCRGraphController> logger, GremlinClient client)
        {
            _logger = logger;
            _gremlinClient = client;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetGraphById(string id)
        {
            try
            {
                var dcrGraph = await RetrieveAndParseGraph(id);
                return Ok(dcrGraph.Title);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving graph: {ex.Message}");
            }
        }

        [HttpPost]
        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
        public async Task<IActionResult> CreateGraph([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            using var stream = file.OpenReadStream();
            var document = XDocument.Load(stream);
            DCRGraph graph = DCRInterpreter.ParseDCRGraphFromXml(document);
            if (graph == null)
                return BadRequest("Invalid graph data.");

            try
            {
                var graphkey = graph.Id;
                // Add graph vertex
                var gremlinQuery = $"g.addV('DCRGraph').property('graph', '{graphkey}').property('id', '{graph.Id}').property('title', '{graph.Title}')";
                await _gremlinClient.SubmitAsync<dynamic>(gremlinQuery);

                // Insert vertices for each event
                foreach (var eventEntry in graph.Events)
                {
                    var eventId = eventEntry.Key;
                    var eventObj = eventEntry.Value;

                    var vertexQuery = $"g.addV('Event')" +
                                      $".property('graph', '{graphkey}')" +
                                      $".property('id', '{eventId.EscapeGremlinString()}')" +
                                      $".property('type', '{eventObj.Type}')" +
                                      $".property('executed', {eventObj.Executed.ToString().ToLower()})" +
                                      $".property('included', {eventObj.Included.ToString().ToLower()})" +
                                      $".property('pending', {eventObj.Pending.ToString().ToLower()})";

                    if (eventObj.Data != null)
                        vertexQuery += $".property('data', '{eventObj.Data}')";

                    if (eventObj.Roles.Count > 0)
                        vertexQuery += $".property('roles', '{string.Join(",", eventObj.Roles)}')";

                    if (eventObj.ReadRoles.Count > 0)
                        vertexQuery += $".property('readRoles', '{string.Join(",", eventObj.ReadRoles)}')";

                    if (!string.IsNullOrEmpty(eventObj.Description))
                        vertexQuery += $".property('description', '{eventObj.Description}')";

                    if (!string.IsNullOrEmpty(eventObj.Label))
                        vertexQuery += $".property('label', '{eventObj.Label}')";

                    await _gremlinClient.SubmitAsync<dynamic>(vertexQuery);

                    if (eventObj.Parent != null)
                    {
                        var parentEdgeQuery = $"g.V('{eventObj.Parent.Id}').addE('parentOf').to(g.V('{eventId}'))";
                        await _gremlinClient.SubmitAsync<dynamic>(parentEdgeQuery);
                    }
                }

                // Insert edges for each relationship
                foreach (var relationship in graph.Relationships)
                {
                    var edgeQuery = $"g.V('{relationship.SourceId}')" +
                                    $".addE('{relationship.Type}')" +
                                    $".to(g.V('{relationship.TargetId}'))";

                    // Add guard properties if present
                    if (!string.IsNullOrEmpty(relationship.GuardExpressionId))
                        edgeQuery += $".property('guardExpressionId', '{relationship.GuardExpressionId}')";

                    if (relationship.GuardExpression != null)
                        edgeQuery += $".property('guardExpressionValue', '{relationship.GuardExpression.Value}')";

                    await _gremlinClient.SubmitAsync<dynamic>(edgeQuery);
                }
                return CreatedAtAction(nameof(GetGraphById), new { id = graph.Id }, graph.Title);
            }
            catch (ResponseException e)
            {
                Console.WriteLine($"Error executing query: {e.Data}");
                Console.WriteLine($"Response error message: {e.Message}");
                return StatusCode((int)e.StatusCode, $"Error creating graph");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating graph: {ex.Message}");
            }

        }

        public class EventRequest
        {
            public string EventId { get; set; }
            public string Data { get; set; }
        }

        [HttpPost("{graphid}")]
        public async Task<IActionResult> ExecuteEvent([FromRoute] string graphid, [FromBody] EventRequest request)
        {
            try
            {
                string eventId = request.EventId;
                string data = request.Data;
                var dcrGraph = await RetrieveAndParseGraph(graphid); //Get graph from somewhere. Beyond the scope of the project
                dcrGraph.Initialize();
                if(!await CanExecuteEvent(eventId, graphid))
                    return BadRequest("Event cannot be executed.");
                var list = dcrGraph.ExecuteEvent(eventId, data);

                foreach (var item in list)
                {
                    var evt = dcrGraph.Events[item];

                    // Construct the update query by selecting the vertex and then setting its properties.
                    var updateQuery = $"g.V().has('graph', '{graphid}')" +
                                      $".has('id', '{evt.Id.EscapeGremlinString()}')" +
                                      $".property('type', '{evt.Type}')" +
                                      $".property('executed', {evt.Executed.ToString().ToLower()})" +
                                      $".property('included', {evt.Included.ToString().ToLower()})" +
                                      $".property('pending', {evt.Pending.ToString().ToLower()})";

                    if (evt.Data != null)
                        updateQuery += $".property(single, 'data', '{evt.Data}')";

                    if (evt.Roles.Count > 0)
                        updateQuery += $".property(single, 'roles', '{string.Join(",", evt.Roles)}')";

                    if (evt.ReadRoles.Count > 0)
                        updateQuery += $".property(single, 'readRoles', '{string.Join(",", evt.ReadRoles)}')";

                    if (!string.IsNullOrEmpty(evt.Description))
                        updateQuery += $".property(single, 'description', '{evt.Description}')";

                    if (!string.IsNullOrEmpty(evt.Label))
                        updateQuery += $".property(single, 'label', '{evt.Label}')";

                    await _gremlinClient.SubmitAsync<dynamic>(updateQuery);
                }

                // Example: Logging values
                Console.WriteLine($"Graph ID: {graphid}, Event ID: {eventId}");

                // Process the request
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving graph: {ex.Message}");
            }
        }

        async Task<DCRGraph> RetrieveAndParseGraph(string id)
        {
            // Query to get the DCRGraph vertex
            var graphproperties = $"g.V().has('graph', '{id}').hasLabel('DCRGraph')";
            var metadata = (await _gremlinClient.SubmitAsync<dynamic>(graphproperties)).First();

            if (metadata is null)
                throw new Exception($"DCR Graph with ID {id} not found.");
            var metadataJson = JToken.FromObject(metadata);
            // Now, inspect or traverse metadataJson as needed:
            string titleToken = metadataJson["properties"]["title"][0]["value"];
            string gid = (string)metadata["id"];
            var dcrGraph = new DCRGraph(titleToken)
            {
                Id = gid
            };

            // Query to get all events connected to this DCRGraph
            var eventsQuery = $"g.V().has('graph', '{id}').hasLabel('Event')";
            var eventsResult = await _gremlinClient.SubmitAsync<dynamic>(eventsQuery);

            foreach (var eventVertex in eventsResult)
            {
                var eventId = (string)eventVertex["id"];
                var eventJson = JToken.FromObject(eventVertex);
                var props = eventJson["properties"];
                bool executed = props["executed"][0]["value"];
                bool included = props["included"][0]["value"];
                bool pending = props["pending"][0]["value"];

                string roles = props["roles"]?[0]?["value"] ?? "";
                string[] items = roles.Split(',');
                List<string> rolesList = items.Select(item => item.Trim()).ToList();

                string readroles = props["readroles"]?[0]?["value"] ?? "";
                string[] items2 = roles.Split(',');
                List<string> readrolesList = items2.Select(item => item.Trim()).ToList();

                string type = props["type"][0]["value"];
                var eventtype = (EventType)Enum.Parse(typeof(EventType), type, true);

                string? description = props["description"]?[0]?["value"] ?? null;

                string? label = props["label"]?[0]?["value"] ?? null;

                string? data = props["data"]?[0]?["value"] ?? null;

                dcrGraph.Events[eventId] = new Event(eventId)
                {
                    Executed = executed,
                    Included = included,
                    Pending = pending,
                    Roles = rolesList,
                    ReadRoles = readrolesList,
                    Type = eventtype,
                    Description = description,
                    Label = label,
                    Data = data
                };
            }

            // Query to get all relationships between events
            var relationshipsQuery = $"g.V().has('graph', '{id}').hasLabel('Event').outE().as('e').inV().as('v').select('e', 'v')";
            var relationshipsResult = await _gremlinClient.SubmitAsync<dynamic>(relationshipsQuery);

            foreach (var result in relationshipsResult)
            {
                var edge = result["e"];
                var targetEventVertex = result["v"];

                var edgeType = (string)edge["label"];           // Relationship type (Condition, Response, etc.)
                var sourceid = (string)edge["outV"];            // Source event ID
                var targetid = (string)targetEventVertex["id"]; // Target event ID


                if (edgeType == "parentOf")
                {
                    dcrGraph.Events[targetid].Parent = dcrGraph.Events[sourceid];
                    dcrGraph.Events[sourceid].Children.Add(dcrGraph.Events[targetid]);
                }
                else
                {

                    var relationship = new Relationship(
                    source: sourceid,
                    target: targetid,
                    relationshipType: Enum.Parse<RelationshipType>(edgeType, true)
                    );

                    dcrGraph.Relationships.Add(relationship);
                }
            }

            /* //kinda missing, but they should be generated, no?
                     var executedEvents = new HashSet<string>(
                doc.Descendants("executed").Descendants("event")
                   .Select(e => e.Attribute("id")?.Value).Where(id => !string.IsNullOrEmpty(id))!);

            var includedEvents = new HashSet<string>(
                doc.Descendants("included").Descendants("event")
                   .Select(e => e.Attribute("id")?.Value).Where(id => !string.IsNullOrEmpty(id))!);

            var pendingEvents = new HashSet<string>(
                doc.Descendants("pendingResponses").Descendants("event")
                   .Select(e => e.Attribute("id")?.Value).Where(id => !string.IsNullOrEmpty(id))!);
             */

            //DCRGraph graph = DCRInterpreter.ParseDCRGraphFromXml(XDocument.Load("the_ultimate_test.xml"));
            //bool ok= graph.Equals(dcrGraph); test, but not the same object so fails

            return dcrGraph;

        }
        public async Task<bool> CanExecuteEvent(string eventId, string graphId)
        {
            // Gremlin query to check if the event can be executed
            var query = $@"
                g.V().has('graph', '{graphId}')
                .has('id', '{eventId.EscapeGremlinString()}')
                .has('included', true)
                .as('event')
                .not(
                    __.inE('Condition')
                    .outV()
                    .has('executed', false)
                )
                .not(
                    __.inE('Milestone')
                    .outV()
                    .has('executed', false)
                )
                .repeat(
                    __.inE('parentOf').outV() 
                    .hasLabel('Event')
                    .has('included', true)
                    .not(
                        __.inE('Condition')
                            .outV()
                            .has('executed', false)
                    )
                    .not(
                        __.inE('Milestone')
                            .outV()
                            .has('executed', false)
                    )
                )
                .until(__.not(__.inE('parentOf')))
                .count()
                .choose(__.is(gt(0)), constant(true), constant(false))";
            try
            {
                var result = await _gremlinClient.SubmitAsync<bool>(query);
                return result.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing CanExecuteEvent query: {ex.Message}");
                return false;
            }
        }

    }
}

