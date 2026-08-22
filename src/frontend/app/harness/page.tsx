import HarnessView from "../components/HarnessView";

export const metadata = { title: "Harness · PMCR-O" };

export default function HarnessPage() {
  return (
    <main className="product-page" aria-labelledby="harness-title">
      <HarnessView />
      <div className="product-grid">
        <article className="product-card"><span className="workspace-section-kicker">01</span><h2>Read-only tools</h2><p>Filesystem and terminal inspection without PMCR-O mutation gates.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">02</span><h2>Progressive skills</h2><p>Advertise, load, read resources, and request scripts only when needed.</p></article>
        <article className="product-card"><span className="workspace-section-kicker">03</span><h2>Bounded turns</h2><p>Completion marker and iteration cap prevent runaway harness loops.</p></article>
      </div>
    </main>
  );
}
