import { StrictMode } from "react"
import { createRoot } from "react-dom/client"

import "./index.css"
import App from "./App.tsx"
import { startFocusReporting } from "./focus.ts"
import { startRegionReporting } from "./region.ts"
import { ThemeProvider } from "@/components/theme-provider.tsx"

startRegionReporting()
startFocusReporting()

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <App />
    </ThemeProvider>
  </StrictMode>
)
