# Explorer Panels — план реализации

> **Исторический документ.** Написан до реализации и после неё не обновлялся: часть решений изменилась (папку нельзя разрешить через местный интерфейс; нажатие на панели не выводит Проводник вперёд; свои виды `panel.tsx` и ввод с клавиатуры не делались). Как программа устроена сейчас — [docs/how-it-works.md](../../how-it-works.md) и [docs/panel-reference.md](../../panel-reference.md).


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Программа, которая всегда работает в системе и показывает в настоящем Проводнике Windows 11 панель управления проектом, описанную одним файлом `.explorer_components/panel.json`, — так, чтобы панель мог добавить Claude по одной ссылке на репозиторий.

**Architecture:** Три сборки на общем ядре: `ExplorerPanel.Core` (разбор и проверка `panel.json`, подстановки, доверие, правила статусов, клиент местного интерфейса), `explorer-panel-host.exe` (фоновая программа: слежение за Проводником, окна панелей на WebView2, серверы, значения, местный HTTP-интерфейс, трей) и `explorer-panel.exe` (консольная команда для агентов). Вид панели — страница React + shadcn/ui, собранная один раз и отдаваемая хозяином.

**Tech Stack:** C# / .NET 10 (WinForms, без WPF), Microsoft.Web.WebView2, xUnit; React 19 + TypeScript + Vite + Tailwind 4 + shadcn/ui (radix, nova), lucide-react, vitest; PowerShell для установки и сквозных проверок.

**Spec:** `docs/superpowers/specs/2026-11-15-explorer-panels-design.md`. Прототип с проверенными приёмами: `_spike/GlueSpike` (`Program.cs`, `WebOverlay.cs`, `Services.cs`), `_spike/panel-ui`, `_spike/WinShot`.

## Global Constraints

