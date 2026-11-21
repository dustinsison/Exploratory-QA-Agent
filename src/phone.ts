import type { Locator, Page } from 'playwright';

export const phoneCountryLabel = /(?:phone|mobile|telephone).*(?:country|dial)|(?:country|dial).*(?:phone|calling|code)/i;

export class PhoneInputError extends Error {}

export async function fillSavedPhone(page: Page, target: Locator, value: string, origin: string): Promise<string | undefined> {
  const input = value.trim();
  const compact = input.replace(/[\s().-]/g, '');
  if (!/^\+?\d+$/.test(compact)) {
    throw new PhoneInputError('The saved phone value must contain a phone number.');
  }

  // A bare 11-digit number beginning with 1 uses North American dialing.
  const bareNorthAmerican = /^1\d{10}$/.test(compact);
  const international = compact.startsWith('+') || bareNorthAmerican;
  const countries = await page.getByRole('combobox', { name: phoneCountryLabel }).and(page.locator('select:visible')).all();
  if (countries.length > 1) throw new PhoneInputError('More than one phone country selector is visible.');

  let digits = compact.replace(/^\+/, '');
  let note: string | undefined;
  if (countries.length === 1 && international) {
    const country = countries[0];
    const options = await country.evaluate((select: HTMLSelectElement) => Array.from(select.options)
      .filter(option => !option.disabled && !option.closest('optgroup[disabled]'))
      .map(option => ({ label: option.label, value: option.value, selected: option.selected })));
    const matching = options.flatMap(option => {
      const code = option.label.match(/\+(\d{1,3})(?!\d)/)?.[1];
      return code && digits.startsWith(code) && digits.length > code.length ? [{ ...option, code }] : [];
    });
    const longest = Math.max(...matching.map(option => option.code.length));
    const candidates = matching.filter(option => option.code.length === longest);
    const selected = candidates.find(option => option.selected)
      ?? (candidates.length === 1 ? candidates[0] : undefined)
      ?? (bareNorthAmerican ? candidates.find(option => /\bunited states\b/i.test(option.label)) : undefined);
    if (!selected) {
      throw new PhoneInputError('The saved phone number’s dialing code could not be matched to a unique country option.');
    }
    if (!selected.selected) await country.selectOption({ label: selected.label });
    digits = digits.slice(selected.code.length);
    note = `Used dialing code +${selected.code} from the saved phone number (${selected.selected ? 'kept' : 'updated'} the country selector).`;
  }

  if (new URL(page.url()).origin !== origin) throw new PhoneInputError('The phone field is outside this website profile.');
  const type = await target.getAttribute('type');
  const text = countries.length === 0 && international && type !== 'number' ? `+${digits}` : digits;
  await target.click();
  if (new URL(page.url()).origin !== origin) throw new PhoneInputError('The phone field is outside this website profile.');
  if (await target.inputValue()) {
    await target.press('ControlOrMeta+a');
    await target.press('Backspace');
  }
  await target.pressSequentially(text, { delay: 25 });
  return note;
}

export const phoneNextButtonName = /^(continue|next)$/i;

export async function waitForPhoneNextButton(page: Page, url: string, signal: AbortSignal): Promise<'ready' | 'disabled' | undefined> {
  const next = page.getByRole('button', { name: phoneNextButtonName });
  const deadline = Date.now() + 8_000;
  let state: 'disabled' | undefined;
  do {
    if (signal.aborted || page.url() !== url) return;
    state = undefined;
    if (await next.count() === 1 && await next.isVisible()) {
      if (await next.isEnabled({ timeout: 250 }).catch(() => false)) return 'ready';
      state = 'disabled';
    }
    await new Promise(resolve => setTimeout(resolve, 150));
  } while (Date.now() < deadline);
  return state;
}
