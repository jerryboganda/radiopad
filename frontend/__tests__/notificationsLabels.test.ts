import { describe, it, expect } from 'vitest';
import {
  NOTIFICATION_CATEGORIES,
  NOTIFICATION_URGENCIES,
  categoryLabelKey,
  urgencyLabelKey,
} from '@/lib/notifications';
import en from '@/messages/en.json';
import es from '@/messages/es.json';
import de from '@/messages/de.json';
import fr from '@/messages/fr.json';
import pt from '@/messages/pt.json';
import hi from '@/messages/hi.json';

// NOTIF-002 — every category / urgency must resolve to a non-empty TEXT label
// (tone is never the only signal) AND that translation key must exist in EVERY
// locale bundle. This catches i18n drift across the six bundles the moment a new
// category is added without a translation.

const bundles: Record<string, Record<string, unknown>> = { en, es, de, fr, pt, hi };

function resolve(ns: unknown, dottedKey: string): unknown {
  return dottedKey.split('.').reduce<unknown>((o, k) => {
    if (o && typeof o === 'object') return (o as Record<string, unknown>)[k];
    return undefined;
  }, ns);
}

describe('notification label i18n coverage', () => {
  for (const [locale, bundle] of Object.entries(bundles)) {
    const ns = (bundle as { notifications?: unknown }).notifications;

    it(`${locale}: has a text label for every category`, () => {
      for (const category of NOTIFICATION_CATEGORIES) {
        const value = resolve(ns, categoryLabelKey(category));
        expect(typeof value, `${locale} ${category}`).toBe('string');
        expect((value as string).length, `${locale} ${category}`).toBeGreaterThan(0);
      }
    });

    it(`${locale}: has a text label for every urgency`, () => {
      for (const urgency of NOTIFICATION_URGENCIES) {
        const value = resolve(ns, urgencyLabelKey(urgency));
        expect(typeof value, `${locale} ${urgency}`).toBe('string');
        expect((value as string).length, `${locale} ${urgency}`).toBeGreaterThan(0);
      }
    });

    it(`${locale}: has the fallback label for an unknown category`, () => {
      const value = resolve(ns, categoryLabelKey('SomeFutureCategory'));
      expect(typeof value, `${locale} fallback`).toBe('string');
      expect((value as string).length).toBeGreaterThan(0);
    });
  }

  it('maps unknown categories to the generic fallback key', () => {
    expect(categoryLabelKey('SomeFutureCategory')).toBe('category.other');
    expect(categoryLabelKey('AiJob')).toBe('category.aiJob');
  });
});