- Windows 11, Проводник со вкладками; .NET 10; WebView2 Evergreen.
- Подпапка проекта: `.explorer_components`; файл: `panel.json`; команда запуска везде называется `run`.
- Имена: продукт Explorer Panels, команда `explorer-panel`, фоновая программа `explorer-panel-host.exe`.
- Данные программы: `%LOCALAPPDATA%\ExplorerPanels\` (переопределяется переменной `EXPLORER_PANEL_DATA` — для тестов). Установка: `%LOCALAPPDATA%\Programs\ExplorerPanels\`.
- Команды выполняются через `cmd.exe /c`, рабочая папка по умолчанию — корень проекта.
- Подстановка `{имя}` в `run` разрешена только для значений из символов `A-Z a-z 0-9 . _ - / : @ +`.
- Статусы: `stopped`, `starting`, `running`, `failed`. Чужой процесс на порту — `failed` с пояснением; панель его никогда не завершает.
- Пока папка не разрешена, команды не выполняются.
- Местный интерфейс: только `127.0.0.1`, порт по умолчанию 47631, обязателен заголовок `X-Explorer-Panel: 1`.
- Окно панели: владелец — окно Проводника; стили `WS_POPUP | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP`, без `WS_EX_APPWINDOW`; в стопке окон сразу над владельцем; привязка к `SHELLDLL_DefView`.
- Сообщения команды `explorer-panel` и документация репозитория — на английском; встроенные подписи панели и трея — на языке системы (en, ru).
- Проверки окон — только на рабочем столе «Claude tests»; живой экран не снимается; клавиши не имитируются.
- Комментарии в коде — на русском.

## Review Focus

1. **Недописанный `panel.json`** (редактор или агент сохранил половину файла): панель не падает, остаётся последняя рабочая версия, проблема видна в `check`; после исправления панель обновляется. → тест в задаче 7.
2. **Пробелы и кириллица в пути проекта и в `cwd`**: команды, доверие и поиск корня работают. → тесты в задачах 2, 3, 7 используют папку `проект с пробелами`.
3. **Одна папка в двух окнах или вкладках, быстрые повторные нажатия «запустить»**: сервер запускается один раз, состояние общее. → тест в задаче 7.
4. **Сервер, который пишет очень много вывода**: лог обрезается, память не растёт. → тест в задаче 7.
5. **Проводник перезапущен или ещё не запущен**: хозяин не падает и находит окна позже. → сценарий в задаче 11.

---

### Task 1: Каркас репозитория

**Files:**
- Create: `.gitignore`, `ExplorerPanels.slnx`, `src/core/ExplorerPanel.Core.csproj`, `src/host/ExplorerPanel.Host.csproj`, `src/host/app.manifest`, `src/cli/ExplorerPanel.Cli.csproj`, `tests/ExplorerPanel.Tests/ExplorerPanel.Tests.csproj`, `tests/ExplorerPanel.Tests/SmokeTests.cs`
- Move: `backend/`, `frontend/`, `.explorer_components/` → `examples/fastapi-react/`; `_spike/panel-ui/` → `src/ui/`; `_spike/WinShot/` → `tools/winshot/`

**Interfaces:**
- Produces: решение из четырёх проектов. `Core` — `net10.0`; `Host` — `WinExe`, `net10.0-windows10.0.19041.0`, `UseWindowsForms`, `AssemblyName=explorer-panel-host`, пакет `Microsoft.Web.WebView2`; `Cli` — `Exe`, `net10.0`, `AssemblyName=explorer-panel`; тесты — `net10.0-windows10.0.19041.0`, ссылаются на все три.

- [ ] **Step 1:** `git init -b main`; `.gitignore` исключает `bin/`, `obj/`, `node_modules/`, `dist/`, `_spike/`, `**/.explorer_components/logs/`, `*.user`.
- [ ] **Step 2:** Перенести папки; в `examples/fastapi-react/.explorer_components/panel.json` заменить `start` на `run` и добавить `actions.site`.
- [ ] **Step 3:** Создать проекты и решение; `SmokeTests.Core_assembly_loads` проверяет, что `typeof(ExplorerPanel.Core.AppInfo).Assembly` загружается, а `AppInfo.Version` не пуст.
- [ ] **Step 4:** Run: `dotnet build ExplorerPanels.slnx` и `dotnet test ExplorerPanels.slnx`. Expected: сборка без ошибок, 1 тест пройден.
- [ ] **Step 5:** Commit: `chore: repository skeleton`.

### Task 2: Ядро — разбор и проверка `panel.json`

**Files:**
- Create: `src/core/PanelConfig.cs`, `src/core/PanelLoader.cs`, `src/core/JsonPositions.cs`, `src/core/Suggest.cs`, `src/core/WidgetTypes.cs`, `src/core/LucideIcons.cs`, `src/core/Resources/lucide-icons.txt` (встроенный ресурс), `src/ui/scripts/export-icons.mjs`
- Test: `tests/ExplorerPanel.Tests/PanelLoaderTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record Problem(string File, int Line, int Column, bool IsError, string Message);
  public sealed record HealthDef(string? Url, string? Run);
  public sealed record ServiceDef(string Name, string Run, string? Cwd, IReadOnlyDictionary<string,string> Env, HealthDef? Health, string? Stop);
  public sealed record ActionDef(string Name, string? Run, string? Open, string? Cwd, IReadOnlyDictionary<string,string> Env, IReadOnlyList<string> Needs);
  public sealed record ValueDef(string Name, string? Run, double Every, string? Cwd, JsonNode? Default);
  public sealed record PanelConfig(string Scope, IReadOnlyList<ServiceDef> Services, IReadOnlyList<ActionDef> Actions,
      IReadOnlyList<ValueDef> Values, JsonArray Widgets);   // Widgets: проверенные; если в файле их нет — построенные по умолчанию
  public sealed record LoadResult(PanelConfig? Config, IReadOnlyList<Problem> Problems) { public bool Ok { get; } }
  public static class PanelLoader {
      public const string DirName = ".explorer_components", FileName = "panel.json";
      public static LoadResult LoadFile(string projectRoot);
      public static LoadResult Parse(string json, string projectRoot);
  }
  public static class WidgetTypes { public static readonly IReadOnlyList<string> All; }  // 12 типов из спецификации
  public static class BuiltinActions { public static bool IsBuiltin(string name); }      // start, stop, restart, logs, start:x, stop:x, restart:x
  ```
- Сообщения — на английском, вида `unknown icon "playy" (did you mean "play"?)`.

- [ ] **Step 1: Тесты** (каждый — отдельный `[Fact]`, корень проекта — временная папка `проект с пробелами`):
  - `Minimal_services_only_builds_default_widgets`: `{"services":{"api":{"run":"x"}}}` → `Ok`; типы виджетов по порядку: `status`, `button`(`do=start`), `button`(`do=stop`), `button`(`do=logs`).
  - `Syntax_error_reports_line_and_column`: обрыв на 3-й строке → одна ошибка, `Line == 3`.
  - `Unknown_icon_suggests_nearest`: `"icon":"playy"` → ошибка содержит `did you mean "play"`; `Line` указывает на строку с иконкой.
  - `Unknown_widget_type_is_error`, `Do_must_reference_action_or_builtin`, `Status_service_must_exist`, `Action_needs_exactly_one_of_run_or_open`, `Needs_must_reference_services`, `Scope_must_be_tree_or_folder`, `Every_must_be_positive`, `Missing_cwd_folder_is_error`, `Action_name_must_not_shadow_builtin`.
  - `Unknown_key_is_warning_with_suggestion`: `"helth"` в сервисе → предупреждение (`IsError == false`) с `did you mean "health"`; `Ok == true`.
  - `Undeclared_placeholder_is_warning`: `"text":"{brnach}"` → предупреждение.
  - `Launcher_without_health_is_warning`: сервис со `stop`, но без `health` → предупреждение.
- [ ] **Step 2:** Run: `dotnet test --filter PanelLoaderTests`. Expected: FAIL (типов нет).
- [ ] **Step 3:** `export-icons.mjs` пишет имена иконок lucide (kebab-case, по одному в строке) в `src/core/Resources/lucide-icons.txt`; запустить `node src/ui/scripts/export-icons.mjs`.
- [ ] **Step 4:** Реализовать. `JsonPositions.Map(byte[] utf8)` обходит документ через `Utf8JsonReader` и возвращает словарь «путь → (строка, столбец)»; проверка идёт по `JsonNode`, проблемы привязываются к пути. `Suggest.Nearest(string word, IEnumerable<string> candidates)` — расстояние Левенштейна, не дальше 2.
- [ ] **Step 5:** Run: `dotnet test --filter PanelLoaderTests`. Expected: PASS.
- [ ] **Step 6:** Commit: `feat(core): panel.json loader and validator`.

### Task 3: Ядро — подстановки, поиск корня, доверие, пути

**Files:**
- Create: `src/core/Template.cs`, `src/core/ProjectLocator.cs`, `src/core/TrustStore.cs`, `src/core/AppPaths.cs`, `src/core/AppInfo.cs`
- Test: `tests/ExplorerPanel.Tests/TemplateTests.cs`, `ProjectLocatorTests.cs`, `TrustStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class Template {
      public static IReadOnlyList<string> Names(string text);                       // имена из {…}
      public static string Fill(string text, Func<string, string?> get);           // для text и tooltip; неизвестное имя → пусто
      public static string FillUrl(string text, Func<string, string?> get);        // значение кодируется Uri.EscapeDataString
      public static bool TryFillCommand(string command, Func<string, string?> get, out string result, out string error);
  }
  public static class ProjectLocator {
      public static string? FindRoot(string folder);                               // ближайший предок с .explorer_components/panel.json
      public static bool Applies(string root, string folder, string scope);        // folder: только сам корень; tree: корень и потомки
      public static string Normalize(string path);                                 // полный путь без завершающего разделителя
  }
  public sealed class TrustStore {
      public TrustStore(string file);
      public bool? Get(string root);                    // true — разрешена, false — запрещена, null — не решено
      public void Set(string root, bool? state);
      public IReadOnlyList<string> Trusted { get; }  public IReadOnlyList<string> Blocked { get; }
  }
  public static class AppPaths { public static string DataDir, TrustFile, HostInfoFile, HostLog; public static string StateFile(string root); }
  ```

- [ ] **Step 1: Тесты:** `Fill_replaces_known_and_blanks_unknown`; `TryFillCommand_rejects_unsafe_value` (`"a & del"` → `false`, `error` называет имя значения); `TryFillCommand_accepts_safe_value` (`"feature/x-1"`); `FillUrl_encodes` (`"a b&c"` → `a%20b%26c`); `FindRoot_walks_up` (из `root\src\deep`); `FindRoot_returns_null_without_panel`; `Applies_folder_scope_only_root`; `Trust_roundtrip_survives_reload`; `Trust_is_case_insensitive_and_ignores_trailing_slash`; `Trust_set_null_forgets`.
- [ ] **Step 2:** Run: `dotnet test --filter "TemplateTests|ProjectLocatorTests|TrustStoreTests"`. Expected: FAIL.
- [ ] **Step 3:** Реализовать. `AppPaths.DataDir` берётся из `EXPLORER_PANEL_DATA`, иначе `%LOCALAPPDATA%\ExplorerPanels`; `StateFile(root)` — `state\<первые 16 знаков SHA-256 нормализованного пути в нижнем регистре>.json`.
- [ ] **Step 4:** Run те же тесты. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(core): templates, project locator, trust store`.

