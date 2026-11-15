// Страница панели. Состояние приходит от программы-хозяина; в обычном браузере показывается пример для вёрстки.

import { useEffect, useState } from "react"

import { isHosted, subscribe, type PanelState } from "@/bridge"
import { PanelRoot } from "@/Panel"

const SAMPLE: PanelState = {
  root: "D:\\project",
  locale: "en",
  trusted: true,
  collapsed: false,
  problems: [],
  widgets: [
    { type: "text", icon: "git-branch", text: "{branch}" },
    { type: "clock" },
    { type: "stopwatch" },
    { type: "toggle", value: "watch", label: "watch" },
    { type: "select", value: "env", options: ["dev", "prod"] },
    { type: "progress", value: "coverage", label: "coverage" },
    { type: "calendar", do: "site" },
    { type: "menu", icon: "ellipsis", tooltip: "More", items: [{ label: "Restart", do: "restart" }] },
    { type: "separator" },
    { type: "status", service: "api" },
    { type: "status", service: "web" },
    { type: "separator" },
    { type: "button", icon: "play", tooltip: "Start", do: "start", pin: true },
    { type: "button", icon: "globe", tooltip: "Open the site", do: "site", pin: true },
    { type: "button", icon: "square", tooltip: "Stop", do: "stop", pin: true },
    { type: "button", icon: "file-text", tooltip: "Logs", do: "logs" },
  ],
  services: { api: { status: "running" }, web: { status: "starting" } },
  values: { branch: "main", watch: true, env: "dev", coverage: 72 },
  actions: {},
  slots: {},
}

export function App() {
  const [state, setState] = useState<PanelState | null>(() => (isHosted() ? null : SAMPLE))
  useEffect(() => subscribe(setState), [])
  return state ? <PanelRoot state={state} /> : null
}

export default App
