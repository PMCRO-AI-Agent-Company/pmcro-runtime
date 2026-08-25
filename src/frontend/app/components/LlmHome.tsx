"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useAgent, CopilotChat } from "@copilotkit/react-core/v2";
import { useRouter, useSearchParams } from "next/navigation";
import type { Trail } from "./TrailView";
import styles from "./LlmHome.module.css";

type Phase = "Planning" | "Checking" | "Reflecting" | "CycleComplete" | "Sealed" | "Error";
type CycleState = {
  trailId: string;
  cycle: number;
  phase: Phase;
  lastAction?: string | null;
  disposition?: string | null;
  allPassed?: boolean | null;
};

type OMode = "auto" | "cot" | "react" | "tot" | "got" | "plan" | "verify" | "explore";
const modes: { id: OMode; label: string; description: string }[] = [
  { id: "auto", label: "Auto", description: "Let the Orchestrator choose the strategy" },
  { id: "cot", label: "Chain-of-Thought", description: "Structured reasoning mode" },
  { id: "react", label: "ReAct", description: "Reason and act through tools" },
  { id: "tot", label: "Tree-of-Thought", description: "Explore competing paths" },
  { id: "got", label: "Graph-of-Thought", description: "Connect and evaluate ideas" },
  { id: "plan", label: "Plan & Execute", description: "Plan first, then execute" },
  { id: "verify", label: "Verify", description: "Prioritize evidence and checks" },
  { id: "explore", label: "Explore", description: "Broaden the solution space" },
];

function phaseState(current: Phase, target: Phase) {
  const order: Phase[] = ["Planning", "Checking", "Reflecting", "Sealed"];
  const c = order.indexOf(current === "CycleComplete" ? "Reflecting" : current);
  const t = order.indexOf(target);
  if (current === "Error") return "error";
  if (t < c || current === "Sealed") return "done";
  if (t === c) return "active";
  return "pending";
}

export default function LlmHome({ trailsByDomain }: { trailsByDomain: Record<string, Trail[]> }) {
  const { agent } = useAgent({ agentId: "Orchestrator" });
  const state = agent.state as CycleState | undefined;
  const [mode, setMode] = useState<OMode>("auto");
  const [showDetails, setShowDetails] = useState(false);
  const lastPhase = useRef<Phase | undefined>(undefined);
  const router = useRouter();
  const searchParams = useSearchParams();
  const selectedMode = modes.find((item) => item.id === mode) ?? modes[0];

  useEffect(() => {
    const value = searchParams.get("mode");
    if (modes.some((m) => m.id === value)) setMode(value as OMode);
  }, [searchParams]);

  useEffect(() => {
    if (state?.phase === "Sealed" && lastPhase.current !== "Sealed") router.refresh();
    lastPhase.current = state?.phase;
  }, [state?.phase, router]);

  const recentTrails = useMemo(() => Object.values(trailsByDomain).flat().slice(0, 3), [trailsByDomain]);
  const active = !!state?.phase && state.phase !== "Sealed" && state.phase !== "Error";
  const labels = useMemo(() => ({
    chatInputPlaceholder: `Message PMCRO · ${selectedMode.label}…`,
    welcomeMessageText: `I am the Orchestrator. O-Mode: ${selectedMode.label}. ${selectedMode.description}. Give me a goal and I will coordinate the governed cycle.`,
  }), [selectedMode]);

  function changeMode(next: OMode) {
    setMode(next);
    const params = new URLSearchParams(searchParams.toString());
    params.set("mode", next);
    router.replace(`/?${params.toString()}`, { scroll: false });
  }

  return (
    <main className={styles.shell}>
      <header className={styles.header}>
        <div className={styles.brand}>PMCRO</div>
        <div className={styles.headerMeta}>
          <span className={styles.statusDot} />
          {active ? "Orchestrating" : "Ready"}
        </div>
        <button className={styles.newButton} onClick={() => router.refresh()}>New chat</button>
      </header>

      <section className={styles.content}>
        <div className={styles.hero}>
          <div className={styles.eyebrow}>PMCR-O AI AGENT COMPANY</div>
          <h1>What do you want to accomplish?</h1>
          <p>Start with a messy goal. The Orchestrator turns it into a governed, evidence-backed execution.</p>
        </div>

        <div className={styles.chatCard}>
          <div className={styles.chatHeader}>
            <div>
              <strong>Orchestrator</strong>
              <span>I AM: Orchestrator · O-Mode: {selectedMode.label}</span>
            </div>
            <div className={styles.modeGroup} aria-label="O-Mode">
              {modes.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  title={item.description}
                  aria-pressed={mode === item.id}
                  className={mode === item.id ? styles.modeActive : styles.mode}
                  onClick={() => changeMode(item.id)}
                >
                  {item.label}
                </button>
              ))}
            </div>
          </div>
          <div className={styles.chat}>
            <CopilotChat labels={labels} className={styles.copilotChat} />
          </div>
        </div>

        <section className={styles.execution}>
          <div className={styles.executionTop}>
            <div>
              <span className={styles.sectionLabel}>LIVE ORCHESTRATION</span>
              <h2>{active ? `Cycle ${state?.cycle ?? 1}` : "Ready for a goal"}</h2>
            </div>
            <button className={styles.details} onClick={() => setShowDetails((v) => !v)}>{showDetails ? "Hide details" : "Execution details"}</button>
          </div>

          <div className={styles.rail}>
            {(["Planning", "Checking", "Reflecting", "Sealed"] as Phase[]).map((phase, index) => (
              <div key={phase} className={styles.stepWrap}>
                <div className={`${styles.step} ${styles[phaseState(state?.phase ?? "Planning", phase)]}`}>
                  <div className={styles.node}>{phaseState(state?.phase ?? "Planning", phase) === "done" ? "✓" : index + 1}</div>
                  <span>{phase === "Planning" ? "Planner" : phase === "Checking" ? "Checker" : phase === "Reflecting" ? "Reflector" : "Trail"}</span>
                </div>
                {index < 3 && <div className={styles.connector} />}
              </div>
            ))}
          </div>

          {showDetails && (
            <div className={styles.detailsPanel}>
              <div><span>O-Mode</span><strong>{selectedMode.label}</strong></div>
              <div><span>Trail</span><strong>{state?.trailId ?? "Not started"}</strong></div>
              <div><span>Last action</span><strong>{state?.lastAction ?? "Waiting for intent"}</strong></div>
              <div><span>Disposition</span><strong>{state?.disposition ?? "—"}</strong></div>
            </div>
          )}
        </section>

        {recentTrails.length > 0 && (
          <section className={styles.recent}>
            <span className={styles.sectionLabel}>RECENT TRAILS</span>
            <div className={styles.trailList}>
              {recentTrails.map((trail, index) => <div className={styles.trailItem} key={`${trail.id ?? "trail"}-${index}`}><span>{trail.id ?? "Trail"}</span><span>Open ›</span></div>)}
            </div>
          </section>
        )}
      </section>
    </main>
  );
}
