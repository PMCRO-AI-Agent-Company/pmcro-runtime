// src/frontend/app/layout.tsx
import type { Metadata } from "next";
import { Suspense } from "react";
import { Space_Grotesk, IBM_Plex_Sans, IBM_Plex_Mono } from "next/font/google";
import "@copilotkit/react-core/v2/styles.css";
import { CopilotKit } from "@copilotkit/react-core";
import "./globals.css";
import Sidebar from "./components/Sidebar";
import ChatPanel from "./components/ChatPanel";

// ARCH-FRONTEND-TYPE-001 (2026-07-13): Space Grotesk (display) + IBM Plex Sans
// (body) + IBM Plex Mono (evidence/data) replace the earlier system-font
// stack. Chosen for this specific brief -- a single-operator mission-control
// console, not a marketing page -- rather than the Inter/Geist pairing most
// dashboards default to. next/font self-hosts the files at build time: no
// FOUC, no runtime request to Google Fonts.
const spaceGrotesk = Space_Grotesk({
  subsets: ["latin"],
  weight: ["500", "600", "700"],
  variable: "--font-display",
});
const plexSans = IBM_Plex_Sans({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-body",
});
const plexMono = IBM_Plex_Mono({
  subsets: ["latin"],
  weight: ["400", "500"],
  variable: "--font-mono",
});

export const metadata: Metadata = {
  title: "PMCR-O AI Agent Company",
  description: "CopilotKit interface for the PMCR-O AI Agent Company Orchestrator",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html
      lang="en"
      className={`${spaceGrotesk.variable} ${plexSans.variable} ${plexMono.variable}`}
    >
      <body>
        {/* ARCH-COPILOTKIT-001 (2026-07-11): runtimeUrl points at the Next.js
            API route (app/api/copilotkit/route.ts), never at OrchestratorService
            directly — the browser only ever talks to this Next.js server.
            agent="Orchestrator" matches the key registered in that route. */}
        {/* ARCH-IA-SPLIT-001 (2026-07-20): app-shell (Sidebar + ChatPanel)
            promoted from ConsoleView.tsx up to the root layout, so every
            route (/, /directory, /platform) shares the same persistent nav
            rail and assistant panel instead of Console being the only page
            with navigation back out. Individual pages now render only their
            own content into app-main -- no page owns Sidebar or ChatPanel
            itself anymore. */}
        <CopilotKit runtimeUrl="/api/copilotkit" agent="Orchestrator" showDevConsole={false}>
          <div className="app-shell">
            <Sidebar />
            <main className="app-main">{children}</main>
          </div>
          {/* ARCH-OMODE-MERGE-001 (2026-08-22): ChatPanel now reads the
              shared `?mode=` param via useSearchParams(), which Next.js
              requires be wrapped in a Suspense boundary for any route that
              isn't already force-dynamic (e.g. /platform has no `dynamic =
              "force-dynamic"` export and would otherwise fail the
              missing-suspense-with-csr-bailout check at build). Root layout
              is shared by every route, so this boundary here covers all of
              them at once rather than needing one per page. Fallback is
              null -- the sidebar defaults closed (defaultOpen={false}), so
              there's nothing visible to flash in before hydration. */}
          <Suspense fallback={null}>
            <ChatPanel />
          </Suspense>
        </CopilotKit>
      </body>
    </html>
  );
}
