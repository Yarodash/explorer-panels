// Виджеты, которые меняют значения или открывают список: тумблер, выбор из вариантов, меню.

import { send, type Widget } from "@/bridge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { display, fillText, str, Tip, usePanel } from "@/context"
import { Icon } from "@/icons"

/** Значение считается «включено», если это true или непустой текст, не равный "false", "0", "off", "no". */
function isOn(value: unknown) {
  if (typeof value === "string") return !["", "false", "0", "off", "no"].includes(value.toLowerCase())
  return Boolean(value)
}

/** Тумблер: меняет значение и, если заданы, запускает действие "on" или "off". */
export function ToggleWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const name = str(widget.value)
  if (!name) return null
  const label = str(widget.label)

  const change = (checked: boolean) => {
    send({ type: "set", name, value: checked })
    const action = str(checked ? widget.on : widget.off)
    if (action) send({ type: "do", action })
  }

  return (
    <Tip text={widget.tooltip ? fillText(widget.tooltip, state.values) : undefined}>
      <label className="flex items-center gap-2 px-1 text-xs whitespace-nowrap text-muted-foreground">
        {label}
        <Switch checked={isOn(state.values[name])} onCheckedChange={change} aria-label={label ?? name} />
      </label>
    </Tip>
  )
}

/** Выбор из вариантов: до четырёх — кнопки в ряд, больше — выпадающий список. */
export function SelectWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const name = str(widget.value)
  const options = Array.isArray(widget.options) ? widget.options.filter((o): o is string => typeof o === "string") : []
  if (!name || options.length === 0) return null
  const current = display(state.values[name])

  const change = (value: string) => {
    if (!value || value === current) return
    send({ type: "set", name, value })
    const action = str(widget.do)
    if (action) send({ type: "do", action })
  }

  const control =
    options.length <= 4 ? (
      <ToggleGroup type="single" variant="outline" size="sm" spacing={0} value={current} onValueChange={change}>
        {options.map((option) => (
          <ToggleGroupItem key={option} value={option} aria-label={option}>
            {option}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
    ) : (
      <Select value={current} onValueChange={change}>
        <SelectTrigger size="sm" aria-label={name}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent side="top">
          {options.map((option) => (
            <SelectItem key={option} value={option}>
              {option}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    )

  return <Tip text={widget.tooltip ? fillText(widget.tooltip, state.values) : undefined}>{<div>{control}</div>}</Tip>
}

type MenuItem = { label?: unknown; do?: unknown; icon?: unknown }

/** Кнопка с выпадающим списком действий. */
export function MenuWidget({ widget }: { widget: Widget }) {
  const state = usePanel()
  const items = Array.isArray(widget.items) ? (widget.items as MenuItem[]) : []
  const icon = str(widget.icon)
  const label = str(widget.label)
  const name = widget.tooltip ? fillText(widget.tooltip, state.values) : (label ?? "menu")

  return (
    <DropdownMenu>
      <Tip text={name}>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size={label ? "sm" : "icon-sm"} aria-label={name}>
            {icon && <Icon name={icon} />}
            {label}
          </Button>
        </DropdownMenuTrigger>
      </Tip>
      <DropdownMenuContent side="top" align="end">
        {items.map((item, i) => {
          const action = str(item.do)
          const itemIcon = str(item.icon)
          if (!action) return null
          return (
            <DropdownMenuItem key={i} onSelect={() => send({ type: "do", action })}>
              {itemIcon && <Icon name={itemIcon} />}
              {fillText(str(item.label) ?? action, state.values)}
            </DropdownMenuItem>
          )
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
