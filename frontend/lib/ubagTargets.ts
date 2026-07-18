/**
 * Fallback UBAG browser-automation target list, shown while
 * `GET /api/ubag/status` hasn't resolved (or reports no targets). Single
 * source of truth for every surface — mirrors the backend's default
 * `RADIOPAD_UBAG_ALLOWED_TARGETS`; the live status response always wins.
 */
export const FALLBACK_UBAG_TARGETS: string[] = ['chatgpt_web', 'gemini_web', 'deepseek_web', 'mock'];
