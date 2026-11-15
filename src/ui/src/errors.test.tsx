import { render, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { fillText } from "@/context"
import { PanelErrorBoundary } from "@/Panel"

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
  vi.spyOn(console, "error").mockImplementation(() => {}) // React сам печатает пойманную ошибку
})

afterEach(() => vi.restoreAllMocks())

function Broken(): never {
  throw new Error("cannot draw the widget")
}

describe("a panel that cannot be drawn", () => {
  it("tells the host what went wrong, so that explorer-panel check can show it", () => {
    render(
      <PanelErrorBoundary fallback={<span>fallback</span>}>
        <Broken />
      </PanelErrorBoundary>
    )

    expect(posted).toContainEqual({ type: "error", message: "cannot draw the widget" })
  })

  it("leaves what does not depend on the panel on screen", () => {
    render(
      <PanelErrorBoundary fallback={<span>fallback</span>}>
        <Broken />
      </PanelErrorBoundary>
    )

    expect(screen.getByText("fallback")).toBeInTheDocument()
  })
})

describe("placeholders in texts", () => {
  it("doubled braces are literal", () => {
    expect(fillText("{{x}} = {x}", { x: 1 })).toBe("{x} = 1")
    expect(fillText("{{{branch}}}", { branch: "main" })).toBe("{main}")
  })

  it("an unknown name is blank", () => {
    expect(fillText("a {nope} b", {})).toBe("a  b")
  })
})
