using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Testing;
using AIThread.Service.Workspace;

namespace AIThread.Tests;

// The endpoints the tester calls come from the route attributes in the code, so a planned path spelled from the class
// name (OutletUnion for OutletUnionApiController) is corrected instead of ending in a 404 the tester guesses around.
public class AspNetRoutesTests
{
    private const string OutletUnion = """
        namespace Controllers.Api.Master.Outbound
        {
            [ApiVersion("1.0")]
            [ApiExplorerSettings(GroupName = "OutboundService")]
            [Route("api/v{version:apiVersion}/Outbound/[controller]")]
            [ApiController]
            public class OutletUnionApiController : ControllerBase
            {
                public OutletUnionApiController(ILogger<OutletUnionApiController> logger) { }

                [HttpPost("GetAll")]
                public async Task<IActionResult> GetAll(ClsPagingModelRequest request) => Ok();

                [HttpGet("{id:int}")]
                public IActionResult Get(int id) => Ok();

                [HttpDelete("/api/outlets/{id}")]
                public IActionResult Delete(int id) => Ok();

                private void Helper() { }
            }
        }
        """;

    [Fact]
    public void Tokens_are_replaced_as_aspnet_does()
    {
        var routes = AspNetRoutes.Parse(OutletUnion, "Controllers/Api/Outbound/OutletUnionApiController.cs");

        Assert.Equal(["POST /api/v1/Outbound/OutletUnionApi/GetAll", "GET /api/v1/Outbound/OutletUnionApi/{id}", "DELETE /api/outlets/{id}"],
            routes.Select(r => r.ToString()));
        var getAll = routes[0];
        Assert.Equal("OutletUnionApiController.GetAll", getAll.Handler);
        Assert.Equal(12, getAll.Line);
        Assert.False(getAll.Conventional);
    }

    [Fact]
    public void Action_names_areas_web_api_2_and_conventional_routes()
    {
        var routes = AspNetRoutes.Parse("""
            [Area("Admin")]
            [Route("[area]/[controller]/[action]")]
            public class UsersController : Controller
            {
                public async Task<IActionResult> SaveAsync() => Ok();
                [ActionName("Hapus")] [HttpPost] public IActionResult Delete() => Ok();
                [NonAction] public void NotAnAction() { }
            }

            [RoutePrefix("api/klaim")]
            public class KlaimController : ApiController
            {
                [HttpPost, Route("save")] public IHttpActionResult Save() => Ok();
                [AcceptVerbs("GET", "POST"), Route("~/legacy/list")] public IHttpActionResult List() => Ok();
            }

            public class HomeController : Controller
            {
                public IActionResult Index() => View();
                [HttpPost] public IActionResult Submit() => View();
            }

            [Route("Systems/{controller}/{action=Index}")]
            public class LovController : Controller
            {
                [HttpPost] public IActionResult GetDataSource() => Ok();
            }
            """, "Controllers.cs");

        Assert.Equal([
            "ANY /Admin/Users/Save", "POST /Admin/Users/Hapus",
            "POST /api/klaim/save", "GET /legacy/list", "POST /legacy/list",
            "ANY /Home/Index", "POST /Home/Submit",
            "POST /Systems/Lov/GetDataSource"
        ], routes.Select(r => r.ToString()));
        Assert.True(routes.Single(r => r.Path == "/Home/Index").Conventional);
    }

