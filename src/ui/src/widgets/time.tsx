// Виджеты про время: часы, секундомер, календарь.

import { useEffect, useRef, useState } from "react"
import { CalendarDays, RotateCcw, Timer } from "lucide-react"
import { ru } from "react-day-picker/locale"

import { send, type Widget } from "@/bridge"
import { Button } from "@/components/ui/button"
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { fillText, str, Tip, usePanel, useStrings } from "@/context"
import { Icon } from "@/icons"

/** Текущее время. "seconds": false — без секунд. */
export function ClockWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const t = useStrings()
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 500)
    return () => clearInterval(id)
  }, [])

  const text = now.toLocaleTimeString(state.locale === "ru" ? "ru-RU" : "en-GB", {
    hour: "2-digit",
    minute: "2-digit",
    second: widget.seconds === false ? undefined : "2-digit",
  })
  return (
    <Tip text={widget.tooltip ? fillText(widget.tooltip, state.values) : t.clock}>
      <span className="px-1 text-xs tabular-nums">{text}</span>
    </Tip>
  )
}

/** Секундомер: старт и пауза по кнопке, отдельная кнопка сброса. */
export function StopwatchWidget() {
  const t = useStrings()
  const [running, setRunning] = useState(false)
  const [elapsed, setElapsed] = useState(0)
  const startedAt = useRef(0)
  const base = useRef(0)

  useEffect(() => {
    if (!running) return
    startedAt.current = performance.now()
    const id = setInterval(() => setElapsed(base.current + performance.now() - startedAt.current), 100)
    return () => {
      clearInterval(id)
      base.current += performance.now() - startedAt.current
    }
  }, [running])

  const reset = () => {
    setRunning(false)
    base.current = 0
    setElapsed(0)
  }
  const minutes = String(Math.floor(elapsed / 60000)).padStart(2, "0")
  const seconds = String(Math.floor(elapsed / 1000) % 60).padStart(2, "0")
  const tenths = Math.floor(elapsed / 100) % 10

  return (
    <span className="flex items-center">
      <Tip text={t.stopwatchToggle}>
        <Button variant="ghost" size="icon-sm" aria-label={t.stopwatchToggle} onClick={() => setRunning((r) => !r)}>
          <Timer />
        </Button>
      </Tip>
      <span className={`w-13 text-xs tabular-nums ${running ? "text-sky-400" : ""}`}>
        {minutes}:{seconds}.{tenths}
      </span>
      <Tip text={t.stopwatchReset}>
        <Button variant="ghost" size="icon-xs" aria-label={t.stopwatchReset} onClick={reset}>
          <RotateCcw />
        </Button>
      </Tip>
    </span>
  )
}

/** Кнопка с числом и всплывающим календарём. Клик по дате запускает действие, которому передаётся {date}. */
export function CalendarWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const [open, setOpen] = useState(false)
  const [date, setDate] = useState<Date | undefined>(() => new Date())
  const action = str(widget.do)
  const icon = str(widget.icon)
  if (!action) return null

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <Tip text={widget.tooltip ? fillText(widget.tooltip, state.values) : undefined}>
        <PopoverTrigger asChild>
          <Button variant="outline" size="sm" aria-label={widget.tooltip ?? "calendar"}>
            {icon ? <Icon name={icon} /> : <CalendarDays />}
            {(date ?? new Date()).getDate()}
          </Button>
        </PopoverTrigger>
      </Tip>
      <PopoverContent side="top" align="end" className="w-auto p-0">
        <Calendar
          mode="single"
          locale={state.locale === "ru" ? ru : undefined}
          selected={date}
          onSelect={(day) => {
            const picked = day ?? date // повторный клик по уже выбранной дате тоже считается выбором
            if (day) setDate(day)
            setOpen(false)
            // "sv-SE" даёт дату в виде ГГГГ-ММ-ДД по местному времени
            if (picked) send({ type: "do", action, params: { date: picked.toLocaleDateString("sv-SE") } })
          }}
        />
      </PopoverContent>
    </Popover>
  )
}
