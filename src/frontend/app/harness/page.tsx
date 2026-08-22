// src/frontend/app/harness/page.tsx
//
// ARCH-OMODE-MERGE-001 (2026-08-22): /harness retired as a standalone route
// -- it's now the "Read-Only" O-Mode on the single Console screen (see
// ConsoleView.tsx's OModeToggle, ?mode=readonly). This file used to render
// HarnessView + its own CopilotChat; that component is now dead code (only
// this file imported it -- confirmed via content search across app/ before
// deleting the import here). Left in place, unimported, rather than
// deleted outright, in case anything in it needs to be salvaged later.
//
// redirect() throws internally (Next.js's control-flow mechanism), so this
// intentionally has no return/JSX below it -- old bookmarks and links to
// /harness land straight on the merged Console in Read-Only mode instead
// of a dead or half-working page.
import { redirect } from "next/navigation";

export default function HarnessPage() {
  redirect("/?mode=readonly");
}
