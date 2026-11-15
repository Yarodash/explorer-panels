// Выгружает список иконок lucide в два места:
//   src/core/Resources/lucide-icons.txt  — имена (kebab-case), по ним ядро проверяет "icon" в panel.json;
//   src/ui/src/generated/icon-map.json    — имя -> компонент, по нему страница находит иконку.
// Запускать после обновления lucide-react:  node scripts/export-icons.mjs

import { mkdirSync, writeFileSync } from "node:fs"
import { dirname, resolve } from "node:path"
import { fileURLToPath } from "node:url"

import dynamicIconImports from "lucide-react/dynamicIconImports"
import { icons } from "lucide-react"

const here = dirname(fileURLToPath(import.meta.url))
const map = {}
for (const name of Object.keys(dynamicIconImports).sort()) {
  // у псевдонима (например, "edit") displayName — имя настоящей иконки ("Pen")
  const pascal = (await dynamicIconImports[name]()).default.displayName
  if (pascal && icons[pascal]) map[name] = pascal
}

const names = Object.keys(map)
const coreFile = resolve(here, "../../core/Resources/lucide-icons.txt")
const uiFile = resolve(here, "../src/generated/icon-map.json")
mkdirSync(dirname(coreFile), { recursive: true })
mkdirSync(dirname(uiFile), { recursive: true })
writeFileSync(coreFile, names.join("\n") + "\n")
writeFileSync(uiFile, JSON.stringify(map))
console.log(`exported ${names.length} icon names`)
