import { afterEach, describe, expect, it, vi } from "vitest"

import { createFocusTracker } from "@/focus"

afterEach(() => {
  document.body.innerHTML = ""
})

function popup(role: string) {
  const element = document.createElement("div")
  element.setAttribute("role", role)
  document.body.append(element)
  return element
}

describe("focus tracker", () => {
  it("gives focus back after a plain press is over", () => {
    const release = vi.fn()
    const tracker = createFocusTracker(release)

    tracker.press()
    expect(release).not.toHaveBeenCalled() // во время нажатия фокус не трогаем
    tracker.lift()

    expect(release).toHaveBeenCalledTimes(1)
  })

  it("keeps focus while a menu opened by the press is open", () => {
    const release = vi.fn()
    const tracker = createFocusTracker(release)

    tracker.press()
    const menu = popup("menu") // меню открылось по нажатию
    tracker.lift()
    expect(release).not.toHaveBeenCalled() // иначе меню потеряло бы фокус и тут же закрылось

    menu.remove()
    tracker.changed()
    expect(release).toHaveBeenCalledTimes(1)
  })

  it.each(["menu", "listbox", "dialog"])("treats an open %s as needing focus", (role) => {
    const release = vi.fn()
    const tracker = createFocusTracker(release)

    tracker.press()
    popup(role)
    tracker.lift()

    expect(release).not.toHaveBeenCalled()
  })

  it("a tooltip does not hold focus", () => {
    const release = vi.fn()
    const tracker = createFocusTracker(release)

    tracker.press()
    popup("tooltip")
    tracker.lift()

    expect(release).toHaveBeenCalledTimes(1)
  })

  it("does not release when nothing was pressed or opened", () => {
    const release = vi.fn()
    createFocusTracker(release).changed()
    expect(release).not.toHaveBeenCalled()
  })

  it("a press inside an open menu does not release until the menu closes", () => {
    const release = vi.fn()
    const tracker = createFocusTracker(release)
    tracker.press()
    const menu = popup("menu")
    tracker.lift()

    tracker.press() // нажали пункт меню
    tracker.lift()
    expect(release).not.toHaveBeenCalled()

    menu.remove() // пункт выбран, меню закрылось
    tracker.changed()
    expect(release).toHaveBeenCalledTimes(1)
  })
})