    [Fact]
    public void A_controller_takes_the_route_of_its_base_class_in_another_file()
    {
        var root = Directory.CreateTempSubdirectory("ait-routes-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "BaseApiController.cs"), """
                [ApiController]
                [Route("api/[controller]")]
                public abstract class BaseApiController : ControllerBase { }
                """);
            Directory.CreateDirectory(Path.Combine(root, "Controllers"));
            File.WriteAllText(Path.Combine(root, "Controllers", "OrdersController.cs"), """
                public class OrdersController : BaseApiController
                {
                    [HttpGet] public IActionResult List() => Ok();
                    [HttpPost("{id}/approve")] public IActionResult Approve(int id) => Ok();
                }
                """);

            var routes = AspNetRoutes.Scan(new WorkspaceScope(root), CancellationToken.None);

            Assert.Equal(["GET /api/Orders", "POST /api/Orders/{id}/approve"], routes.Select(r => r.ToString()));
            Assert.Equal("Controllers/OrdersController.cs", routes[0].File);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("POST", "/api/v1/Outbound/OutletUnionApi/GetAll", "POST /api/v1/Outbound/OutletUnionApi/GetAll")]
    [InlineData("post", "/API/V1/outbound/outletunionapi/getall", "POST /api/v1/Outbound/OutletUnionApi/GetAll")]
    [InlineData("GET", "/api/v1/Outbound/OutletUnionApi/42", "GET /api/v1/Outbound/OutletUnionApi/{id}")]
    [InlineData("GET", "/api/v1/Outbound/OutletUnion/GetAll", null)]
    [InlineData("GET", "/api/v1/Outbound/OutletUnionApi/GetAll", "GET /api/v1/Outbound/OutletUnionApi/{id}")]
    public void Requests_match_routes_by_method_and_segments(string method, string path, string? expected)
    {
        var routes = AspNetRoutes.Parse(OutletUnion, "OutletUnionApiController.cs");
        Assert.Equal(expected, AspNetRoutes.Matching(method, path, routes).FirstOrDefault()?.ToString());
    }

    [Fact]
    public void A_path_spelled_from_the_class_name_is_corrected_and_an_unclear_one_is_not()
    {
        var routes = AspNetRoutes.Parse(OutletUnion + """
            [Route("api/v1/Outbound/[controller]")]
            public class LobApiController : ControllerBase
            {
                [HttpPost("GetAll")] public IActionResult GetAll() => Ok();
            }
            """, "Controllers.cs");

        Assert.Equal("/api/v1/Outbound/OutletUnionApi/GetAll", AspNetRoutes.Correction("POST", "/api/v1/Outbound/OutletUnion/GetAll", routes)?.Path);
        Assert.Equal("/api/v1/Outbound/OutletUnionApi/7", AspNetRoutes.Correction("GET", "/api/v1/Outbound/OutletUnion/7", routes)?.Path);
        // Served as written, or too far from any route to be sure.
        Assert.Null(AspNetRoutes.Correction("POST", "/api/v1/Outbound/OutletUnionApi/GetAll", routes));
        Assert.Null(AspNetRoutes.Correction("POST", "/api/v1/Inbound/Customer/GetAll", routes));
        // A segment missing: no correction, but the route still comes first among the closest.
        Assert.Equal("POST /api/v1/Outbound/OutletUnionApi/GetAll", AspNetRoutes.Closest("POST", "/api/v1/OutletUnion/GetAll", routes).First().ToString());
    }

    [Theory]
    [InlineData("POST /api/users?x=1", "POST", "/api/users")]
    [InlineData("/User/Index", "GET", "/User/Index")]
    [InlineData("http://localhost:5211/api/a", "GET", "/api/a")]
    [InlineData("the users page", null, null)]
    public void Targets_split_into_method_and_path(string target, string? method, string? path)
    {
        var split = AspNetRoutes.SplitTarget(target);
        Assert.Equal(method, split?.Method);
        Assert.Equal(path, split?.Path);
    }

    [Fact]
    public void The_tester_brief_lists_the_endpoints_and_corrects_the_planned_target()
    {
        var routes = AspNetRoutes.Parse(OutletUnion, "Master-Data/Controllers/Api/Outbound/OutletUnionApiController.cs");
        var plan = new ThreadPlan
        {
            Summary = "Reroute OutletUnion GetAll",
            Tasks = [new PlanTask { Id = "T1", Title = "Service", File = "Master-Data/Services/OutletUnionService.cs", Instruction = "…" }],
            Test = new TestPlan
            {
                Level = TestPlan.Api,
                BaseUrl = "http://localhost:5211",
                Scenarios =
                [
                    new TestScenario
                    {
                        Title = "GetAll paging",
                        Target = "POST /api/v1/Outbound/OutletUnion/GetAll",
                        Steps = ["POST /api/v1/Outbound/OutletUnion/GetAll with body {\"page\":1,\"size\":10}"],
                        Expect = "200 with Items"
                    }
                ]
            }
        };
        var input = new TestingInput(new AiNode { Name = "Tester", Model = "m" }, new ThreadRequest(ThreadSource.ClaudeCode, "konfirmasi", "konfirmasi", null, "C:/p", Guid.NewGuid(), "chat-1"), plan,
            new ExecutionSummary([], 0, 0, 0), new ProjectContext(Guid.NewGuid(), "Master-Data", "C:/p", [".NET"], []));

        var brief = TesterAgent.Brief(input, routes: routes);

        Assert.Contains("# Endpoints in the code", brief);
        Assert.Contains("- POST /api/v1/Outbound/OutletUnionApi/GetAll — OutletUnionApiController.GetAll (Master-Data/Controllers/Api/Outbound/OutletUnionApiController.cs:12)", brief);
        Assert.Contains("Start at: POST /api/v1/Outbound/OutletUnionApi/GetAll (the plan wrote /api/v1/Outbound/OutletUnion/GetAll, which no route in the code serves)", brief);
        Assert.Contains("1. POST /api/v1/Outbound/OutletUnionApi/GetAll with body", brief);
        Assert.DoesNotContain("# Endpoints in the code", TesterAgent.Brief(input));
    }

    [Fact]
    public void After_a_404_the_tester_reads_the_closest_routes_or_where_to_find_them()
    {
        var routes = AspNetRoutes.Parse(OutletUnion, "OutletUnionApiController.cs");

        var closest = TesterAgent.ResponseHint("POST", "/api/v1/Outbound/OutletUnion/GetAll", 404, "", routes)!;
        Assert.Contains("no route in the code serves POST /api/v1/Outbound/OutletUnion/GetAll", closest);
        Assert.Contains("- POST /api/v1/Outbound/OutletUnionApi/GetAll — OutletUnionApiController.GetAll", closest);

        Assert.Contains("has probably not loaded the change", TesterAgent.ResponseHint("POST", "/api/v1/Outbound/OutletUnionApi/GetAll", 404, "", routes));
        Assert.Contains("open /swagger/index.html", TesterAgent.ResponseHint("GET", "/nothing/like/it/at/all", 404, "", []));
        Assert.Null(TesterAgent.ResponseHint("GET", "/api/v1/Outbound/OutletUnionApi/1", 200, "{}", routes));
    }

    [Fact]
    public void A_swagger_ui_page_names_its_openapi_documents()
    {
        const string page = """
            <title>Swagger UI</title>
            <script>
                var configObject = JSON.parse('{"urls":[{"url":"/swagger/IDM Service/swagger.json","name":"IDM"},{"url":"/swagger/OutboundService/swagger.json","name":"Outbound"}],"deepLinking":false}');
            </script>
            """;

        Assert.Equal(["/swagger/IDM Service/swagger.json", "/swagger/OutboundService/swagger.json"], SwaggerDocuments.From(page));
        Assert.Equal(["/swagger/v1/swagger.json"], SwaggerDocuments.From("SwaggerUIBundle({ url: \"/swagger/v1/swagger.json\", dom_id: '#swagger-ui' })"));
        Assert.Empty(SwaggerDocuments.From("{\"url\":\"/data.json\"}"));
        Assert.Contains("/swagger/OutboundService/swagger.json", TesterAgent.ResponseHint("GET", "/swagger/index.html", 200, page, []));
    }
}