### Task 4: Ядро — правила статусов, вывод обработчика, клиент местного интерфейса

**Files:**
- Create: `src/core/ServiceStatusRules.cs`, `src/core/HandlerOutput.cs`, `src/core/Api/Contracts.cs`, `src/core/Api/HostClient.cs`
- Test: `tests/ExplorerPanel.Tests/ServiceStatusRulesTests.cs`, `HandlerOutputTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum ServiceStatus { Stopped, Starting, Running, Failed }
  public enum PortOwner { None, Project, Foreign }
  public sealed record ServiceObservation(bool HasHealth, bool Healthy, bool OwnProcessAlive, int? ExitCode, double SecondsSinceExit,
      bool WantRunning, PortOwner PortOwner, string? ForeignName, int? ForeignPid);
  public static class ServiceStatusRules {
      public const double LauncherGraceSeconds = 60;
      public static (ServiceStatus Status, string? Detail) Compute(ServiceObservation o);
  }
  public sealed record HandlerResult(JsonNode? Value, double? Ttl, JsonArray? Widgets);
  public static class HandlerOutput { public static HandlerResult Parse(string stdout); }
  // Contracts: StatusResponse { Root, Trusted, Loaded, Problems, Services, Values }, SetRequest, DoRequest, TrustRequest, PingResponse
  public sealed class HostClient { public static HostClient? TryConnect(); Task<PingResponse>, Task<StatusResponse> Status(folder), Set, Do, Trust, Reload }
  ```
