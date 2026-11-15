import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { PanelState } from "@/bridge"
import { PanelRoot } from "@/Panel"

const OPEN = "Open Claude in this folder"
const CONTINUE = "Claude: continue the last conversation in this folder (claude --continue)"

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

function show(patch: Partial<PanelState> = {}) {
  const state: PanelState = {
    root: "D:\\any folder",
    locale: "en",
    trusted: true,
    collapsed: false,
    problems: [],
    widgets: [],
    services: {},
    values: {},
    actions: {},
    slots: {},
    ...patch,
  }
  return render(<PanelRoot state={state} />)
}

const fill = (name: string) => screen.getByRole("button", { name }).querySelector("svg")?.getAttribute("fill")

describe("Claude buttons", () => {
  it("a folder without a panel shows just the two buttons", () => {
    show({ bare: true, claude: true })

    expect(screen.getAllByRole("button").map((button) => button.getAttribute("aria-label"))).toEqual([OPEN, CONTINUE])
  })

  it("the left button opens Claude, the right one continues the last conversation", async () => {
    show({ bare: true, claude: true })

    await userEvent.click(screen.getByRole("button", { name: OPEN }))
    await userEvent.click(screen.getByRole("button", { name: CONTINUE }))

    expect(posted).toEqual([
      { type: "claude", resume: false },
      { type: "claude", resume: true },
    ])
  })

  it("the second icon is the same shape in a redder colour", () => {
    show({ bare: true, claude: true })

    const [open, resume] = [OPEN, CONTINUE].map((name) => screen.getByRole("button", { name }).querySelector("path"))
    expect(open?.getAttribute("d")).toBeTruthy()
    expect(resume?.getAttribute("d")).toBe(open?.getAttribute("d"))

    // оба цвета вида #RRGGBB; «краснее» — меньше зелёного при не меньшем красном
    const channels = (hex: string) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16))
    const [r1, g1] = channels(fill(OPEN)!)
    const [r2, g2] = channels(fill(CONTINUE)!)
    expect(r2).toBeGreaterThanOrEqual(r1)
    expect(g2).toBeLessThan(g1 - 30)
  })

  it("are added at the right end of a project's panel", () => {
    show({ claude: true, widgets: [{ type: "button", icon: "play", tooltip: "Start", do: "start" }] })

    const labels = [...document.querySelectorAll("[data-panel] button")].map((button) => button.getAttribute("aria-label"))
    expect(labels.slice(-3)).toEqual(["Start", OPEN, CONTINUE])
  })

  it("stay in a collapsed panel", () => {
    show({ claude: true, collapsed: true, widgets: [{ type: "button", icon: "play", tooltip: "Start", do: "start" }] })

    expect(screen.queryByRole("button", { name: "Start" })).toBeNull()
    expect(screen.getByRole("button", { name: OPEN })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: CONTINUE })).toBeInTheDocument()
  })

  it("are shown next to the question about an undecided folder", () => {
    show({ claude: true, trusted: null })

    expect(screen.getByRole("button", { name: "Allow" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: OPEN })).toBeInTheDocument()
  })

  it("are absent when turned off in the tray", () => {
    show({ widgets: [{ type: "button", icon: "play", tooltip: "Start", do: "start" }] })

    expect(screen.queryByRole("button", { name: OPEN })).toBeNull()
    expect(screen.queryByRole("button", { name: CONTINUE })).toBeNull()
  })

  it("speak Russian on a Russian system", () => {
    show({ bare: true, claude: true, locale: "ru" })

    expect(screen.getByRole("button", { name: "Открыть Claude в этой папке" })).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Claude: продолжить последний разговор в этой папке (claude --continue)" })
    ).toBeInTheDocument()
  })
})
