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
const modes: { id: OMode; label: string; short: string; description: string }[] = [
  { id: "auto", label: "Auto", short: "Auto", description: "Let the Orchestrator choose the strategy" },
  { id: "cot", label: "Chain-of-Thought", short: "CoT", description: "Structured reasoning mode" },
  { id: "react", label: "ReAct", short: "ReAct", description: "Reason and act through tools" },
  { id: "tot", label: "Tree-of-Thought", short: "ToT", description: "Explore competing paths" },
  { id: "got", label: "Graph-of-Thought", short: "GoT", description: "Connect and evaluate ideas" },
  { id: "plan", label: "Plan & Execute", short: "Plan", description: "Plan first, then execute" },
  { id: "verify", label: "Verify", short: "Verify", description: "Prioritize evidence and checks" },
  { id: "explore", label: "Explore", short: "Explore", description: "Broaden the solution space" },
];

function phaseState(current: Phase, target: Phase) {
  const order: Phase[] = ["Planning", "Checking", "Reflecting", "Sealed"];
  const normalized = current === "CycleComplete" ? "Reflecting" : current;
  const c = order.indexOf(normalized);
  const t = order.indexOf(target);
  if (current === "Error") return "error";
  if (t < c || current === "Sealed") return "done";
  if (t === c) return "active";
  return "pending";
}

const navItems = [
  ["⌂", "New chat", "new"],
  ["⌕", "Search", "search"],
  ["◈", "Projects", "projects"],
  ["▣", "Skills", "skills"],
  ["◇", "Trails", "trails"],
  ["◫", "Platform", "platform"],
] as const;