- Правила `Compute` по порядку: чужой процесс на порту → `Failed` с `port in use by another program: <имя> (<pid>)`; отвечает → `Running`; свой процесс жив → `Starting` (без `health` → `Running`); `ExitCode == 0`, есть `health`, `WantRunning`, прошло меньше `LauncherGraceSeconds` → `Starting`; `ExitCode` не ноль, либо процесс завершился при `WantRunning` → `Failed`; иначе `Stopped`.

- [ ] **Step 1: Тесты:** по одному на каждое правило (`Foreign_port_owner_is_failed_with_detail`, `Healthy_is_running`, `Alive_without_response_is_starting`, `Alive_without_health_is_running`, `Launcher_exit_zero_is_starting_within_grace`, `Launcher_exit_zero_fails_after_grace`, `Nonzero_exit_is_failed`, `Died_while_wanted_is_failed`, `Stopped_by_user_is_stopped`). Для обработчика: `Plain_text_is_value` (`"main\r\n"` → `"main"`), `Json_scalar_is_value`, `Envelope_with_value_ttl_widgets`, `Object_without_envelope_keys_is_value`, `Empty_output_is_null`.
- [ ] **Step 2:** Run: `dotnet test --filter "ServiceStatusRulesTests|HandlerOutputTests"`. Expected: FAIL.
- [ ] **Step 3:** Реализовать. `HostClient` читает порт из `AppPaths.HostInfoFile`, шлёт JSON с заголовком `X-Explorer-Panel: 1`, время ожидания 5 с.
- [ ] **Step 4:** Run те же тесты. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(core): status rules, handler output, host client`.

### Task 5: Страница панели — стандартные виджеты

**Files:**
- Create: `src/ui/src/bridge.ts`, `src/ui/src/Panel.tsx`, `src/ui/src/TrustPrompt.tsx`, `src/ui/src/fit.ts`, `src/ui/src/icons.tsx`, `src/ui/src/i18n.ts`, `src/ui/src/widgets/*.tsx` (по файлу на тип) и `src/ui/src/widgets/index.tsx`, `src/ui/vitest.config.ts`
- Modify: `src/ui/src/App.tsx`, `src/ui/src/main.tsx`, `src/ui/src/index.css`, `src/ui/package.json`
- Test: `src/ui/src/fit.test.ts`, `src/ui/src/widgets/widgets.test.tsx`

**Interfaces:**
- Consumes: виджеты в том виде, в каком их проверило ядро (Task 2).
- Produces (обмен со хозяином, используется в Task 6–7):
  ```ts
  type Status = "stopped" | "starting" | "running" | "failed"
  type PanelState = {
    root: string; locale: "en" | "ru"; trusted: boolean | null; collapsed: boolean
    widgets: Widget[]
    services: Record<string, { status: Status; detail?: string }>
    values: Record<string, unknown>
    actions: Record<string, "running" | "ok" | "failed">
  }
  // хозяин → страница
  type HostMessage = { type: "state"; state: PanelState }
  // страница → хозяин
  type PageMessage =
    | { type: "ready" } | { type: "region"; dpr: number; rects: number[][] } | { type: "fit"; width: number; dpr: number }
    | { type: "do"; action: string; params?: Record<string, string> } | { type: "set"; name: string; value: unknown }
    | { type: "trust"; allow: boolean } | { type: "collapse"; collapsed: boolean }
  export function chooseVisible(widths: number[], pinned: boolean[], available: number, fitButtonWidth: number): boolean[]
  ```
- `chooseVisible` прячет виджеты слева направо, пока сумма ширин не поместится; закреплённые (`pin`) и статусы прячутся последними; если что-то спрятано, учитывается ширина кнопки «расширить».
- В свёрнутом виде показываются статусы и виджеты с `"pin": true`.

- [ ] **Step 1: Тесты:** `fit.test.ts` — `all_fit_shows_all`, `hides_leftmost_first`, `pinned_hidden_last`, `accounts_for_fit_button`. `widgets.test.tsx` (vitest + @testing-library/react, мост подменён) — `status_shows_running_dot_and_detail_tooltip`, `button_click_sends_do`, `text_fills_placeholders`, `toggle_sends_set_and_action`, `select_sends_set`, `untrusted_shows_prompt_and_sends_trust`, `unknown_widget_type_renders_nothing`.
- [ ] **Step 2:** Run: `npm test` в `src/ui`. Expected: FAIL.
- [ ] **Step 3:** Реализовать виджеты на компонентах shadcn (перенести вид и стили из прототипа: курсор-рука, подсветка, `region.ts` без изменений). Иконка по имени — через карту `icons` из `lucide-react`.
- [ ] **Step 4:** Run: `npm test` и `npm run build`. Expected: PASS, сборка в `src/ui/dist`.
- [ ] **Step 5:** Commit: `feat(ui): standard widgets driven by panel state`.

### Task 6: Хозяин — слежение за Проводником и окно панели

**Files:**
- Create: `src/host/Program.cs`, `src/host/HostContext.cs`, `src/host/Log.cs`, `src/host/Native.cs`, `src/host/ExplorerProbe.cs`, `src/host/ExplorerTracker.cs`, `src/host/PanelWindow.cs`
- Источник приёмов: `_spike/GlueSpike/Program.cs` (обход окон, активная вкладка, события), `_spike/GlueSpike/WebOverlay.cs` (окно, область, расширение Проводника, порядок окон).

**Interfaces:**
- Produces:
  ```csharp
  sealed record ExplorerView(IntPtr Window, string? Folder, bool Minimized);
  sealed class ExplorerTracker : IDisposable { event Action<IReadOnlyList<ExplorerView>> Changed; event Action<IntPtr> Moved; void Start(); }
  sealed class PanelWindow : IDisposable {
      PanelWindow(IntPtr explorer, string uiFolder);
      event Action<JsonElement> Message;            // сообщения страницы, кроме region и fit — их окно обрабатывает само
      void Post(string json);  void Reposition();  void SetVisible(bool visible);  bool Alive { get; }
  }
  ```
- Папка страницы: переменная `EXPLORER_PANEL_UI`, иначе `<папка программы>\ui`.

- [ ] **Step 1:** Перенести код прототипа без WPF: цикл сообщений WinForms (`ApplicationContext`), таймеры `System.Windows.Forms.Timer`. Обход окон — по событиям плюс раз в секунду.
- [ ] **Step 2:** `HostContext` на этом шаге показывает панель в любой папке с валидным `panel.json` и шлёт странице состояние с виджетами и статусами `stopped`.
- [ ] **Step 3:** Run: `dotnet build src/host` и запуск с `EXPLORER_PANEL_UI=src/ui/dist` на рабочем столе «Claude tests»; снимок через `tools/winshot`. Expected: панель примера `examples/fastapi-react` видна в правом нижнем углу области файлов, вокруг прозрачно.
- [ ] **Step 4:** Commit: `feat(host): explorer tracking and panel window`.

### Task 7: Хозяин — проекты, серверы, действия, значения

**Files:**
- Create: `src/host/Project.cs`, `src/host/ProjectRegistry.cs`, `src/host/ServiceRunner.cs`, `src/host/ProcessInfo.cs`, `src/host/CommandRunner.cs`, `src/host/RotatingLog.cs`, `src/host/ValueStore.cs`
- Modify: `src/host/HostContext.cs`
- Test: `tests/ExplorerPanel.Tests/ProcessInfoTests.cs`, `CommandRunnerTests.cs`, `RotatingLogTests.cs`, `ProjectTests.cs`, `ValueStoreTests.cs`

**Interfaces:**
- Consumes: `PanelLoader`, `Template`, `TrustStore`, `ServiceStatusRules`, `HandlerOutput`.
- Produces:
  ```csharp
  static class ProcessInfo {
      static IReadOnlyList<int> ListeningPids(int port);         // GetExtendedTcpTable, IPv4 и IPv6
      static string? WorkingDirectory(int pid);                  // чтение из блока параметров процесса
      static DateTime? StartTime(int pid);  static void KillTree(int pid);
  }
  sealed record CommandResult(int ExitCode, string Output);
  static class CommandRunner {
      static Process Start(string command, string cwd, IReadOnlyDictionary<string,string> env, Action<string> onLine);   // невидимо, cmd.exe /c
      static Task<CommandResult> RunAsync(string command, string cwd, IReadOnlyDictionary<string,string> env, TimeSpan timeout);
  }
  sealed class RotatingLog { RotatingLog(string file, long maxBytes = 5_000_000); void Append(string source, string line); }
  sealed class ValueStore { JsonNode? Get(string name); void Set(string name, JsonNode? value, bool persist); event Action Changed; JsonObject Snapshot(); }
  sealed class Project {
      string Root; LoadResult Loaded; bool? Trusted; event Action Changed;
      void Reload();                                              // при ошибках оставляет последнюю рабочую конфигурацию
      Task DoAsync(string action, IReadOnlyDictionary<string,string>? parameters = null);
      JsonObject BuildState(string locale);                       // PanelState из Task 5
      void SetActive(bool active);                                // есть ли видимая панель: от этого зависят опросы
  }
  sealed class ProjectRegistry { Project? ForFolder(string folder); IReadOnlyCollection<Project> All { get; } }
  ```
- Сервер принадлежит проекту, если его запустила панель (pid и время запуска сохранены в `AppPaths.StateFile`) либо рабочая папка процесса, слушающего порт из `health`, лежит внутри корня проекта.
- Опрос: запускающийся сервер — раз в 0,7 с, работающий — раз в 4 с; адрес с `localhost` проверяется сразу по `127.0.0.1` и `::1`. Проекты без видимой панели опрашиваются, только если у них есть серверы, запущенные панелью, — раз в 10 с.
- При первом запуске команды программа создаёт `.explorer_components/.gitignore` с `logs/`, если файла нет.

- [ ] **Step 1: Тесты:**
  - `ProcessInfoTests`: `ListeningPids_finds_own_listener` (свой `TcpListener` → свой pid), `WorkingDirectory_of_child` (дочерний `cmd.exe` с папкой `проект с пробелами` → эта папка), `KillTree_kills_grandchild`.
  - `CommandRunnerTests`: `Runs_in_cwd_and_captures_output` (`cd` → папка), `Timeout_kills_process`, `Passes_env`.
  - `RotatingLogTests`: `Rotates_when_over_limit` (предел 2000 байт, 200 строк → файл не больше предела, рядом `.1`).
  - `ProjectTests` (сервер — `python -m http.server <свободный порт>`): `Start_then_running_then_stop`, `Double_start_spawns_once`, `Untrusted_project_refuses_commands`, `Broken_json_keeps_last_good_config`, `Fixed_json_reloads`, `Foreign_port_owner_is_failed_and_not_killed`, `Stop_command_is_used_when_present`, `Unsafe_value_blocks_command`.
  - `ValueStoreTests`: `Persisted_value_survives_reload`, `Handler_envelope_sets_value_and_ttl`.
- [ ] **Step 2:** Run: `dotnet test --filter "ProcessInfoTests|CommandRunnerTests|RotatingLogTests|ProjectTests|ValueStoreTests"`. Expected: FAIL.
- [ ] **Step 3:** Реализовать; подключить в `HostContext`: сообщения страницы `do`, `set`, `trust`, `collapse`; рассылка `state` при `Project.Changed`.
- [ ] **Step 4:** Run те же тесты. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(host): projects, services, actions, values`.

### Task 8: Местный интерфейс и команда `explorer-panel`

**Files:**
- Create: `src/host/ApiServer.cs`, `src/cli/Program.cs`, `src/cli/Commands.cs`, `src/cli/CheckCommand.cs`
- Test: `tests/ExplorerPanel.Tests/CheckCommandTests.cs`, `ApiServerTests.cs`

**Interfaces:**
- Consumes: `HostClient`, `Contracts`, `ProjectRegistry`, `TrustStore`.
- Produces:
  ```csharp
  interface IHostApi { StatusResponse Status(string folder); void Set(string folder, JsonObject values); Task Do(string folder, string action); void Trust(string folder, bool? state); void Reload(string folder); }
  sealed class ApiServer : IDisposable { ApiServer(IHostApi api, int preferredPort = 47631); int Port { get; } void Start(); }  // пишет AppPaths.HostInfoFile
  static class CheckCommand { public static int Run(string folder, TextWriter output, HostClient? host); }
  ```
- Пути: `GET /v1/ping`, `GET /v1/status?folder=`, `POST /v1/set`, `POST /v1/do`, `POST /v1/trust`, `POST /v1/reload`. Без заголовка `X-Explorer-Panel: 1` — ответ 403.
- Команды: `check`, `trust`, `untrust`, `status`, `start`, `stop`, `restart`, `do <action>`, `set name=value…`, `logs`, `install`, `uninstall`, `version`. Папка — первый аргумент-путь либо текущая; корень ищется вверх.
- Формат `check`: `panel.json:<строка>:<столбец>: error|warning: <сообщение>`; в конце `OK: N services, N actions, N widgets` либо `FAILED: N errors`; затем строки `host: running|not running`, `trusted: yes|no|not decided`, `loaded: yes|no`. Код возврата 1, если есть ошибки.

- [ ] **Step 1: Тесты:** `Check_ok_prints_summary_and_returns_0`; `Check_error_prints_file_line_col_and_returns_1`; `Check_warning_returns_0`; `Check_without_panel_returns_1` (`no .explorer_components/panel.json found`); `Api_rejects_request_without_header` (403); `Api_set_then_status_roundtrip` (подставной `IHostApi`, случайный порт); `Api_port_falls_forward_when_busy`.
- [ ] **Step 2:** Run: `dotnet test --filter "CheckCommandTests|ApiServerTests"`. Expected: FAIL.
- [ ] **Step 3:** Реализовать (`HttpListener` на `http://127.0.0.1:<порт>/`, при занятом порте — следующие десять).
- [ ] **Step 4:** Run те же тесты. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: local API and explorer-panel CLI`.

### Task 9: Постоянная работа — один экземпляр, трей, автозапуск, установка

**Files:**
- Create: `src/host/Tray.cs`, `src/host/Strings.cs`, `src/cli/InstallCommand.cs`, `scripts/build.ps1`, `scripts/install.ps1`, `scripts/uninstall.ps1`
- Modify: `src/host/Program.cs`, `src/host/HostContext.cs`
- Test: `tests/ExplorerPanel.Tests/InstallCommandTests.cs`

**Interfaces:**
- Produces: `static class InstallCommand { static void Register(string installDir, RegistryKey runKey, Func<string> getPath, Action<string> setPath); static void Unregister(...); }` — значение автозапуска `ExplorerPanels` = `"<installDir>\explorer-panel-host.exe"`, папка добавляется в пользовательский PATH один раз.
- Один экземпляр — именованный мьютекс `Local\ExplorerPanelsHost`; второй запуск завершается сразу.
- Меню трея: проекты с работающими серверами («остановить все»), разрешённые и запрещённые папки, пауза, открыть лог, выход.
- Браузерный движок освобождается, если ни одной панели не видно 5 минут.

- [ ] **Step 1: Тесты:** `Register_sets_run_value_and_adds_path_once` (подставной раздел реестра в `HKCU\Software\ExplorerPanelsTests`), `Register_twice_does_not_duplicate_path`, `Unregister_removes_both`.
- [ ] **Step 2:** Run: `dotnet test --filter InstallCommandTests`. Expected: FAIL.
- [ ] **Step 3:** Реализовать. `build.ps1`: `npm ci`, `npm run build` в `src/ui`; `dotnet publish` хозяина и команды в `artifacts\app`; копирование `src/ui/dist` в `artifacts\app\ui`. `install.ps1`: `build.ps1`, остановка работающего хозяина, копирование в `%LOCALAPPDATA%\Programs\ExplorerPanels`, `explorer-panel install`, запуск хозяина.
- [ ] **Step 4:** Run: тесты; затем `scripts\install.ps1`. Expected: PASS; `explorer-panel version` из новой консоли печатает версию; процесс `explorer-panel-host` один; повторный запуск exe не создаёт второй.
- [ ] **Step 5:** Commit: `feat: tray, single instance, autostart, install scripts`.

### Task 10: Документация для агентов, схема, примеры, навык

**Files:**
- Create: `README.md`, `docs/panel-reference.md`, `docs/how-it-works.md`, `schema/panel.schema.json`, `examples/node/.explorer_components/panel.json`, `examples/docker-compose/.explorer_components/panel.json`, `examples/static-site/.explorer_components/panel.json`, `examples/README.md`, `skill/explorer-panel/SKILL.md`, `.explorer_components/panel.json` (панель самого репозитория: сборка, установка, тесты)
- Test: `tests/ExplorerPanel.Tests/ExamplesTests.cs`

**Interfaces:**
- Consumes: `PanelLoader`, `WidgetTypes`.
- README начинается с раздела `Add a panel to a project` не длиннее экрана: создать файл, выполнить `explorer-panel trust .` и `explorer-panel check .`, минимальный пример.
- Навык: `name: explorer-panel`; описание включает фразы-триггеры «добавь управление в проводник», «добавь кнопок управления текущим проектом», `explorer panel`.

- [ ] **Step 1: Тесты:** `Every_example_panel_loads_without_problems` (все `**/.explorer_components/panel.json` в репозитории, кроме `_spike`); `Schema_widget_types_match_core` (перечисление типов в схеме равно `WidgetTypes.All`); `Readme_minimal_example_is_valid` (первый блок ```json из README проходит `PanelLoader.Parse`).
- [ ] **Step 2:** Run: `dotnet test --filter ExamplesTests`. Expected: FAIL.
- [ ] **Step 3:** Написать документы, схему, примеры, навык.
- [ ] **Step 4:** Run: `dotnet test --filter ExamplesTests`. Expected: PASS.
- [ ] **Step 5:** Commit: `docs: agent recipe, reference, schema, examples, skill`.

