import { describe, expect, it } from "vitest"

import { chooseVisible } from "@/fit"

describe("chooseVisible", () => {
  it("shows everything when it fits", () => {
    expect(chooseVisible([50, 50, 50], [false, false, false], 150, 30)).toEqual([true, true, true])
  })

  it("hides the leftmost widgets first", () => {
    // 4 × 50 = 200 не помещается в 140: остаются два правых (100) и кнопка «расширить» (30)
    expect(chooseVisible([50, 50, 50, 50], [false, false, false, false], 140, 30)).toEqual([false, false, true, true])
  })

  it("hides pinned widgets last", () => {
    expect(chooseVisible([50, 50, 50, 50], [true, false, false, true], 140, 30)).toEqual([true, false, false, true])
  })

  it("accounts for the width of the fit button", () => {
    // без кнопки два виджета (100) поместились бы в 120, но с кнопкой (30) — уже нет
    expect(chooseVisible([50, 50, 50], [false, false, false], 120, 30)).toEqual([false, false, true])
  })

  it("hides pinned widgets too when even they do not fit", () => {
    expect(chooseVisible([50, 50, 50], [true, true, true], 90, 30)).toEqual([false, false, true])
  })
})
