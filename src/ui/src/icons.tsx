// Иконка lucide по имени из panel.json ("play", "git-branch").

import { icons, type LucideProps } from "lucide-react"

// имя (kebab-case) -> имя компонента; создаётся скриптом scripts/export-icons.mjs
import iconMap from "@/generated/icon-map.json"

const map = iconMap as Record<string, string>

export function Icon({ name, ...props }: { name: string } & LucideProps) {
  const Component = icons[map[name] as keyof typeof icons]
  return Component ? <Component {...props} /> : null
}
