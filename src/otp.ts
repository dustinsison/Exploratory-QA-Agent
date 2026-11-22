import type { Locator, Page } from 'playwright';

const verificationContext = /\botp\b|verification code|one[ -]time code|verify (?:your )?(?:mobile|phone|email)|(?:6|six)[ -]digit code/i;

export async function findOtpSlots(page: Page, snapshot: string): Promise<Locator | undefined> {
  if (!verificationContext.test(snapshot)) return;
  const slots = page.locator('input[maxlength="1"]:visible:not([type="hidden"]):not([disabled]):not([readonly])');
  const count = await slots.count();
  if (count >= 4 && count <= 10) return slots;
}

export async function enterOtp(target: Locator, slots: Locator | undefined, code: string, origin: string): Promise<void> {
  if (!/^\d+$/.test(code)) throw new Error('Saved OTP must contain digits only.');
  if (slots && await slots.count() !== code.length) {
    throw new Error('Saved OTP length does not match the number of verification boxes.');
  }
  const typeDigitSequence = async (field: Locator, value: string) => {
    if (new URL(field.page().url()).origin !== origin) throw new Error('OTP field is outside the website profile.');
    await field.click();
    if (new URL(field.page().url()).origin !== origin) throw new Error('OTP field is outside the website profile.');
    if (await field.inputValue() !== '') await field.press('ControlOrMeta+a');
    await field.pressSequentially(value, { delay: 40 });
  };
  if (!slots) {
    await typeDigitSequence(target, code);
    return;
  }
  const originalUrl = target.page().url();
  for (const [index, digit] of [...code].entries()) {
    if (target.page().url() !== originalUrl || await slots.count() !== code.length) {
      throw new Error('The verification form changed during OTP entry.');
    }
    await typeDigitSequence(slots.nth(index), digit);
  }
}
