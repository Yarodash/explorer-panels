// Кнопки Claude. Они есть в любой папке, а не только там, где лежит panel.json; включаются и выключаются
// в меню значка в трее. Левая открывает Claude в папке, которая сейчас показана в Проводнике,
// правая — продолжает в ней последний разговор (claude --continue).

import { siClaude } from "simple-icons"

import { send } from "@/bridge"
import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"
import { Tip, useStrings } from "@/context"

const ORANGE = `#${siClaude.hex}` // фирменный цвет значка
const RED = "#E5341F" // «продолжить»: тот же значок, но краснее

function ClaudeIcon({ color }: { color: string }) {
  return (
    <svg viewBox="0 0 24 24" fill={color} aria-hidden="true" className="size-4.5">
      <path d={siClaude.path} />
    </svg>
  )
}

/** @param separated отделить кнопки чертой от того, что стоит слева от них */
export function ClaudeButtons({ separated }: { separated?: boolean }) {
  const t = useStrings()
  return (
    <span data-claude className="flex shrink-0 items-center gap-1">
      {separated && <Separator orientation="vertical" className="mx-1 h-5!" />}
      <Tip text={t.claudeOpen}>
        <Button variant="ghost" size="icon" aria-label={t.claudeOpen} onClick={() => send({ type: "claude", resume: false })}>
          <ClaudeIcon color={ORANGE} />
        </Button>
      </Tip>
      <Tip text={t.claudeContinue}>
        <Button variant="ghost" size="icon" aria-label={t.claudeContinue} onClick={() => send({ type: "claude", resume: true })}>
          <ClaudeIcon color={RED} />
        </Button>
      </Tip>
    </span>
  )
}
