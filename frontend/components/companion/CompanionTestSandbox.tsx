'use client';

/**
 * Pre-reporting Live Dictation & Voice Command Test Sandbox.
 *
 * Provides practice clinical sections (Findings & Impression) registered with
 * the section editor registry so radiologists can test their mobile companion
 * microphone, on-device speech-to-text, push-to-talk (PTT), and remote
 * navigation commands in a safe, interactive practice space before opening live
 * patient reports.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  registerSectionEditor,
  unregisterSectionEditor,
  noteSectionEditorFocus,
  type SectionEditorHandle,
} from '@/lib/editor/sectionEditorRegistry';
import { useCompanion } from './CompanionContext';
import {
  Mic,
  Volume2,
  CornerDownLeft,
  RotateCcw,
  Sparkles,
  ArrowRight,
  CheckCircle2,
  Trash2,
  Command,
  Radio,
} from 'lucide-react';

interface SectionState {
  text: string;
  interim: string;
}

export default function CompanionTestSandbox() {
  const {
    phase,
    link,
    companionDeviceName,
    phoneListening,
    transcribing,
    slowTranscribe,
    lastCommand,
    lastTranscript,
  } = useCompanion();

  const [findings, setFindings] = useState<SectionState>({ text: '', interim: '' });
  const [impression, setImpression] = useState<SectionState>({ text: '', interim: '' });
  const [activeSection, setActiveSection] = useState<'findings' | 'impression'>('findings');
  const [commandHighlight, setCommandHighlight] = useState<string | null>(null);

  const findingsRef = useRef<HTMLTextAreaElement | null>(null);
  const impressionRef = useRef<HTMLTextAreaElement | null>(null);
  const findingsStateRef = useRef(findings);
  const impressionStateRef = useRef(impression);
  findingsStateRef.current = findings;
  impressionStateRef.current = impression;

  // Highlight commands visually when received
  useEffect(() => {
    if (!lastCommand) return;
    setCommandHighlight(lastCommand);
    const t = setTimeout(() => setCommandHighlight(null), 1800);
    return () => clearTimeout(t);
  }, [lastCommand]);

  // Register findings handle
  useEffect(() => {
    const handle: SectionEditorHandle = {
      sectionKey: 'findings',
      insertAtCursor: (text: string) => {
        setFindings((prev) => {
          const current = prev.text;
          const space = current && !current.endsWith(' ') && !current.endsWith('\n') ? ' ' : '';
          return { text: `${current}${space}${text}`, interim: '' };
        });
      },
      focus: () => {
        setActiveSection('findings');
        findingsRef.current?.focus();
        noteSectionEditorFocus('findings');
      },
      setInterim: (text: string) => {
        setFindings((prev) => ({ ...prev, interim: text }));
      },
      clearInterim: () => {
        setFindings((prev) => ({ ...prev, interim: '' }));
      },
      newLine: () => {
        setFindings((prev) => ({ text: `${prev.text}\n`, interim: '' }));
      },
      undo: () => {
        setFindings((prev) => {
          const words = prev.text.trimEnd().split(/\s+/);
          words.pop();
          return { text: words.join(' '), interim: '' };
        });
      },
    };

    registerSectionEditor(handle);
    // Mark as initially focused by default
    noteSectionEditorFocus('findings');

    return () => {
      unregisterSectionEditor('findings');
    };
  }, []);

  // Register impression handle
  useEffect(() => {
    const handle: SectionEditorHandle = {
      sectionKey: 'impression',
      insertAtCursor: (text: string) => {
        setImpression((prev) => {
          const current = prev.text;
          const space = current && !current.endsWith(' ') && !current.endsWith('\n') ? ' ' : '';
          return { text: `${current}${space}${text}`, interim: '' };
        });
      },
      focus: () => {
        setActiveSection('impression');
        impressionRef.current?.focus();
        noteSectionEditorFocus('impression');
      },
      setInterim: (text: string) => {
        setImpression((prev) => ({ ...prev, interim: text }));
      },
      clearInterim: () => {
        setImpression((prev) => ({ ...prev, interim: '' }));
      },
      newLine: () => {
        setImpression((prev) => ({ text: `${prev.text}\n`, interim: '' }));
      },
      undo: () => {
        setImpression((prev) => {
          const words = prev.text.trimEnd().split(/\s+/);
          words.pop();
          return { text: words.join(' '), interim: '' };
        });
      },
    };

    registerSectionEditor(handle);
    return () => {
      unregisterSectionEditor('impression');
    };
  }, []);

  const clearAll = useCallback(() => {
    setFindings({ text: '', interim: '' });
    setImpression({ text: '', interim: '' });
  }, []);

  const sampleClinicalPrompt = (sampleText: string) => {
    if (activeSection === 'findings') {
      setFindings((prev) => ({ ...prev, text: sampleText }));
    } else {
      setImpression((prev) => ({ ...prev, text: sampleText }));
    }
  };

  const isPaired = phase === 'paired';

  return (
    <section
      className="rp-panel rp-sandbox-panel"
      aria-label="Pre-reporting live dictation test sandbox"
    >
      <div className="flex items-center justify-between gap-3 border-b border-border/40 pb-3 mb-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="flex h-6 w-6 items-center justify-center rounded-md bg-accent/15 text-accent">
              <Radio size={14} className={phoneListening ? 'animate-pulse' : ''} />
            </span>
            <h2 className="text-base font-semibold text-foreground">Pre-reporting test sandbox</h2>
            {isPaired && (
              <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-600 dark:text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded-full">
                <CheckCircle2 size={12} /> Paired: {companionDeviceName || 'Mobile device'}
              </span>
            )}
          </div>
          <p className="text-xs text-muted-foreground mt-0.5">
            Test voice dictation, microphone sensitivity, and remote commands before opening patient studies.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            className="ghost text-xs px-2.5 py-1.5 inline-flex items-center gap-1.5"
            onClick={clearAll}
            aria-label="Clear practice text"
          >
            <Trash2 size={13} />
            <span>Clear practice text</span>
          </button>
        </div>
      </div>

      {/* Live Audio & Streaming Monitor Banner */}
      <div className="mb-4 rounded-lg bg-surface-muted/50 border border-border/60 p-3 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div
            className={`flex h-8 w-8 items-center justify-center rounded-full transition-colors ${
              phoneListening
                ? 'bg-rose-500 text-white animate-pulse shadow-md shadow-rose-500/30'
                : transcribing
                ? 'bg-amber-500 text-white animate-pulse'
                : 'bg-muted text-muted-foreground'
            }`}
          >
            <Mic size={16} />
          </div>

          <div>
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold text-foreground">
                {phoneListening
                  ? 'Phone mic active — listening…'
                  : transcribing
                  ? slowTranscribe
                    ? 'Speech engine warming up on-device…'
                    : 'Transcribing speech…'
                  : isPaired
                  ? 'Microphone ready — press and hold mic on your phone'
                  : 'Pair your device above to start testing'}
              </span>
              {phoneListening && (
                <span className="flex h-2 w-2 rounded-full bg-rose-500 animate-ping" />
              )}
            </div>
            <div className="text-[11px] text-muted-foreground">
              {link === 'connected'
                ? 'Direct LAN connection active (0 cloud audio)'
                : link === 'connecting'
                ? 'Negotiating peer link…'
                : 'Waiting for paired audio stream'}
            </div>
          </div>
        </div>

        {lastTranscript && (
          <div className="text-xs text-muted-foreground max-w-sm truncate bg-surface px-2.5 py-1 rounded border border-border/40">
            <span className="font-medium text-foreground">Last phrase:</span> “{lastTranscript}”
          </div>
        )}
      </div>

      {/* Two Practice Report Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
        {/* Practice Findings */}
        <div
          className={`relative rounded-lg border p-3 transition-all ${
            activeSection === 'findings'
              ? 'border-accent ring-1 ring-accent/30 bg-surface'
              : 'border-border/60 bg-surface-muted/20 hover:border-border'
          }`}
          onClick={() => {
            setActiveSection('findings');
            findingsRef.current?.focus();
            noteSectionEditorFocus('findings');
          }}
        >
          <div className="flex items-center justify-between mb-2">
            <label
              htmlFor="sandbox-findings"
              className="text-xs font-bold uppercase tracking-wider text-muted-foreground cursor-pointer"
            >
              Practice Findings
            </label>
            {activeSection === 'findings' && (
              <span className="text-[10px] font-medium text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                Focused target
              </span>
            )}
          </div>

          <textarea
            id="sandbox-findings"
            ref={findingsRef}
            className="w-full h-32 text-xs leading-relaxed bg-transparent resize-none outline-none border-0 text-foreground placeholder:text-muted-foreground/60 font-mono"
            placeholder="Dictate or type sample findings here (e.g. 'Heart size is normal. The lungs are clear bilaterally with no focal consolidation, pneumothorax, or pleural effusion.')"
            value={findings.text}
            onChange={(e) => setFindings({ ...findings, text: e.target.value })}
            onFocus={() => {
              setActiveSection('findings');
              noteSectionEditorFocus('findings');
            }}
          />

          {findings.interim && (
            <div className="mt-1 text-xs text-accent italic font-mono bg-accent/5 p-1 rounded border border-accent/20">
              <span className="text-[10px] font-bold uppercase tracking-wider text-accent/80 mr-1.5">Live interim:</span>
              {findings.interim}
            </div>
          )}
        </div>

        {/* Practice Impression */}
        <div
          className={`relative rounded-lg border p-3 transition-all ${
            activeSection === 'impression'
              ? 'border-accent ring-1 ring-accent/30 bg-surface'
              : 'border-border/60 bg-surface-muted/20 hover:border-border'
          }`}
          onClick={() => {
            setActiveSection('impression');
            impressionRef.current?.focus();
            noteSectionEditorFocus('impression');
          }}
        >
          <div className="flex items-center justify-between mb-2">
            <label
              htmlFor="sandbox-impression"
              className="text-xs font-bold uppercase tracking-wider text-muted-foreground cursor-pointer"
            >
              Practice Impression
            </label>
            {activeSection === 'impression' && (
              <span className="text-[10px] font-medium text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                Focused target
              </span>
            )}
          </div>

          <textarea
            id="sandbox-impression"
            ref={impressionRef}
            className="w-full h-32 text-xs leading-relaxed bg-transparent resize-none outline-none border-0 text-foreground placeholder:text-muted-foreground/60 font-mono"
            placeholder="Dictate or type sample impression here (e.g. '1. No acute cardiopulmonary abnormality. 2. Stable degenerative spine changes.')"
            value={impression.text}
            onChange={(e) => setImpression({ ...impression, text: e.target.value })}
            onFocus={() => {
              setActiveSection('impression');
              noteSectionEditorFocus('impression');
            }}
          />

          {impression.interim && (
            <div className="mt-1 text-xs text-accent italic font-mono bg-accent/5 p-1 rounded border border-accent/20">
              <span className="text-[10px] font-bold uppercase tracking-wider text-accent/80 mr-1.5">Live interim:</span>
              {impression.interim}
            </div>
          )}
        </div>
      </div>

      {/* Voice Commands Test Feed & Cheatsheet */}
      <div className="rounded-lg border border-border/60 bg-surface-muted/30 p-3">
        <div className="flex items-center justify-between mb-2.5">
          <div className="flex items-center gap-1.5">
            <Command size={14} className="text-muted-foreground" />
            <span className="text-xs font-semibold text-foreground">Voice commands &amp; remote buttons</span>
          </div>
          <span className="text-[11px] text-muted-foreground">
            Tap a button or speak command on your phone
          </span>
        </div>

        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-6 gap-2">
          <div
            className={`flex flex-col items-center justify-center p-2 rounded-md border text-center transition-all ${
              commandHighlight === 'next_section'
                ? 'border-accent bg-accent text-accent-foreground font-semibold scale-105 shadow'
                : 'border-border/60 bg-surface text-foreground'
            }`}
          >
            <ArrowRight size={14} className="mb-1 opacity-70" />
            <span className="text-[11px] font-medium leading-none">Next section</span>
            <span className="text-[9px] text-muted-foreground mt-0.5">“next section”</span>
          </div>

          <div
            className={`flex flex-col items-center justify-center p-2 rounded-md border text-center transition-all ${
              commandHighlight === 'jump_findings'
                ? 'border-accent bg-accent text-accent-foreground font-semibold scale-105 shadow'
                : 'border-border/60 bg-surface text-foreground'
            }`}
          >
            <Volume2 size={14} className="mb-1 opacity-70" />
            <span className="text-[11px] font-medium leading-none">Findings</span>
            <span className="text-[9px] text-muted-foreground mt-0.5">“jump findings”</span>
          </div>

          <div
            className={`flex flex-col items-center justify-center p-2 rounded-md border text-center transition-all ${
              commandHighlight === 'jump_impression'
                ? 'border-accent bg-accent text-accent-foreground font-semibold scale-105 shadow'
                : 'border-border/60 bg-surface text-foreground'
            }`}
          >
            <Sparkles size={14} className="mb-1 opacity-70" />
            <span className="text-[11px] font-medium leading-none">Impression</span>
            <span className="text-[9px] text-muted-foreground mt-0.5">“jump impression”</span>
          </div>

          <div
            className={`flex flex-col items-center justify-center p-2 rounded-md border text-center transition-all ${
              commandHighlight === 'new_line'
                ? 'border-accent bg-accent text-accent-foreground font-semibold scale-105 shadow'
                : 'border-border/60 bg-surface text-foreground'
            }`}
          >
            <CornerDownLeft size={14} className="mb-1 opacity-70" />
            <span className="text-[11px] font-medium leading-none">New line</span>
            <span className="text-[9px] text-muted-foreground mt-0.5">“new line”</span>
          </div>

          <div
            className={`flex flex-col items-center justify-center p-2 rounded-md border text-center transition-all ${
              commandHighlight === 'undo'
                ? 'border-accent bg-accent text-accent-foreground font-semibold scale-105 shadow'
                : 'border-border/60 bg-surface text-foreground'
            }`}
          >
            <RotateCcw size={14} className="mb-1 opacity-70" />
            <span className="text-[11px] font-medium leading-none">Undo</span>
            <span className="text-[9px] text-muted-foreground mt-0.5">“undo last”</span>
          </div>

          <div
            className={`flex flex-col items-center justify-center p-2 rounded-md border text-center transition-all ${
              commandHighlight === 'generate_impression'
                ? 'border-accent bg-accent text-accent-foreground font-semibold scale-105 shadow'
                : 'border-border/60 bg-surface text-foreground'
            }`}
          >
            <Sparkles size={14} className="mb-1 text-accent" />
            <span className="text-[11px] font-medium leading-none">AI Impression</span>
            <span className="text-[9px] text-muted-foreground mt-0.5">Auto-draft</span>
          </div>
        </div>

        <div className="mt-3 flex items-center justify-between pt-2 border-t border-border/40 text-[11px] text-muted-foreground">
          <span>
            Quick test phrases:
          </span>
          <div className="flex gap-2">
            <button
              type="button"
              className="text-accent hover:underline text-[11px]"
              onClick={() =>
                sampleClinicalPrompt(
                  'Cardiomediastinal silhouette is within normal limits. Lungs are clear without focal consolidation, pleural effusion, or pneumothorax. Osseous structures are unremarkable.',
                )
              }
            >
              Insert normal chest findings
            </button>
            <span>•</span>
            <button
              type="button"
              className="text-accent hover:underline text-[11px]"
              onClick={() =>
                sampleClinicalPrompt('No acute cardiopulmonary disease identified.')
              }
            >
              Insert normal impression
            </button>
          </div>
        </div>
      </div>
    </section>
  );
}
