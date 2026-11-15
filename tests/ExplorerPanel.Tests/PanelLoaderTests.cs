using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

public class PanelLoaderTests : IDisposable
{
    readonly TempProject _project = new("backend");
    public void Dispose() => _project.Dispose();

    LoadResult Parse(string json) => PanelLoader.Parse(json, _project.Root);

    static Problem SingleError(LoadResult result) => Assert.Single(result.Problems, p => p.IsError);
    static Problem SingleWarning(LoadResult result) => Assert.Single(result.Problems, p => !p.IsError);

    [Fact]
    public void Minimal_services_only_builds_default_widgets()
    {
        var result = Parse("""{ "services": { "api": { "run": "x" } } }""");

        Assert.True(result.Ok);
        Assert.Empty(result.Problems);
        var widgets = result.Config!.Widgets.Select(w => (Type: (string?)w!["type"], Do: (string?)w["do"])).ToList();
        Assert.Equal(new (string?, string?)[] { ("status", null), ("button", "start"), ("button", "stop"), ("button", "logs") }, widgets);
        Assert.Equal("api", (string?)result.Config.Widgets[0]!["service"]);
    }

    [Fact]
    public void Default_widgets_include_a_button_per_action()
    {
        var result = Parse("""{ "actions": { "site": { "open": "http://localhost:5173" } } }""");

        Assert.True(result.Ok);
        var button = Assert.Single(result.Config!.Widgets);
        Assert.Equal("site", (string?)button!["do"]);
    }

    [Fact]
    public void Syntax_error_reports_line_and_column()
    {
        var result = Parse("{\n  \"services\": {\n    \"api\": {");

        Assert.False(result.Ok);
        Assert.Null(result.Config);
        var error = SingleError(result);
        Assert.Equal(3, error.Line);
        Assert.Equal("panel.json", error.File);
    }

    [Fact]
    public void Unknown_icon_suggests_nearest()
    {
        var result = Parse("""
            {
              "widgets": [
                { "type": "separator" },
                { "type": "button", "icon": "playy", "do": "start" }
              ]
            }
            """);

        var error = SingleError(result);
        Assert.Contains("unknown icon \"playy\"", error.Message);
        Assert.Contains("did you mean \"play\"", error.Message);
        Assert.Equal(4, error.Line);
    }

    [Fact]
    public void Unknown_widget_type_is_error()
    {
        var error = SingleError(Parse("""{ "widgets": [ { "type": "buton" } ] }"""));
        Assert.Contains("unknown widget type \"buton\"", error.Message);
        Assert.Contains("did you mean \"button\"", error.Message);
    }

    [Fact]
    public void Do_must_reference_action_or_builtin()
    {
        var error = SingleError(Parse("""
            { "actions": { "site": { "open": "http://x" } },
              "widgets": [ { "type": "button", "icon": "play", "do": "sit" } ] }
            """));
        Assert.Contains("unknown action \"sit\"", error.Message);
        Assert.Contains("did you mean \"site\"", error.Message);
    }

    [Fact]
    public void Builtin_action_for_one_service_must_name_existing_service()
    {
        var error = SingleError(Parse("""
            { "services": { "api": { "run": "x" } },
              "widgets": [ { "type": "button", "icon": "play", "do": "start:apii" } ] }
            """));
        Assert.Contains("unknown service \"apii\"", error.Message);
    }

    [Fact]
    public void Status_service_must_exist()
    {
        var error = SingleError(Parse("""{ "widgets": [ { "type": "status", "service": "api" } ] }"""));
        Assert.Contains("unknown service \"api\"", error.Message);
    }

    [Fact]
    public void Action_needs_exactly_one_of_run_or_open()
    {
        var both = SingleError(Parse("""{ "actions": { "a": { "run": "x", "open": "http://x" } } }"""));
        Assert.Contains("exactly one of \"run\" or \"open\"", both.Message);

        var none = SingleError(Parse("""{ "actions": { "a": { } } }"""));
        Assert.Contains("exactly one of \"run\" or \"open\"", none.Message);
    }

    [Fact]
    public void Needs_must_reference_services()
    {
        var error = SingleError(Parse("""{ "actions": { "site": { "open": "http://x", "needs": ["web"] } } }"""));
        Assert.Contains("unknown service \"web\"", error.Message);
    }

    [Fact]
    public void Scope_must_be_tree_or_folder()
    {
        var error = SingleError(Parse("""{ "scope": "all" }"""));
        Assert.Contains("\"tree\" or \"folder\"", error.Message);

        Assert.Equal("tree", Parse("{}").Config!.Scope);
        Assert.Equal("folder", Parse("""{ "scope": "folder" }""").Config!.Scope);
    }

    [Fact]
    public void Every_must_be_positive()
    {
        var error = SingleError(Parse("""{ "values": { "branch": { "run": "git branch", "every": 0 } } }"""));
        Assert.Contains("\"every\" must be a number greater than 0", error.Message);
    }

    [Fact]
    public void Missing_cwd_folder_is_error()
    {
        var error = SingleError(Parse("""{ "services": { "api": { "run": "x", "cwd": "bakend" } } }"""));
        Assert.Contains("\"bakend\" does not exist", error.Message);

        Assert.True(Parse("""{ "services": { "api": { "run": "x", "cwd": "backend" } } }""").Ok);
    }

