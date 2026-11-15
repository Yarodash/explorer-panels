using System.Text.Json;
using System.Text.RegularExpressions;
using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

/// <summary>
/// Документация и примеры — это то, по чему агент пишет панель. Они не должны расходиться с программой.
/// </summary>
public class ExamplesTests
{
    static readonly string Repo = FindRepo();

    static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ExplorerPanels.slnx"))) return dir.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    static readonly string[] Skipped = ["_spike", "node_modules", "artifacts", "bin", "obj", ".git", ".superpowers"];

    public static IEnumerable<object[]> PanelRoots()
    {
        var pending = new Stack<string>([Repo]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (File.Exists(PanelLoader.PanelFile(dir))) yield return [Path.GetRelativePath(Repo, dir)];
            foreach (var sub in Directory.GetDirectories(dir))
                if (!Skipped.Contains(Path.GetFileName(sub)) && Path.GetFileName(sub) != PanelLoader.DirName) pending.Push(sub);
        }
    }

    [Theory]
    [MemberData(nameof(PanelRoots))]
    public void Every_example_panel_loads_without_problems(string relativeRoot)
    {
        var result = PanelLoader.LoadFile(Path.GetFullPath(Path.Combine(Repo, relativeRoot)));

        Assert.True(result.Ok);
        Assert.Empty(result.Problems); // примеры — образец: без предупреждений тоже
    }

    [Fact]
    public void There_are_examples_for_the_common_project_kinds()
    {
        var roots = PanelRoots().Select(r => ((string)r[0]).Replace('\\', '/')).ToList();
        foreach (var expected in new[] { "examples/fastapi-react", "examples/node", "examples/docker-compose", "examples/static-site", "examples/git-info" })
            Assert.Contains(expected, roots);
    }

    [Fact]
    public void Schema_widget_types_match_core()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "schema", "panel.schema.json")));
        var types = schema.RootElement.GetProperty("definitions").GetProperty("widgetType").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.Equal(WidgetTypes.All.Order(), types.Order());
    }

    [Fact]
    public void Readme_minimal_example_is_valid()
    {
        var readme = File.ReadAllText(Path.Combine(Repo, "README.md"));
        var first = Regex.Match(readme, "```json\\s*\\n(.*?)```", RegexOptions.Singleline);
        Assert.True(first.Success, "README.md must contain a ```json example");

        using var project = new TempProject();
        var result = PanelLoader.Parse(first.Groups[1].Value, project.Root);

        Assert.True(result.Ok);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Every_json_example_in_the_reference_is_valid()
    {
        var reference = File.ReadAllText(Path.Combine(Repo, "docs", "panel-reference.md"));
        var blocks = Regex.Matches(reference, "```json\\s*\\n(.*?)```", RegexOptions.Singleline);
        Assert.NotEmpty(blocks);

        using var project = new TempProject("backend", "frontend");
        string[] panelKeys = ["$schema", "version", "scope", "services", "actions", "values", "widgets"];
        int checkedExamples = 0;
        foreach (Match block in blocks)
        {
            var json = block.Groups[1].Value;
            // примером panel.json считается блок, у которого на верхнем уровне только ключи panel.json
            // (вывод обработчика с "value" и "ttl" — не panel.json)
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(p => !panelKeys.Contains(p.Name))) continue;

            var result = PanelLoader.Parse(json, project.Root);
            Assert.True(result.Ok && result.Problems.Count == 0, $"example is not valid:\n{json}\n{string.Join("\n", result.Problems)}");
            checkedExamples++;
        }
        Assert.True(checkedExamples >= 4, "the reference should show panel.json examples for services, actions, values and widgets");
    }

    [Fact]
    public void Reference_documents_every_widget_type_and_builtin_action()
    {
        var reference = File.ReadAllText(Path.Combine(Repo, "docs", "panel-reference.md"));
        foreach (var type in WidgetTypes.All) Assert.Contains($"`{type}`", reference);
        foreach (var action in BuiltinActions.All) Assert.Contains($"`{action}`", reference);
    }

    [Fact]
    public void Skill_has_the_trigger_phrases_and_the_two_commands()
    {
        var skill = File.ReadAllText(Path.Combine(Repo, "skill", "explorer-panel", "SKILL.md"));

        Assert.Contains("добавь управление в проводник", skill);
        Assert.Contains("explorer-panel trust", skill);
        Assert.Contains("explorer-panel check", skill);
    }
}
