// ЧЕРНОВОЙ ПРОТОТИП (на выброс): панель из настоящих компонентов shadcn/ui внутри Проводника.
// Серверы запускает и останавливает программа-хозяин; страница только показывает их статусы и шлёт команды.

import { useEffect, useLayoutEffect, useRef, useState } from "react"
import {
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  Ellipsis,
  FileText,
  Globe,
  MoveHorizontal,
  Play,
  RotateCcw,
  Square,
  Timer,
} from "lucide-react"
import { ru } from "react-day-picker/locale"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Calendar } from "@/components/ui/calendar"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Separator } from "@/components/ui/separator"
import { Slider } from "@/components/ui/slider"
import { Switch } from "@/components/ui/switch"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip"

// ── связь с программой-хозяином (окно, приклеенное к Проводнику) ──

// страница -> программа
type PageMessage =
  | { type: "ready" } // страница загрузилась и ждёт статусы
  | { type: "start" }
  | { type: "stop" }
  | { type: "restart" }
  | { type: "openSite"; suffix?: string } // открыть сайт; если серверы не работают — сначала поднять их
  | { type: "logs" }
  | { type: "fit"; width: number; dpr: number } // расширить окно Проводника, чтобы влезло width пикселей

// программа -> страница
type Status = "stopped" | "starting" | "running" | "failed"
type Service = { name: string; status: Status }
type HostMessage = { type: "state"; services: Service[] }

type WebViewBridge = {
  postMessage: (message: unknown) => void
  addEventListener: (type: "message", listener: (event: { data: HostMessage }) => void) => void
  removeEventListener: (type: "message", listener: (event: { data: HostMessage }) => void) => void
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge }
  }
}

function send(message: PageMessage) {
  window.chrome?.webview?.postMessage(message)
}

// Статусы серверов приходят от программы — и сразу, как только что-то изменилось.
function useServices() {
  const [services, setServices] = useState<Service[]>([])
  useEffect(() => {
    const bridge = window.chrome?.webview
    if (!bridge) {
      // открыто в обычном браузере (отладка вёрстки) — показываем пример
      setServices([
        { name: "api", status: "running" },
        { name: "web", status: "starting" },
      ])
      return
    }
    const onMessage = (event: { data: HostMessage }) => {
      if (event.data.type === "state") setServices(event.data.services)
    }
    bridge.addEventListener("message", onMessage)
    send({ type: "ready" })
    return () => bridge.removeEventListener("message", onMessage)
  }, [])
  return services
}

const STATUS: Record<Status, { label: string; dot: string }> = {
  stopped: { label: "не запущен", dot: "bg-zinc-500" },
  starting: { label: "запускается…", dot: "bg-amber-400 animate-pulse" },
  running: { label: "работает", dot: "bg-emerald-500" },
  failed: { label: "упал — смотри логи", dot: "bg-red-500" },
}

function Tip({ text, children }: { text: string; children: React.ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>{children}</TooltipTrigger>
      <TooltipContent side="top">{text}</TooltipContent>
    </Tooltip>
  )
}

function useClock() {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 250)
    return () => clearInterval(id)
  }, [])
  return now
}