### Task 11: Сквозные проверки

**Files:**
- Create: `tests/e2e/lib.ps1` (из рабочих скриптов прототипа: тестовый рабочий стол, окно Проводника, снимок через `tools/winshot`, выполнение JS на странице), `tests/e2e/run.ps1`, `tests/e2e/manual-checklist.md`
- Modify: `src/host/PanelWindow.cs` (отладочный канал выполнения JS — только при `EXPLORER_PANEL_DEBUG=1`)

- [ ] **Step 1:** Сценарии `run.ps1` (каждый печатает `PASS` или `FAIL` с причиной): панель появляется в папке примера; следует за перемещением и изменением размера; прячется при сворачивании; исчезает в папке без панели; появляется после записи валидного файла и не появляется при битом; область кликов совпадает с панелью; `start` → `running`, `stop` → `stopped`; убитый сервер → `failed`; кнопка «расширить» в узком окне; хозяин запущен при закрытом Проводнике и находит окно, открытое позже; окно панели не имеет `WS_EX_APPWINDOW` и лежит сразу над владельцем.
- [ ] **Step 2:** Run: `tests\e2e\run.ps1`. Expected: все `PASS`.
- [ ] **Step 3:** `manual-checklist.md`: подсветка при наведении, проход кликов мимо панели, плавность при перетаскивании, масштаб 150 %, второй монитор.
- [ ] **Step 4:** Commit: `test: end-to-end scenarios on a separate virtual desktop`.

