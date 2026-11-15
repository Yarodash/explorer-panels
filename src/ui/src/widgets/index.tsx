// Стандартные виджеты панели: тип из panel.json -> компонент.
// Набор совпадает с WidgetTypes в ядре и с перечислением в schema/panel.schema.json.

import type { ReactNode } from "react"

import type { Widget } from "@/bridge"
import { usePanel, str } from "@/context"
import { ButtonWidget, ProgressWidget, SeparatorWidget, StatusWidget, TextWidget } from "@/widgets/basic"
import { MenuWidget, SelectWidget, ToggleWidget } from "@/widgets/controls"
import { CalendarWidget, ClockWidget, StopwatchWidget } from "@/widgets/time"

/** Виджеты, которые вернул обработчик значения (ключ "widgets" в его выводе). */
function SlotWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const widgets = state.slots[str(widget.value) ?? ""] ?? []
  return (
    <>
      {widgets
        .filter((inner) => inner.type !== "slot") // вложенные слоты не поддерживаются
        .map((inner, i) => (
          <WidgetView key={i} widget={inner} />
        ))}
    </>
  )
}

const registry: Record<string, (props: { widget: Widget }) => ReactNode> = {
  status: StatusWidget,
  button: ButtonWidget,
  text: TextWidget,
  toggle: ToggleWidget,
  select: SelectWidget,
  progress: ProgressWidget,
  menu: MenuWidget,
  clock: ClockWidget,
  stopwatch: StopwatchWidget,
  calendar: CalendarWidget,
  separator: SeparatorWidget,
  slot: SlotWidget,
}

export const widgetTypes = Object.keys(registry)

/** Виджет неизвестного типа ничего не рисует: панель не должна ломаться из-за одного элемента. */
export function WidgetView({ widget }: { widget: Widget }) {
  const Component = registry[widget.type]
  return Component ? <Component widget={widget} /> : null
}