function useStopwatch() {
  const [running, setRunning] = useState(false)
  const [elapsed, setElapsed] = useState(0)
  const startedAt = useRef(0)
  const base = useRef(0)

  useEffect(() => {
    if (!running) return
    startedAt.current = performance.now()
    const id = setInterval(
      () => setElapsed(base.current + performance.now() - startedAt.current),
      100
    )
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
  return { running, toggle: () => setRunning((r) => !r), reset, text: `${minutes}:${seconds}.${tenths}` }
}

const EDGE = 8 // отступ панели от краёв холста (класс right-2 / bottom-2)

// Следит, помещается ли панель целиком в отведённую ей ширину (холст = область файлов Проводника).
// Когда не помещается, дополнительные элементы прячутся целиком — ничего не обрезается на полуслове.
function useFit(wantExtras: boolean) {
  const panel = useRef<HTMLDivElement>(null)
  const [fullWidth, setFullWidth] = useState(0) // ширина панели со всеми элементами
  const [viewport, setViewport] = useState(() => window.innerWidth)

  useEffect(() => {
    const onResize = () => setViewport(window.innerWidth)
    window.addEventListener("resize", onResize)
    return () => window.removeEventListener("resize", onResize)
  }, [])

  const cramped = fullWidth > viewport - 2 * EDGE
  const showExtras = wantExtras && !cramped

  // пока дополнительные элементы на экране — запоминаем, сколько места панель занимает целиком
  useLayoutEffect(() => {
    if (!showExtras || !panel.current) return
    const width = Math.ceil(panel.current.getBoundingClientRect().width)
    if (width !== fullWidth) setFullWidth(width)
  })

  return { panel, showExtras, cramped: wantExtras && cramped, neededWidth: fullWidth + 2 * EDGE }
}

export function App() {
  const now = useClock()
  const stopwatch = useStopwatch()
  const services = useServices()
  const [expanded, setExpanded] = useState(true)
  const [auto, setAuto] = useState(false)
  const [env, setEnv] = useState("dev")
  const [poll, setPoll] = useState([5])
  const [showClock, setShowClock] = useState(true)
  const [calendarOpen, setCalendarOpen] = useState(false)
  const [date, setDate] = useState<Date | undefined>(() => new Date())
  const fit = useFit(expanded)

  const anyUp = services.some((s) => s.status === "running" || s.status === "starting")

  return (
    <TooltipProvider delayDuration={350}>
      <div
        ref={fit.panel}
        data-panel
        className="fixed right-2 bottom-2 flex h-11 w-max items-center gap-1 rounded-xl border bg-[oklch(0.225_0_0)] px-1.5 text-sm text-foreground shadow-lg select-none"
      >
        <Tip text={expanded ? "Свернуть панель" : "Развернуть панель"}>
          <Button variant="ghost" size="icon-xs" onClick={() => setExpanded((e) => !e)}>
            {expanded ? <ChevronRight /> : <ChevronLeft />}
          </Button>
        </Tip>

        {/* панель не помещается целиком — предлагаем расширить окно Проводника ровно настолько, чтобы влезла */}
        {fit.cramped && (
          <>
            <Tip text="Панель не помещается целиком. Расширить окно Проводника">
              <Button
                variant="outline"
                size="icon-sm"
                data-fit-button
                onClick={() => send({ type: "fit", width: fit.neededWidth, dpr: window.devicePixelRatio })}
              >
                <MoveHorizontal />
              </Button>
            </Tip>
            <Separator orientation="vertical" className="mx-1 h-5!" />
          </>
        )}

        {fit.showExtras && (
          <>
            {showClock && (
              <>
                <Tip text="Текущее время">
                  <span className="px-1 text-xs tabular-nums">{now.toLocaleTimeString("ru-RU")}</span>
                </Tip>
                <Separator orientation="vertical" className="mx-1 h-5!" />
              </>
            )}

            <Tip text="Секундомер: старт / пауза">
              <Button variant="ghost" size="icon-sm" onClick={stopwatch.toggle}>
                <Timer />
              </Button>
            </Tip>
            <span className={`w-13 text-xs tabular-nums ${stopwatch.running ? "text-sky-400" : ""}`}>
              {stopwatch.text}
            </span>
            <Tip text="Сбросить секундомер">
              <Button variant="ghost" size="icon-xs" onClick={stopwatch.reset}>
                <RotateCcw />
              </Button>
            </Tip>
            <Separator orientation="vertical" className="mx-1 h-5!" />

            <Tip text={`Автозапуск при открытии папки: ${auto ? "включён" : "выключен"}`}>
              <label className="flex items-center gap-2 px-1 text-xs text-muted-foreground">
                авто
                <Switch checked={auto} onCheckedChange={setAuto} />
              </label>
            </Tip>

            <ToggleGroup
              type="single"
              variant="outline"
              size="sm"
              spacing={0}
              value={env}
              onValueChange={(value) => value && setEnv(value)}
            >
              <ToggleGroupItem value="dev">dev</ToggleGroupItem>
              <ToggleGroupItem value="prod">prod</ToggleGroupItem>
            </ToggleGroup>

            <Tip text={`Опрашивать статус раз в ${poll[0]} с`}>
              <div className="flex items-center px-2">
                <Slider className="w-20" min={1} max={30} step={1} value={poll} onValueChange={setPoll} />
              </div>
            </Tip>
            <Separator orientation="vertical" className="mx-1 h-5!" />

            <Popover open={calendarOpen} onOpenChange={setCalendarOpen}>
              <Tip text="Календарь: нажми на дату — откроется сайт">
                <PopoverTrigger asChild>
                  <Button variant="outline" size="sm">
                    <CalendarDays />
                    {(date ?? now).getDate()}
                  </Button>
                </PopoverTrigger>
              </Tip>
              <PopoverContent side="top" align="end" className="w-auto p-0">
                <Calendar
                  mode="single"
                  locale={ru}
                  selected={date}
                  onSelect={(day) => {
                    const picked = day ?? date // повторный клик по выбранной дате тоже открывает сайт
                    if (day) setDate(day)
                    setCalendarOpen(false)
                    if (picked) send({ type: "openSite", suffix: `/?date=${picked.toLocaleDateString("sv-SE")}` })
                  }}
                />
              </PopoverContent>
            </Popover>

            <DropdownMenu>
              <Tip text="Ещё">
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" size="icon-sm">
                    <Ellipsis />
                  </Button>
                </DropdownMenuTrigger>
              </Tip>
              <DropdownMenuContent side="top" align="end">
                <DropdownMenuLabel>Панель</DropdownMenuLabel>
                <DropdownMenuCheckboxItem checked={showClock} onCheckedChange={setShowClock}>
                  Показывать время
                </DropdownMenuCheckboxItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem onSelect={() => send({ type: "restart" })}>Перезапустить серверы</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            <Separator orientation="vertical" className="mx-1 h-5!" />
          </>
        )}

        {/* статусы серверов — настоящие, приходят от программы */}
        {services.map((service) => (
          <Tip key={service.name} text={`${service.name}: ${STATUS[service.status].label}`}>
            <Badge variant="outline" className="h-6 gap-1.5" data-service={service.name} data-status={service.status}>
              <span className={`size-2 rounded-full ${STATUS[service.status].dot}`} />
              {service.name}
            </Badge>
          </Tip>
        ))}
        {services.length > 0 && <Separator orientation="vertical" className="mx-1 h-5!" />}

        <Tip text="Запустить сервер">
          <Button variant="ghost" size="icon" data-action="start" onClick={() => send({ type: "start" })}>
            <Play />
          </Button>
        </Tip>
        <Tip text={anyUp ? "Открыть сайт" : "Открыть сайт (сначала запустит сервер)"}>
          <Button variant="ghost" size="icon" data-action="open" onClick={() => send({ type: "openSite" })}>
            <Globe />
          </Button>
        </Tip>
        <Tip text="Выключить сервер">
          <Button variant="ghost" size="icon" data-action="stop" onClick={() => send({ type: "stop" })}>
            <Square />
          </Button>
        </Tip>
        <Tip text="Логи">
          <Button variant="ghost" size="icon" data-action="logs" onClick={() => send({ type: "logs" })}>
            <FileText />
          </Button>
        </Tip>
      </div>
    </TooltipProvider>
  )
}

export default App