### Task 12: Свой вид `panel.tsx`, клавиатура, снимок панели

**Files:**
- Create: `src/host/CustomView.cs`, `src/ui/src/custom.tsx`, `src/ui/src/api.ts` (модуль `explorer-panel`: `Panel`, `useService`, `useValue`, `run`), `src/ui/scripts/export-shims.mjs`
- Modify: `src/host/PanelWindow.cs`, `src/host/Project.cs`, `src/host/ApiServer.cs`, `src/cli/Commands.cs`, `src/cli/CheckCommand.cs`, `scripts/build.ps1`
- Test: `tests/ExplorerPanel.Tests/CustomViewTests.cs`

**Interfaces:**
- Produces: `static class CustomView { static Task<(string? Script, IReadOnlyList<Problem> Problems)> BuildAsync(string projectRoot, string toolsDir); }` — собирает `.explorer_components/panel.tsx` программой `esbuild.exe` из папки установки; импорты `react`, `lucide-react`, `explorer-panel`, `@/components/ui/*` направляются на заглушки, которые берут модули у уже загруженной страницы.
- Страница загружает собранный скрипт и рисует его компонент по умолчанию внутри границы ошибок; ошибка показа уходит хозяину и видна в `check`.
- Клавиатура: при фокусе в поле ввода страница шлёт `{ type: "focus", wanted: true }`, окно панели активируется; при потере фокуса или Escape фокус возвращается Проводнику.
- `explorer-panel screenshot -o файл.png` — снимок панели через `CoreWebView2.CapturePreviewAsync`.

