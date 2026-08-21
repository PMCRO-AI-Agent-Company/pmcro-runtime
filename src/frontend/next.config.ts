import type { NextConfig } from "next";

// ARCH-COPILOTKIT-001 (2026-07-11): standalone output makes `next start`
// self-contained (no separate `npm install` step needed in the Aspire-managed
// process) — matches how Aspire.Hosting.JavaScript expects to run Node apps
// as a plain child process rather than through a separate build pipeline.
const nextConfig: NextConfig = {
  output: "standalone",
  reactStrictMode: true,
  // ARCH-A2UI-001 follow-up (2026-07-15): Aspire's Next.js hosting fronts the
  // dev server through a proxied hostname (frontend-<name>.dev.localhost),
  // which differs from the actual origin the dev server binds to -- Next
  // blocks HMR websocket requests from origins it doesn't recognize by
  // default. Without this, the app still works but loses live-reload.
  allowedDevOrigins: ["frontend-projectnmae.dev.localhost"],
};

export default nextConfig;
