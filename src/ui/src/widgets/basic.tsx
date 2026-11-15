// Виджеты, которые только показывают или запускают действие: статус, кнопка, текст, прогресс, разделитель.

import { Check, X } from "lucide-react"

import { send, type Status, type Widget } from "@/bridge"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Progress } from "@/components/ui/progress"
import { Separator } from "@/components/ui/separator"
import { Spinner } from "@/components/ui/spinner"
import { display, fillText, str, Tip, usePanel, useStrings } from "@/context"
import { Icon } from "@/icons"

const DOT: Record<Status, string> = {
  stopped: "bg-zinc-500",
  starting: "bg-amber-400 animate-pulse",
  running: "bg-emerald-500",
  failed: "bg-red-500",
}

const isStatus = (value: unknown): value is Status =>
  value === "stopped" || value === "starting" || value === "running" || value === "failed"

/** Точка статуса с подписью. Источник — сервер из panel.json либо значение, которое присылает программа проекта. */
export function StatusWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const t = useStrings()
  const service = str(widget.service)
  const valueName = str(widget.value)

  let status: Status = "stopped"
  let detail: string | undefined
  let label = str(widget.label) ?? service ?? valueName ?? ""

  if (service) {
    const info = state.services[service]
    if (info) ({ status, detail } = info)
  } else if (valueName) {
    const value = state.values[valueName]
    if (isStatus(value)) status = value
    else if (value && typeof value === "object") {
      // значение может быть объектом { state, label, detail }
      const object = value as Record<string, unknown>
      if (isStatus(object.state)) status = object.state
      if (!widget.label && typeof object.label === "string") label = object.label
      if (typeof object.detail === "string") detail = object.detail
    }
  }

  const text = widget.tooltip
    ? fillText(widget.tooltip, state.values)
    : `${label}: ${t.status[status]}${detail ? ` — ${detail}` : ""}`

  return (
    <Tip text={text}>
      <Badge
        variant="outline"
        className="h-6 gap-1.5"
        data-service={service}
        data-value={valueName}
        data-status={status}
        aria-label={text}
      >
        <span className={`size-2 rounded-full ${DOT[status]}`} />
        {label}
      </Badge>
    </Tip>
  )
}

/** Кнопка с иконкой и подсказкой (или с подписью). Пока действие выполняется, показывает индикатор. */
export function ButtonWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const action = str(widget.do)
  if (!action) return null

  const icon = str(widget.icon)
  const label = str(widget.label)
  const progress = state.actions[action]
  const name = widget.tooltip ? fillText(widget.tooltip, state.values) : (label ?? action)

  return (
    <Tip text={name}>
      <Button
        variant="ghost"
        size={label ? "sm" : "icon"}
        aria-label={name}
        aria-busy={progress === "running"}
        data-action={action}
        onClick={() => send({ type: "do", action })}
      >
        {progress === "running" ? (
          <Spinner />
        ) : progress === "ok" ? (
          <Check className="text-emerald-500" />
        ) : progress === "failed" ? (
          <X className="text-red-500" />
        ) : icon ? (
          <Icon name={icon} />
        ) : null}
        {label && fillText(label, state.values)}
      </Button>
    </Tip>
  )
}

/** Текст, обычно со значением: "{branch}". Пока значения нет и показать нечего — не занимает места. */
export function TextWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const icon = str(widget.icon)
  const text = fillText(str(widget.text) ?? "", state.values)
  if (!text.trim()) return null

  return (
    <Tip text={widget.tooltip ? fillText(widget.tooltip, state.values) : undefined}>
      <span className="flex items-center gap-1.5 px-1 text-xs whitespace-nowrap">
        {icon && <Icon name={icon} className="size-3.5 text-muted-foreground" />}
        {text}
      </span>
    </Tip>
  )
}

/** Полоса 0–100. */
export function ProgressWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const name = str(widget.value) ?? ""
  const raw = Number(display(state.values[name]))
  const percent = Number.isFinite(raw) ? Math.min(100, Math.max(0, raw)) : 0
  const label = str(widget.label) ?? name
  const text = widget.tooltip ? fillText(widget.tooltip, state.values) : `${label}: ${Math.round(percent)}%`

  return (
    <Tip text={text}>
      <div className="flex items-center px-1" aria-label={text}>
        <Progress value={percent} className="w-20" />
      </div>
    </Tip>
  )
}

export function SeparatorWidget() {
  return <Separator orientation="vertical" className="mx-1 h-5!" />
}
