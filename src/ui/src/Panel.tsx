// Сама панель: рамка, кнопка сворачивания, виджеты и правило «не помещается — прячем целиком».

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react"
import { ChevronLeft, ChevronRight, MoveHorizontal, ShieldQuestion, TriangleAlert } from "lucide-react"

import { send, type PanelState, type Widget } from "@/bridge"
import { Button } from "@/components/ui/button"
import { TooltipProvider } from "@/components/ui/tooltip"
import { PanelProvider, Tip, usePanel, useStrings } from "@/context"
import { chooseVisible } from "@/fit"
import { WidgetView } from "@/widgets"

const FRAME =
  "fixed right-2 bottom-2 flex h-11 w-max items-center gap-1 rounded-xl border bg-[oklch(0.225_0_0)] px-1.5 text-sm text-foreground shadow-lg select-none"

const EDGE = 8 // отступ панели от краёв холста (right-2 / bottom-2)
const GAP = 4 // промежуток между виджетами (gap-1)
const CHROME = 42 // поля и рамка панели, кнопка сворачивания и промежуток после неё
const FIT_BUTTON = 28 + GAP

/** Закреплённые виджеты остаются в свёрнутой панели и прячутся последними, когда не хватает места. */
const isPinned = (widget: Widget) => widget.type === "status" || widget.pin === true

/**
 * Следит, помещаются ли виджеты в отведённую ширину (холст = область файлов Проводника).
 * Ширина каждого виджета запоминается, пока он виден: спрятанный виджет измерить нельзя,
 * а знать, поместится ли он обратно, нужно.
 */
function useFit(widgets: Widget[]) {
  const elements = useRef<(HTMLElement | null)[]>([])
  const [widths, setWidths] = useState<number[]>([])
  const [viewport, setViewport] = useState(() => window.innerWidth)

  useEffect(() => {
    const onResize = () => setViewport(window.innerWidth)
    window.addEventListener("resize", onResize)
    return () => window.removeEventListener("resize", onResize)
  }, [])

  useLayoutEffect(() => {
    const measured = widgets.map((_, i) => {
      const width = elements.current[i]?.offsetWidth ?? 0
      return width > 0 ? width : (widths[i] ?? 0)
    })
    if (measured.length !== widths.length || measured.some((width, i) => width !== widths[i])) setWidths(measured)
  })

  const withGaps = widgets.map((_, i) => ((widths[i] ?? 0) > 0 ? widths[i] + GAP : 0))
  const visible =
    widths.length === widgets.length
      ? chooseVisible(withGaps, widgets.map(isPinned), viewport - 2 * EDGE - CHROME, FIT_BUTTON)
      : widgets.map(() => true)
  const neededWidth = Math.ceil(withGaps.reduce((sum, width) => sum + width, 0) + CHROME + 2 * EDGE)

  return { elements, visible, cramped: visible.includes(false), neededWidth }
}

function Panel() {
  const state = usePanel()
  const t = useStrings()
  const shown = useMemo(
    () => (state.collapsed ? state.widgets.filter(isPinned) : state.widgets),
    [state.collapsed, state.widgets]
  )
  const fit = useFit(shown)
  const collapseLabel = state.collapsed ? t.expand : t.collapse

  return (
    <div data-panel className={FRAME}>
      <Tip text={collapseLabel}>
        <Button
          variant="ghost"
          size="icon-xs"
          aria-label={collapseLabel}
          onClick={() => send({ type: "collapse", collapsed: !state.collapsed })}
        >
          {state.collapsed ? <ChevronLeft /> : <ChevronRight />}
        </Button>
      </Tip>

      {/* новая версия panel.json с ошибками: панель показывает прежнюю рабочую и предупреждает */}
      {state.problems.length > 0 && (
        <Tip text={`${t.problems}\n${state.problems.slice(0, 3).join("\n")}`}>
          <span className="flex size-6 items-center justify-center" aria-label={t.problems}>
            <TriangleAlert className="size-4 text-amber-400" />
          </span>
        </Tip>
      )}

      {/* не помещается целиком — предлагаем расширить окно Проводника ровно настолько, чтобы влезло */}
      {fit.cramped && (
        <Tip text={t.fit}>
          <Button
            variant="outline"
            size="icon-sm"
            data-fit-button
            aria-label={t.fit}
            onClick={() => send({ type: "fit", width: fit.neededWidth, dpr: window.devicePixelRatio })}
          >
            <MoveHorizontal />
          </Button>
        </Tip>
      )}

      {shown.map((widget, i) => (
        <span
          key={i}
          ref={(element) => {
            fit.elements.current[i] = element
          }}
          className={fit.visible[i] ? "flex shrink-0 items-center empty:hidden" : "hidden"}
        >
          <WidgetView widget={widget} />
        </span>
      ))}
    </div>
  )
}

/** Пока владелец не решил, разрешать ли панель папки, вместо панели показывается этот вопрос. */
function TrustPrompt() {
  const t = useStrings()
  return (
    <div data-panel className={`${FRAME} gap-2 px-3`}>
      <ShieldQuestion className="size-4 text-amber-400" />
      <span className="text-xs">{t.trustQuestion}</span>
      <Button size="sm" onClick={() => send({ type: "trust", allow: true })}>
        {t.allow}
      </Button>
      <Button size="sm" variant="ghost" onClick={() => send({ type: "trust", allow: false })}>
        {t.deny}
      </Button>
    </div>
  )
}

export function PanelRoot({ state }: { state: PanelState }) {
  return (
    <TooltipProvider delayDuration={350}>
      <PanelProvider value={state}>
        {state.trusted === true ? <Panel /> : state.trusted === null ? <TrustPrompt /> : null}
      </PanelProvider>
    </TooltipProvider>
  )
}
