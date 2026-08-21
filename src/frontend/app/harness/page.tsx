export const metadata = { title: "Harness · PMCR-O" };

export default function HarnessPage() {
  return (
    <main className="product-page" aria-labelledby="harness-title">
      <header className="product-page-header">
        <p className="workspace-section-kicker">Runtime · MAF Harness</p>
        <h1 id="harness-title">Harness workspace</h1>
        <p>Use the read-only MAF Harness for multi-turn inspection, progressive skill loading, and guided exploration.</p>
      </header>
      <section className="product-card product-card--accent">
        <span className="activity-status is-ready"><span className="activity-status-dot" /> Harness available</span>
        <h2>Open the Harness assistant</h2>
        <p>Use the assistant button in the lower corner, then choose <strong>Harness</strong>. It connects through the native <code>/agui/harness</code> endpoint.</p>
      </section>
      <div className="product-grid">
        <article className="product-card"><span className="workspace-section-kicker">01</span><h2>Read-only tools</h2><p>Filesystem and terminal inspection without PMCR-O mutation gates.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">02</span><h2>Progressive skills</h2><p>Advertise, load, read resources, and request scripts only when needed.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">03</span><h2>Bounded turns</h2><p>Completion marker and iteration cap prevent runaway harness loops.</p></article>
      </div>
    </main>
  );
}
