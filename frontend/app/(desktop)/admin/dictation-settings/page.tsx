'use client';

import { useState, useEffect } from 'react';
import { api, type DictationSettings } from '@/lib/api';
import Banner from '@/components/ui/Banner';
import { Mic, RefreshCw, Save, Cpu, Cloud, Sliders } from 'lucide-react';

const DEFAULT_PROMPT =
  'You are an expert medical radiology transcription assistant. The speaker is a senior radiologist dictating positive findings...';

export default function AdminDictationSettingsPage() {
  const [activeEngine, setActiveEngine] = useState<string>('medASR-6gram');
  const [ubagModel, setUbagModel] = useState<string>('ubag-gemini-audio');
  const [radiologySystemPrompt, setRadiologySystemPrompt] = useState<string>(DEFAULT_PROMPT);

  const [loading, setLoading] = useState<boolean>(true);
  const [saving, setSaving] = useState<boolean>(false);
  const [saveSuccess, setSaveSuccess] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function loadSettings() {
      try {
        setLoading(true);
        const data = await api.dictationSettings.get();
        if (data) {
          if (data.activeEngine) setActiveEngine(data.activeEngine);
          if (data.ubagModel) setUbagModel(data.ubagModel);
          if (data.radiologySystemPrompt) setRadiologySystemPrompt(data.radiologySystemPrompt);
        }
      } catch (err) {
        // Fallback to defaults on error / offline mode
        setError('Failed to load remote dictation settings. Using default configurations.');
      } finally {
        setLoading(false);
      }
    }
    loadSettings();
  }, []);

  async function handleSave() {
    setSaving(true);
    setSaveSuccess(false);
    setError(null);
    try {
      const payload: DictationSettings = {
        activeEngine,
        ubagModel,
        radiologySystemPrompt,
      };
      await api.dictationSettings.update(payload);
      setSaveSuccess(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to save dictation settings.';
      setError(message);
    } finally {
      setSaving(false);
    }
  }

  function handleResetPrompt() {
    setRadiologySystemPrompt(DEFAULT_PROMPT);
  }

  if (loading) {
    return (
      <div className="rp-container" data-testid="dictation-settings-page">
        <div className="rp-panel rp-mt-md" style={{ padding: '32px', textAlign: 'center' }}>
          <span className="rp-spinner lg" aria-hidden="true" />
          <p className="rp-page-sub rp-mt-sm">Loading Dictation & Audio AI Settings...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="rp-container" data-testid="dictation-settings-page">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Mic size={24} className="text-accent" />
            <h1 className="rp-page-title">Dictation & Audio AI Settings</h1>
          </div>
          <p className="rp-page-sub">
            Configure system-wide speech-to-text engines, UBAG audio providers, and radiology transcription prompts.
          </p>
        </div>
        <div className="rp-toolbar">
          <button
            className="primary"
            onClick={handleSave}
            disabled={saving}
            data-testid="save-settings-btn"
            style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          >
            {saving ? <span className="rp-spinner sm" aria-hidden="true" /> : <Save size={16} />}
            {saving ? 'Saving...' : 'Save Settings'}
          </button>
        </div>
      </header>

      {error && (
        <div className="rp-mt-sm">
          <Banner tone="warn" onDismiss={() => setError(null)}>
            {error}
          </Banner>
        </div>
      )}

      {saveSuccess && (
        <div className="rp-mt-sm" data-testid="save-success-banner">
          <Banner tone="success" onDismiss={() => setSaveSuccess(false)}>
            Dictation and Audio AI settings saved successfully.
          </Banner>
        </div>
      )}

      <div className="rp-page-grid rp-mt-md">
        <div className="rp-page-main" style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
          {/* Engine Selector Panel */}
          <div className="rp-panel">
            <div className="rp-panel-title" style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
              <Cpu size={18} className="text-accent" />
              Primary Transcription Engine
            </div>
            <p className="rp-page-sub" style={{ marginBottom: '16px' }}>
              Select the default speech-to-text recognition model for all radiologist dictations.
            </p>

            <div role="radiogroup" aria-label="Transcription Engine" style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
              <label
                className={`rp-card ${activeEngine === 'medASR-6gram' ? 'bg-accent-soft border-accent' : ''}`}
                style={{ cursor: 'pointer', padding: '14px', display: 'flex', alignItems: 'flex-start', gap: '12px' }}
                data-testid="engine-option-medasr"
              >
                <input
                  type="radio"
                  name="activeEngine"
                  value="medASR-6gram"
                  checked={activeEngine === 'medASR-6gram'}
                  onChange={(e) => setActiveEngine(e.target.value)}
                  style={{ marginTop: '3px' }}
                />
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}>
                    Local medASR + 6-gram Model (4.4% WER)
                    <span className="rp-badge success" style={{ fontSize: '11px' }}>Default & Recommended</span>
                  </div>
                  <p className="rp-page-sub" style={{ fontSize: '13px', marginTop: '4px' }}>
                    On-device medical speech-to-text optimized for radiology vocabulary. Zero network latency and total PHI privacy.
                  </p>
                </div>
              </label>

              <label
                className={`rp-card ${activeEngine === 'ubag-cloud' ? 'bg-accent-soft border-accent' : ''}`}
                style={{ cursor: 'pointer', padding: '14px', display: 'flex', alignItems: 'flex-start', gap: '12px' }}
                data-testid="engine-option-ubag-cloud"
              >
                <input
                  type="radio"
                  name="activeEngine"
                  value="ubag-cloud"
                  checked={activeEngine === 'ubag-cloud'}
                  onChange={(e) => setActiveEngine(e.target.value)}
                  style={{ marginTop: '3px' }}
                />
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}>
                    UBAG Cloud Provider
                    <span className="rp-badge info" style={{ fontSize: '11px' }}>Cloud AI</span>
                  </div>
                  <p className="rp-page-sub" style={{ fontSize: '13px', marginTop: '4px' }}>
                    Universal Browser AI Gateway. Routes audio dictations to specialized multimodality cloud audio models.
                  </p>
                </div>
              </label>

              <label
                className={`rp-card ${activeEngine === 'openai-direct' ? 'bg-accent-soft border-accent' : ''}`}
                style={{ cursor: 'pointer', padding: '14px', display: 'flex', alignItems: 'flex-start', gap: '12px' }}
                data-testid="engine-option-openai-direct"
              >
                <input
                  type="radio"
                  name="activeEngine"
                  value="openai-direct"
                  checked={activeEngine === 'openai-direct'}
                  onChange={(e) => setActiveEngine(e.target.value)}
                  style={{ marginTop: '3px' }}
                />
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}>
                    OpenAI Direct
                  </div>
                  <p className="rp-page-sub" style={{ fontSize: '13px', marginTop: '4px' }}>
                    Direct Whisper cloud API integration using configured API keys.
                  </p>
                </div>
              </label>
            </div>
          </div>

          {/* UBAG Provider Configuration */}
          <div className="rp-panel">
            <div className="rp-panel-title" style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
              <Cloud size={18} className="text-accent" />
              UBAG Model Configuration
            </div>
            <p className="rp-page-sub" style={{ marginBottom: '16px' }}>
              Select the audio AI model used when UBAG Cloud Provider is active.
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
              <label htmlFor="ubag-model-select" style={{ fontWeight: 500, fontSize: '14px' }}>
                UBAG Audio Model:
              </label>
              <select
                id="ubag-model-select"
                className="rp-input"
                value={ubagModel}
                onChange={(e) => setUbagModel(e.target.value)}
                data-testid="ubag-model-select"
                style={{ maxWidth: '360px' }}
              >
                <option value="ubag-gemini-audio">UBAG Gemini Audio</option>
                <option value="ubag-chatgpt-audio">UBAG ChatGPT Audio</option>
              </select>
            </div>
          </div>

          {/* Radiology System Prompt Panel */}
          <div className="rp-panel">
            <div className="rp-panel-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '12px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <Sliders size={18} className="text-accent" />
                Radiology System Prompt
              </div>
              <button
                type="button"
                className="ghost sm"
                onClick={handleResetPrompt}
                data-testid="reset-prompt-btn"
                style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}
              >
                <RefreshCw size={14} /> Reset to Default
              </button>
            </div>
            <p className="rp-page-sub" style={{ marginBottom: '12px' }}>
              System instructions passed to the transcription & cleanup LLM pass for dictation formatting.
            </p>

            <textarea
              className="rp-input"
              rows={5}
              value={radiologySystemPrompt}
              onChange={(e) => setRadiologySystemPrompt(e.target.value)}
              data-testid="radiology-system-prompt"
              placeholder="Enter radiology system prompt instructions..."
              style={{ fontFamily: 'monospace', fontSize: '13px', lineHeight: '1.5' }}
            />
          </div>
        </div>

        {/* Aside / Help panel */}
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">Engine Defaults</div>
            <p>
              <strong>medASR 6-gram</strong> is pre-trained on medical radiology corpora with a 4.4% Word Error Rate (WER).
            </p>
          </div>

          <div className="rp-help">
            <div className="rp-help-title">UBAG Integration</div>
            <p>
              When <strong>UBAG Cloud Provider</strong> is active, audio streams are securely dispatched to UBAG Gemini Audio or ChatGPT Audio endpoints.
            </p>
          </div>

          <div className="rp-help">
            <div className="rp-help-title">System Prompts</div>
            <p>
              Custom radiology prompts guide structured section formatting and anatomical term corrections during dictation processing.
            </p>
          </div>
        </aside>
      </div>
    </div>
  );
}
