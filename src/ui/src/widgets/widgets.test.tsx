import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { PanelState, Widget } from "@/bridge"
import { PanelRoot } from "@/Panel"

// всё, что страница отправила программе-хозяину
const posted: unknown[] = []

beforeEach(() => {
  posted.length = 0
  window.chrome = {
    webview: {
      postMessage: (message: unknown) => posted.push(message),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    },
  }
})

function show(widgets: Widget[], patch: Partial<PanelState> = {}) {
  const state: PanelState = {
    root: "D:\\project",
    locale: "en",
    trusted: true,
    collapsed: false,
    problems: [],
    widgets,
    services: {},
    values: {},
    actions: {},
    slots: {},
    ...patch,
  }
  return render(<PanelRoot state={state} />)
}

describe("standard widgets", () => {
  it("status shows the service state and the reason", () => {
    show([{ type: "status", service: "api" }], {
      services: { api: { status: "failed", detail: "port in use by another program: node.exe (4242)" } },
    })

    const status = document.querySelector('[data-panel] [data-service="api"]')
    expect(status).toHaveAttribute("data-status", "failed")
    expect(status).toHaveAttribute("aria-label", "api: failed — port in use by another program: node.exe (4242)")
  })

  it("status can follow a value pushed by the project's own program", () => {
    show([{ type: "status", value: "deploy" }], { values: { deploy: "running" } })

    expect(document.querySelector('[data-panel] [data-value="deploy"]')).toHaveAttribute("data-status", "running")
  })

  it("button click sends the action", async () => {
    show([{ type: "button", icon: "play", tooltip: "Start", do: "start" }])

    await userEvent.click(screen.getByRole("button", { name: "Start" }))

    expect(posted).toEqual([{ type: "do", action: "start" }])
  })

  it("button is busy while its action runs", () => {
    show([{ type: "button", icon: "play", tooltip: "Start", do: "start" }], { actions: { start: "running" } })

    expect(screen.getByRole("button", { name: "Start" })).toHaveAttribute("aria-busy", "true")
  })

  it("text fills placeholders with values", () => {
    show([{ type: "text", text: "branch {branch}" }], { values: { branch: "main" } })

    expect(screen.getByText("branch main")).toBeInTheDocument()
  })

  it("toggle sets the value and runs the matching action", async () => {
    show([{ type: "toggle", value: "watch", label: "watch", on: "watch-on", off: "watch-off" }], { values: { watch: false } })

    await userEvent.click(screen.getByRole("switch"))

    expect(posted).toEqual([
      { type: "set", name: "watch", value: true },
      { type: "do", action: "watch-on" },
    ])
  })

  it("select sets the chosen option", async () => {
    show([{ type: "select", value: "env", options: ["dev", "prod"] }], { values: { env: "dev" } })

    await userEvent.click(screen.getByRole("radio", { name: "prod" }))

    expect(posted).toEqual([{ type: "set", name: "env", value: "prod" }])
  })

  it("menu item runs its action", async () => {
    show([{ type: "menu", icon: "ellipsis", tooltip: "More", items: [{ label: "Restart", do: "restart" }] }])

    await userEvent.click(screen.getByRole("button", { name: "More" }))
    await userEvent.click(await screen.findByRole("menuitem", { name: "Restart" }))

    expect(posted).toEqual([{ type: "do", action: "restart" }])
  })

  it("slot shows widgets returned by a handler", () => {
    show([{ type: "slot", value: "commits" }], { slots: { commits: [{ type: "text", text: "3 new commits" }] } })

    expect(screen.getByText("3 new commits")).toBeInTheDocument()
  })

  it("an unknown widget type renders nothing and breaks nothing", () => {
    show([{ type: "hologram" }, { type: "text", text: "still here" }])

    expect(screen.getByText("still here")).toBeInTheDocument()
  })
})

describe("panel", () => {
  it("asks for permission before showing an undecided folder's panel", async () => {
    show([{ type: "button", icon: "play", tooltip: "Start", do: "start" }], { trusted: null })

    expect(screen.queryByRole("button", { name: "Start" })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole("button", { name: "Allow" }))

    expect(posted).toEqual([{ type: "trust", allow: true }])
  })

  it("shows nothing for a blocked folder", () => {
    show([{ type: "text", text: "secret" }], { trusted: false })

    expect(document.querySelector("[data-panel]")).toBeNull()
  })

  it("collapsed panel keeps statuses and pinned widgets only", () => {
    show(
      [
        { type: "text", text: "extra" },
        { type: "status", service: "api" },
        { type: "button", icon: "play", tooltip: "Start", do: "start", pin: true },
        { type: "button", icon: "file-text", tooltip: "Logs", do: "logs" },
      ],
      { collapsed: true, services: { api: { status: "running" } } }
    )

    const panel = document.querySelector("[data-panel]")!
    expect(panel.querySelector('[data-service="api"]')).not.toBeNull()
    expect(panel.querySelector('[aria-label="Start"]')).not.toBeNull()
    expect(panel.querySelector('[aria-label="Logs"]')).toBeNull()
    expect(panel).not.toHaveTextContent("extra")
  })

  it("collapse button asks the host to remember the state", async () => {
    show([{ type: "text", text: "x" }])

    await userEvent.click(screen.getByRole("button", { name: "Collapse the panel" }))

    expect(posted).toEqual([{ type: "collapse", collapsed: true }])
  })

  it("warns when panel.json has errors", () => {
    show([{ type: "text", text: "x" }], { problems: ["panel.json:3:5: error: unknown icon"] })

    expect(screen.getByLabelText(/panel\.json has errors/)).toBeInTheDocument()
  })
})
