// Состояние панели, доступное всем виджетам, и мелкие общие помощники.

import { createContext, useContext } from "react"

import type { PanelState } from "@/bridge"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { strings } from "@/i18n"

const PanelContext = createContext<PanelState | null>(null)

export const PanelProvider = PanelContext.Provider

export function usePanel(): PanelState {
  const state = useContext(PanelContext)
  if (!state) throw new Error("usePanel must be used inside the panel")
  return state
}

export function useStrings() {
  return strings[usePanel().locale] ?? strings.en
}

/** Значение в виде текста для показа. */
export function display(value: unknown): string {
  if (value === null || value === undefined) return ""
  if (typeof value === "string") return value
  if (typeof value === "number" || typeof value === "boolean") return String(value)
  return JSON.stringify(value)
}

/**
 * Заменяет {имя} в тексте текущими значениями; неизвестное имя — пустой строкой.
 * Удвоенная скобка — буквальная: "{{" даёт "{", "}}" даёт "}" (так же, как в командах panel.json).
 */
export function fillText(text: string, values: Record<string, unknown>): string {
  return text.replace(/\{\{|\}\}|\{([A-Za-z0-9_-]+)\}/g, (match: string, name?: string) =>
    name === undefined ? match[0] : display(values[name])
  )
}

/** Поле виджета как строка (или undefined, если его нет либо оно другого типа). */
export function str(value: unknown): string | undefined {
  return typeof value === "string" && value.length > 0 ? value : undefined
}

/** Всплывающая подсказка при наведении. Без текста просто показывает содержимое. */
export function Tip({ text, children }: { text?: string; children: React.ReactElement }) {
  if (!text) return children
  return (
    <Tooltip>
      <TooltipTrigger asChild>{children}</TooltipTrigger>
      <TooltipContent side="top" className="whitespace-pre-line">
        {text}
      </TooltipContent>
    </Tooltip>
  )
}
