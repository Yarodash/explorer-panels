// Кому принадлежит фокус: панели или Проводнику.
//
// Нажатие на панель передаёт фокус окну страницы — так работает браузерный движок. Сразу возвращать его
// Проводнику нельзя: меню и списки Radix закрываются, как только окно страницы теряет фокус, и меню
// исчезало бы в момент открытия. Поэтому фокус возвращается, только когда нажатие закончилось
// и ни одной всплывающей части, которой он нужен, не осталось.

import { isHosted, send } from "@/bridge"

/** Всплывающие части, которым нужен фокус, пока они открыты: меню, списки выбора, календарь. Подсказки — нет. */
const POPUPS = '[role="menu"], [role="listbox"], [role="dialog"]'

/**
 * Следит за нажатиями и всплывающими частями и вызывает <c>release</c> в момент, когда панель
 * перестала быть занятой.
 */
export function createFocusTracker(release: () => void) {
  let pressed = false
  let busy = false

  const update = () => {
    const now = pressed || document.querySelector(POPUPS) !== null
    if (busy && !now) release()
    busy = now
  }

  return {
    /** кнопка мыши нажата */
    press() {
      pressed = true
      update()
    },
    /** кнопка мыши отпущена, и обработчики щелчка уже отработали */
    lift() {
      pressed = false
      update()
    },
    /** всплывающая часть появилась или исчезла */
    changed: update,
  }
}

export function startFocusReporting() {
  if (!isHosted()) return
  const tracker = createFocusTracker(() => send({ type: "release" }))

  window.addEventListener("pointerdown", tracker.press, true)
  // Щелчок приходит после отпускания кнопки, а календарь открывается именно по щелчку —
  // даём обработчикам отработать, прежде чем решать, свободна ли панель.
  const lift = () => window.setTimeout(tracker.lift, 60)
  window.addEventListener("pointerup", lift, true)
  window.addEventListener("pointercancel", lift, true)
  // Radix добавляет и убирает всплывающие части прямо в <body>
  new MutationObserver(tracker.changed).observe(document.body, { childList: true })
}
