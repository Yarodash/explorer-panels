// Встроенные подписи панели. Язык берётся из системы (его сообщает программа-хозяин).
// Подписи из panel.json пишет автор панели — они не переводятся.

import type { Status } from "@/bridge"

export type Strings = {
  collapse: string
  expand: string
  fit: string
  trustQuestion: string
  allow: string
  deny: string
  problems: string
  status: Record<Status, string>
  clock: string
  stopwatchToggle: string
  stopwatchReset: string
  claudeOpen: string
  claudeContinue: string
}

const en: Strings = {
  collapse: "Collapse the panel",
  expand: "Expand the panel",
  fit: "The panel does not fit. Widen the Explorer window",
  trustQuestion: "Allow this folder's panel?",
  allow: "Allow",
  deny: "No",
  problems: "panel.json has errors — run: explorer-panel check",
  status: { stopped: "stopped", starting: "starting…", running: "running", failed: "failed" },
  clock: "Current time",
  stopwatchToggle: "Stopwatch: start / pause",
  stopwatchReset: "Reset the stopwatch",
  claudeOpen: "Open Claude in this folder",
  claudeContinue: "Claude: continue the last conversation in this folder (claude --continue)",
}

const ru: Strings = {
  collapse: "Свернуть панель",
  expand: "Развернуть панель",
  fit: "Панель не помещается. Расширить окно Проводника",
  trustQuestion: "Разрешить панель этой папки?",
  allow: "Разрешить",
  deny: "Нет",
  problems: "В panel.json ошибки — выполните: explorer-panel check",
  status: { stopped: "не запущен", starting: "запускается…", running: "работает", failed: "ошибка" },
  clock: "Текущее время",
  stopwatchToggle: "Секундомер: старт / пауза",
  stopwatchReset: "Сбросить секундомер",
  claudeOpen: "Открыть Claude в этой папке",
  claudeContinue: "Claude: продолжить последний разговор в этой папке (claude --continue)",
}

export const strings: Record<"en" | "ru", Strings> = { en, ru }
