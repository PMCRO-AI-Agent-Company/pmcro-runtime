export const metadata = { title: "Platform · PMCR-O" };

export default function PlatformPage() {
  return (
    <main className="product-page" aria-labelledby="platform-title">
      <header className="product-page-header"><p className="workspace-section-kicker">System · Platform</p><h1 id="platform-title">Colony platform</h1><p>Understand the runtime surfaces behind the PMCR-O software factory.</p></header>
      <div className="product-grid">
        <article className="product-card"><span className="workspace-section-kicker">MAF</span><h2>Native Agent Skills</h2><p>Marketplace registry materializes into <code>.pmcro/skills-staging</code>; MAF progressively loads skill instructions and resources.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">AG-UI</span><h2>Agent connection</h2><p>CopilotKit uses the Next.js runtime route, which bridges to Orchestrator and Harness AG-UI services.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">CodeAct</span><h2>Bounded execution</h2><p>Hyperlight/Python provides read-only computation while mutation remains governed by PMCR-O and HIL.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">Observability</span><h2>Evidence and trails</h2><p>Every governed cycle can produce plans, execution evidence, checks, reflection, dispositions, and a sealed trail.</p></article>
      </div>
    </main>
  );
}