- [ ] **Step 1: Тесты:** `Valid_tsx_builds`; `Syntax_error_reports_line`; `Unknown_import_is_error`; `Missing_default_export_is_error`.
- [ ] **Step 2:** Run: `dotnet test --filter CustomViewTests`. Expected: FAIL.
- [ ] **Step 3:** Реализовать.
- [ ] **Step 4:** Run: тесты и сквозной сценарий «панель из `panel.tsx` показывает кнопку и реагирует на клик». Expected: PASS.
- [ ] **Step 5:** Commit: `feat: custom panel.tsx views, keyboard focus, screenshot`.

### Task 13: Публикация и глобальный контекст Claude

- [ ] **Step 1:** Удалить `_spike`. Run: `dotnet test ExplorerPanels.slnx`, `npm test` в `src/ui`, `tests\e2e\run.ps1`. Expected: всё проходит.
- [ ] **Step 2:** `scripts\install.ps1` — установить итоговую версию; убедиться, что она запущена и стоит в автозапуске.
- [ ] **Step 3:** `gh repo create explorer-panels --private --source . --remote origin --push`.
- [ ] **Step 4:** Скопировать `skill/explorer-panel` в `~/.claude/skills/explorer-panel`; добавить в `~/.claude/CLAUDE.md` раздел об Explorer Panels со ссылкой на репозиторий и фразой-триггером «добавь управление в проводник».
- [ ] **Step 5:** Независимая проверка всей ветки свежим проверяющим; исправить найденное; `git push`.
