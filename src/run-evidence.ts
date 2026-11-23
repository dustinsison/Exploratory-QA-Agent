import type { Page } from 'playwright';
import { setTimeout as delay } from 'node:timers/promises';

export type Outcome = 'Pass' | 'Fail' | 'Inconclusive';

export interface CheckResult {
  check: string;
  status: Outcome;
  evidence: string;
}

export interface TimelineEntry {
  atMs: number;
  durationMs: number;
  phase: 'Model' | 'Browser';
  detail: string;
}

export class Checklist {
  private plan: string[] = [];
  private observations: string[] = [];

  constructor(private readonly charter: string) {}

  get checks(): readonly string[] {
    return this.plan;
  }

  setPlan(value: unknown) {
    if (this.plan.length || !Array.isArray(value)) return;
    this.plan = [...new Set(value.flatMap(item =>
      typeof item === 'string' && item.trim() ? [item.trim().slice(0, 240)] : [],
    ))].slice(0, 8);
  }

  observe(snapshot: string) {
    this.observations.push(snapshot);
  }

  finish(results: unknown, steps: string[]): CheckResult[] {
    const entries = Array.isArray(results) ? results : [];
    return this.plan.map((check, index) => {
      const matching = entries.filter(entry => entry?.id === index + 1);
      const result = matching.length === 1 ? matching[0] : undefined;
      const quote = typeof result?.evidence === 'string' ? result.evidence.trim() : '';
      const supported = quote.length >= 3 && quote.length <= 240
        && (this.observations.some(observation => observation.includes(quote))
          || steps.some(step => step.startsWith('Executed ') && step.includes(quote)));
      const status = supported && result?.status === 'pass' ? 'Pass'
        : supported && result?.status === 'fail' ? 'Fail' : 'Inconclusive';
      return { check, status, evidence: status === 'Inconclusive' ? 'No matching evidence was recorded.' : quote };
    });
  }

  fallback(status: Outcome, evidence: string): CheckResult[] {
    return [{ check: this.charter, status, evidence }];
  }
}

export function aggregateChecks(checks: CheckResult[]): Outcome {
  return checks.some(check => check.status === 'Fail') ? 'Fail'
    : checks.length && checks.every(check => check.status === 'Pass') ? 'Pass' : 'Inconclusive';
}

export async function waitForObservation(
  page: Page, value: unknown, beforeUrl: string, beforeSnapshot: string, signal: AbortSignal,
): Promise<string | undefined> {
  if (!value || typeof value !== 'object' || !('type' in value)) return;
  const type = value.type;
  if (type !== 'url-change' && type !== 'text-visible' && type !== 'text-hidden') return;
  const text = 'text' in value && typeof value.text === 'string' ? value.text.trim().slice(0, 180) : '';
  if (type !== 'url-change' && !text) return;
  if (type === 'text-hidden' && !beforeSnapshot.includes(text)) return;
  if (type === 'text-visible' && beforeSnapshot.includes(text)) return;
  const locator = text ? page.getByText(text, { exact: true }).and(page.locator(':visible')).first() : undefined;
  const description = type === 'url-change' ? 'the page URL to change'
    : type === 'text-visible' ? `“${text}” to appear` : `“${text}” to disappear`;
  const expires = Date.now() + 8_000;
  while (!signal.aborted && Date.now() < expires) {
    const matched = type === 'url-change' ? page.url() !== beforeUrl
      : (await locator!.isVisible().catch(() => false)) === (type === 'text-visible');
    if (matched) return `Observed ${description.replace(' to ', ' ')}.`;
    await delay(150, undefined, { signal }).catch(error => { if (!signal.aborted) throw error; });
  }
  if (!signal.aborted) return `Timed out waiting for ${description}.`;
  return undefined;
}