    [Fact]
    public void Action_name_must_not_shadow_builtin()
    {
        var error = SingleError(Parse("""{ "actions": { "stop": { "run": "x" } } }"""));
        Assert.Contains("\"stop\" is a built-in action", error.Message);
    }

    [Fact]
    public void Service_requires_run()
    {
        var error = SingleError(Parse("""{ "services": { "api": { "start": "x" } } }"""));
        Assert.Contains("\"run\" is required", error.Message);
    }

    [Fact]
    public void Unknown_key_is_warning_with_suggestion()
    {
        var result = Parse("""{ "services": { "api": { "run": "x", "helth": "http://localhost:1" } } }""");

        Assert.True(result.Ok);
        var warning = SingleWarning(result);
        Assert.Contains("unknown key \"helth\"", warning.Message);
        Assert.Contains("did you mean \"health\"", warning.Message);
    }

    [Fact]
    public void Undeclared_placeholder_is_warning()
    {
        var result = Parse("""
            { "values": { "branch": { "run": "git branch --show-current" } },
              "widgets": [ { "type": "text", "text": "{brnach}" } ] }
            """);

        Assert.True(result.Ok);
        var warning = SingleWarning(result);
        Assert.Contains("\"brnach\" is not declared", warning.Message);
        Assert.Contains("did you mean \"branch\"", warning.Message);
    }

    [Fact]
    public void Launcher_without_health_is_warning()
    {
        var result = Parse("""{ "services": { "db": { "run": "docker compose up -d", "stop": "docker compose down" } } }""");

        Assert.True(result.Ok);
        Assert.Contains("\"stop\" but no \"health\"", SingleWarning(result).Message);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_accepted()
    {
        var result = Parse("""
            {
              // серверы проекта
              "services": { "api": { "run": "x", }, },
            }
            """);
        Assert.True(result.Ok);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Full_example_parses_into_typed_config()
    {
        var result = Parse("""
            {
              "version": 1,
              "services": {
                "api": { "run": "python -m uvicorn main:app", "cwd": "backend", "health": "http://localhost:8000/api/hello", "env": { "DEBUG": "1" } },
                "db": { "run": "docker compose up -d", "stop": "docker compose down", "health": { "run": "docker compose ps -q" } }
              },
              "actions": { "site": { "open": "http://localhost:5173", "needs": ["api"] }, "tests": { "run": "pytest -q", "cwd": "backend" } },
              "values": { "branch": { "run": "git branch --show-current", "every": 15 }, "env": { "default": "dev" } },
              "widgets": [
                { "type": "text", "icon": "git-branch", "text": "{branch}" },
                { "type": "status", "service": "api" },
                { "type": "select", "value": "env", "options": ["dev", "prod"] },
                { "type": "button", "icon": "globe", "tooltip": "Open", "do": "site", "pin": true },
                { "type": "menu", "icon": "ellipsis", "items": [ { "label": "Tests", "do": "tests" }, { "label": "Restart api", "do": "restart:api" } ] }
              ]
            }
            """);

        Assert.Empty(result.Problems);
        var config = result.Config!;
        var api = config.Services.Single(s => s.Name == "api");
        Assert.Equal("backend", api.Cwd);
        Assert.Equal("http://localhost:8000/api/hello", api.Health!.Url);
        Assert.Equal("1", api.Env["DEBUG"]);
        var db = config.Services.Single(s => s.Name == "db");
        Assert.Equal("docker compose down", db.Stop);
        Assert.Equal("docker compose ps -q", db.Health!.Run);
        Assert.Equal(new[] { "api" }, config.Actions.Single(a => a.Name == "site").Needs);
        Assert.Equal(15, config.Values.Single(v => v.Name == "branch").Every);
        Assert.Equal("dev", (string?)config.Values.Single(v => v.Name == "env").Default);
        Assert.Equal(5, config.Widgets.Count);
    }

    [Fact]
    public void ValidWidgets_keeps_good_and_drops_bad()
    {
        var config = Parse("""{ "actions": { "site": { "open": "http://x" } } }""").Config!;
        var widgets = (System.Text.Json.Nodes.JsonArray)System.Text.Json.Nodes.JsonNode.Parse("""
            [ { "type": "text", "text": "ok" }, { "type": "hologram" }, { "type": "button", "icon": "globe", "do": "site" },
              { "type": "button", "icon": "globe", "do": "missing" } ]
            """)!;

        var valid = PanelLoader.ValidWidgets(widgets, config, _project.Root);

        Assert.Equal(new[] { "text", "button" }, valid.Select(w => (string?)w!["type"]));
        Assert.Equal("site", (string?)valid[1]!["do"]);
    }

    [Fact]
    public void LoadFile_reads_panel_json_from_project()
    {
        _project.WritePanel("""{ "services": { "api": { "run": "x" } } }""");
        Assert.True(PanelLoader.LoadFile(_project.Root).Ok);
    }

    [Fact]
    public void LoadFile_without_file_reports_error()
    {
        var result = PanelLoader.LoadFile(_project.Root);
        Assert.False(result.Ok);
        Assert.Contains("not found", SingleError(result).Message);
    }
}
