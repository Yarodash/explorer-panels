// Панель живёт в прозрачном окне-"холсте", которое больше самой панели.
// Чтобы клики мимо панели проходили в Проводник, страница каждый кадр сообщает программе-хозяину
// прямоугольники своих видимых частей: сама панель плюс всё, что всплывает поверх
// (подсказки, календарь, меню, диалоги — shadcn/Radix добавляет их прямо в <body>).

type Rect = [x: number, y: number, width: number, height: number]

function visibleRects(): Rect[] {
  const elements = document.querySelectorAll<HTMLElement>(
    "[data-panel], body > :not(#root):not(script):not(style)"
  )
  const rects: Rect[] = []
  for (const element of elements) {
    const r = element.getBoundingClientRect()
    if (r.width < 2 || r.height < 2) continue // служебные невидимые элементы
    rects.push([Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)])
  }
  return rects
}

export function startRegionReporting() {
  const host = window.chrome?.webview
  if (!host) return // открыто в обычном браузере — сообщать некому
  // Курсор-рука и подсветка при наведении: без этого класса в обычном браузере страница выглядела бы так же,
  // но пусть будет видно, что страница работает внутри программы.
  document.documentElement.dataset.hosted = "true"

  let last = ""
  const tick = () => {
    const rects = visibleRects()
    const key = JSON.stringify(rects)
    if (key !== last) {
      last = key
      host.postMessage({ type: "region", dpr: window.devicePixelRatio, rects })
    }
    requestAnimationFrame(tick)
  }
  requestAnimationFrame(tick)
}
