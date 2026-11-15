// Обмен сообщениями между страницей панели и программой-хозяином (окном, приклеенным к Проводнику).

export type Status = "stopped" | "starting" | "running" | "failed"

/** Виджет в том виде, в каком его проверило ядро (см. docs/panel-reference.md). */
export type Widget = { type: string; pin?: boolean; tooltip?: string; [key: string]: unknown }

/** Всё, что нужно странице, чтобы нарисовать панель. Присылается целиком при любом изменении. */
export type PanelState = {
  root: string
  locale: "en" | "ru"
  /** true — папка разрешена, false — запрещена, null — владелец ещё не решал */
  trusted: boolean | null
  collapsed: boolean
  /** ошибки последней версии panel.json; панель при этом показывает предыдущую рабочую версию */
  problems: string[]
  widgets: Widget[]
  services: Record<string, { status: Status; detail?: string }>
  values: Record<string, unknown>
  /** состояние действий: выполняется, только что завершилось успешно или с ошибкой */
  actions: Record<string, "running" | "ok" | "failed">
  /** виджеты, которые вернули обработчики значений, — для виджета slot */
  slots: Record<string, Widget[]>
}

// хозяин → страница
export type HostMessage = { type: "state"; state: PanelState }

// страница → хозяин
export type PageMessage =
  | { type: "ready" }
  | { type: "region"; dpr: number; rects: number[][] } // где сейчас видимые части страницы
  | { type: "fit"; width: number; dpr: number } // расширить окно Проводника, чтобы влезло width пикселей
  | { type: "do"; action: string; params?: Record<string, string> }
  | { type: "set"; name: string; value: unknown }
  | { type: "trust"; allow: boolean }
  | { type: "collapse"; collapsed: boolean }
  | { type: "activate" } // по панели нажали: вывести её Проводник на передний план

type Listener = (event: { data: HostMessage }) => void

export type WebViewBridge = {
  postMessage: (message: unknown) => void
  addEventListener: (type: "message", listener: Listener) => void
  removeEventListener: (type: "message", listener: Listener) => void
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge }
  }
}

/** Страница открыта внутри программы, а не в обычном браузере. */
export function isHosted() {
  return Boolean(window.chrome?.webview)
}

export function send(message: PageMessage) {
  window.chrome?.webview?.postMessage(message)
}

/** Подписка на состояние от хозяина. Возвращает функцию отписки. */
export function subscribe(onState: (state: PanelState) => void) {
  const bridge = window.chrome?.webview
  if (!bridge) return () => {}
  const listener: Listener = (event) => {
    if (event.data?.type === "state") onState(event.data.state)
  }
  bridge.addEventListener("message", listener)
  send({ type: "ready" })
  return () => bridge.removeEventListener("message", listener)
}
