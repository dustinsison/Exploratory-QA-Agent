import type { Locator, Page } from 'playwright';

interface BrowserAction {
  kind: 'click' | 'fill' | 'select';
  name: string;
  role: Parameters<Page['getByRole']>[0];
  target: Locator;
  option?: string;
  fieldType?: string;
}

export async function availableActions(page: Page, snapshot: string, includePasswords = false, includePhones = false): Promise<BrowserAction[]> {
  const controls = /^\s*- (button|link|textbox|searchbox|combobox|checkbox|radio|tab|spinbutton)(?: ("(?:[^"\\]|\\.)*"))?(?=\s|:|$)([^\n]*)/gm;
  const actions: BrowserAction[] = [];
  const seen = new Set<string>();

  for (const [, matchedRole, quotedName, state] of snapshot.matchAll(controls)) {
    if (actions.length >= 60) break;
    if (/^\s*(?:\[[^\]]+\]\s*)*\[disabled\]/.test(state)) continue;
    const role = matchedRole as BrowserAction['role'];
    const name: string = quotedName ? JSON.parse(quotedName) : '';
    const key = JSON.stringify([role, name]);
    if (seen.has(key)) continue;
    seen.add(key);
    const target = page.getByRole(role, { name, exact: true });
    if (await target.count() !== 1) continue;

    if (role === 'combobox') {
      const options = await target.evaluate(element => element instanceof HTMLSelectElement
        ? Array.from(element.options)
          .filter(option => !option.disabled && !option.selected && !option.closest('optgroup[disabled]'))
          .map(option => option.label)
        : [], undefined, { timeout: 1_000 }).catch(() => []);
      for (const option of options.slice(0, 60 - actions.length)) {
        actions.push({ kind: 'select', name, role, target, option });
      }
    } else if (role === 'textbox' || role === 'searchbox' || role === 'spinbutton') {
      const fieldType = await fieldInputType(target);
      if (fieldType !== 'password' && await target.isEditable()) {
        const fieldName = name || await target.getAttribute('placeholder', { timeout: 1_000 }) || '';
        actions.push({ kind: 'fill', name: fieldName, role, target, fieldType });
      }
    } else {
      actions.push({ kind: 'click', name, role, target });
    }
  }
  if (includePhones && !actions.some(action => action.fieldType === 'tel')) {
    const phones = page.locator('input:is([type="tel"], [inputmode="tel"], [autocomplete~="tel"], [autocomplete~="tel-national"]):visible');
    for (const target of await phones.all()) {
      if (!await target.isEditable() || await fieldInputType(target) !== 'tel') continue;
      const name = await target.evaluate((element: HTMLInputElement) =>
        element.getAttribute('aria-label') || [...(element.labels ?? [])].map(label => label.textContent).join(' ').trim()
        || element.placeholder || 'Phone number', undefined, { timeout: 1_000 });
      actions.push({ kind: 'fill', name, role: 'textbox', target, fieldType: 'tel' });
    }
  }
  if (includePasswords) {
    for (const target of await page.locator('input[type="password"]:visible').all()) {
      if (!await target.isEditable()) continue;
      const name = await target.evaluate((element: HTMLInputElement) =>
        element.getAttribute('aria-label') || [...(element.labels ?? [])].map(label => label.textContent).join(' ').trim()
        || element.placeholder || 'Password', undefined, { timeout: 1_000 });
      actions.push({ kind: 'fill', name, role: 'textbox', target, fieldType: 'password' });
    }
  }
  return actions;
}

export async function fieldInputType(target: Locator): Promise<string> {
  return target.evaluate(element => {
    if (!(element instanceof HTMLInputElement)) return 'text';
    if (['text', 'number', 'tel'].includes(element.type) &&
      (element.inputMode === 'tel' || element.autocomplete.split(' ').some(token => /^tel(?:-|$)/.test(token)))) {
      return 'tel';
    }
    return element.type;
  }, undefined, { timeout: 1_000 });
}
