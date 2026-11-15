import { StrictMode } from "react"
import { createRoot } from "react-dom/client"

import "./index.css"
import App from "./App.tsx"
import { send } from "./bridge.ts"
import { startRegionReporting } from "./region.ts"
import { ThemeProvider } from "@/components/theme-provider.tsx"

startRegionReporting()

// нажатие в любом месте панели ведёт себя как нажатие на сам Проводник: он выходит на передний план
window.addEventListener("pointerdown", () => send({ type: "activate" }), true)

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <App />
    </ThemeProvider>
  </StrictMode>
)