export default function LlmHome({ trailsByDomain }: { trailsByDomain: Record<string, Trail[]> }) {
  const { agent } = useAgent({ agentId: "Orchestrator" });
  const state = agent.state as CycleState | undefined;
  const [mode, setMode] = useState<OMode>("auto");
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [inspectorOpen, setInspectorOpen] = useState(true);
  const [modeOpen, setModeOpen] = useState(false);
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

  const recentTrails = useMemo(() => Object.values(trailsByDomain).flat().slice(0, 5), [trailsByDomain]);
  const hasCycle = Boolean(state?.phase);
  const active = hasCycle && state?.phase !== "Sealed" && state?.phase !== "Error";
  const labels = useMemo(() => ({
    chatInputPlaceholder: `Message PMCRO · ${selectedMode.label}…`,
    welcomeMessageText: `I am the PMCR-O Orchestrator. ${selectedMode.description}. Give me a goal and I will coordinate the governed cycle.`,
  }), [selectedMode]);

  function changeMode(next: OMode) {
    setMode(next);
    setModeOpen(false);
    const params = new URLSearchParams(searchParams.toString());
    params.set("mode", next);
    router.replace(`/?${params.toString()}`, { scroll: false });
  }

  function newChat() {
    router.replace("/", { scroll: false });
    router.refresh();
  }

  return (
    <main className={styles.workspace}>
      <aside className={`${styles.sidebar} ${sidebarOpen ? "" : styles.sidebarCollapsed}`}>
        <div className={styles.sidebarTop}>
          <button className={styles.brand} onClick={() => router.push("/")} aria-label="PMCR-O home">
            <span className={styles.brandMark}>P</span>
            {sidebarOpen && <span><strong>PMCR-O</strong><small>AI AGENT COMPANY</small></span>}
          </button>
          <button className={styles.iconButton} onClick={() => setSidebarOpen(v => !v)} aria-label="Toggle sidebar">{sidebarOpen ? "‹" : "›"}</button>
        </div>

        <button className={styles.newChat} onClick={newChat}><span>＋</span>{sidebarOpen && "New chat"}</button>

        <nav className={styles.nav} aria-label="Workspace">
          {navItems.map(([icon, label, route]) => (
            <button key={route} className={`${styles.navItem} ${route === "new" ? styles.navActive : ""}`} onClick={() => route === "new" ? newChat() : router.push(`/${route === "search" ? "" : route}`)}>
              <span className={styles.navIcon}>{icon}</span>{sidebarOpen && <span>{label}</span>}
            </button>
          ))}
        </nav>

        {sidebarOpen && <div className={styles.sidebarSection}>
          <div className={styles.sectionHeader}><span>RECENT TRAILS</span><button onClick={() => router.push("/trails")}>View</button></div>
          {recentTrails.slice(0, 4).map((trail, index) => (
            <button className={styles.trailRow} key={`${trail.id ?? "trail"}-${index}`} onClick={() => router.push("/trails")}>
              <span className={styles.trailDot} />
              <span className={styles.trailText}>{trail.id ?? "Trail"}</span>
            </button>
          ))}
        </div>}

        <div className={styles.sidebarBottom}>
          <button className={styles.navItem}><span className={styles.navIcon}>⚙</span>{sidebarOpen && <span>Settings</span>}</button>
          <div className={styles.profile}>{sidebarOpen && <><span className={styles.avatar}>S</span><span><strong>Workspace</strong><small>Local Colony</small></span></>}</div>
        </div>
      </aside>

      <section className={styles.mainArea}>
        <header className={styles.topbar}>
          <div className={styles.breadcrumb}><span>Colony</span><b>/</b><strong>Orchestrator</strong></div>
          <div className={styles.topbarActions}>
            <div className={styles.modePicker}>
              <button className={styles.modeButton} onClick={() => setModeOpen(v => !v)} aria-expanded={modeOpen}>
                <span className={styles.modeIndicator} />
                <span>{selectedMode.short}</span>
                <span className={styles.chevron}>⌄</span>
              </button>
              {modeOpen && <div className={styles.modeMenu}>
                <div className={styles.modeMenuTitle}>O-MODE</div>
                {modes.map(item => <button key={item.id} className={mode === item.id ? styles.modeOptionActive : styles.modeOption} onClick={() => changeMode(item.id)}><span><strong>{item.label}</strong><small>{item.description}</small></span>{mode === item.id && <b>✓</b>}</button>)}
              </div>}
            </div>
            <span className={styles.liveStatus}><i />{active ? "Running" : "Ready"}</span>
            <button className={styles.iconButton} onClick={() => setInspectorOpen(v => !v)} aria-label="Toggle execution inspector">◧</button>
          </div>
        </header>

        <div className={`${styles.canvas} ${inspectorOpen ? "" : styles.canvasWide}`}>
          <div className={styles.conversation}>
            <div className={styles.welcome}>
              <span className={styles.kicker}>PMCR-O AI AGENT COMPANY</span>
              <h1>What do you want to accomplish?</h1>
              <p>Start with a messy goal. The Orchestrator turns it into governed, evidence-backed execution.</p>
            </div>
            <div className={styles.chatShell}>
              <div className={styles.chatIdentity}><span className={styles.agentAvatar}>O</span><div><strong>Orchestrator</strong><small>I AM: Orchestrator · O-Mode: {selectedMode.label}</small></div><span className={styles.contextBadge}>Governed</span></div>
              <div className={styles.chat}><CopilotChat labels={labels} className={styles.copilotChat} /></div>
            </div>
          </div>

          {inspectorOpen && <aside className={styles.inspector}>
            <div className={styles.inspectorHeader}><div><span>EXECUTION</span><strong>{active ? `Cycle ${state?.cycle ?? 1}` : "Ready"}</strong></div><span className={styles.liveBadge}>{active ? "LIVE" : "IDLE"}</span></div>
            <div className={styles.phaseList}>
              {(["Planning", "Checking", "Reflecting", "Sealed"] as Phase[]).map((phase, index) => {
                const status = hasCycle ? phaseState(state!.phase, phase) : "pending";
                const label = phase === "Planning" ? "Planner" : phase === "Checking" ? "Checker" : phase === "Reflecting" ? "Reflector" : "Trail";
                const statusText = !hasCycle ? "Waiting" : status === "active" ? "In progress" : status === "done" ? "Complete" : status === "error" ? "Error" : "Waiting";
                return <div key={phase} className={styles.phaseRow}><span className={`${styles.phaseNode} ${styles[status]}`}>{status === "done" ? "✓" : index + 1}</span><div><strong>{label}</strong><small>{statusText}</small></div></div>;
              })}
            </div>
            <div className={styles.inspectorDivider} />
            <div className={styles.inspectorMeta}><span>O-MODE</span><strong>{selectedMode.label}</strong></div>
            <div className={styles.inspectorMeta}><span>TRAIL</span><strong>{state?.trailId ?? "Not started"}</strong></div>
            <div className={styles.inspectorMeta}><span>LAST ACTION</span><strong>{state?.lastAction ?? "Waiting for intent"}</strong></div>
            <div className={styles.inspectorMeta}><span>DISPOSITION</span><strong className={state?.disposition === "Accept" ? styles.pass : ""}>{state?.disposition ?? "—"}</strong></div>
            <button className={styles.openTrails} onClick={() => router.push("/trails")}>Open trail history <span>→</span></button>
          </aside>}
        </div>
      </section>
    </main>
  );
}
