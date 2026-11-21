export type VariableKind = 'email' | 'password' | 'phone' | 'otp' | 'pin' | 'text';

interface ProfileVariable {
  kind: VariableKind;
  value: string;
}

export function readVariables(charter: string): Map<string, ProfileVariable> {
  const raw = process.env.QA_PROFILE_VARIABLES;
  delete process.env.QA_PROFILE_VARIABLES;
  const names = new Set([...charter.matchAll(/\{\{([a-z][a-z0-9_-]*)\}\}/gi)].map(match => match[1].toLowerCase()));
  const groups = [...charter.matchAll(/\b(?:using|with)\s+(?:the\s+)?([a-z][a-z0-9_-]*)\s+credentials\b/gi)]
    .map(match => match[1].toLowerCase());
  if (!names.size && !groups.length) return new Map();

  const saved = JSON.parse(raw ?? '{}') as Record<string, { value?: unknown }>;
  for (const group of groups) {
    const matches = Object.keys(saved).filter(name => name.startsWith(`${group}_`) || name.startsWith(`${group}-`));
    if (!matches.length) throw new Error(`No saved profile variables match "${group} credentials". Use names such as ${group}_email and ${group}_password.`);
    for (const name of matches) names.add(name);
  }
  return new Map([...names].map(name => {
    const entry = Object.hasOwn(saved, name) ? saved[name] : undefined;
    if (typeof entry?.value !== 'string' || !entry.value) {
      throw new Error(`No saved profile variable named {{${name}}} is available for this run.`);
    }
    return [name, { kind: inferVariableKind(name), value: entry.value }];
  }));
}

function inferVariableKind(name: string): VariableKind {
  if (/(?:^|[_-])(?:phone|mobile|telephone)(?:[_-]?number)?$/i.test(name)) return 'phone';
  if (/(?:^|[_-])e[_-]?mail(?:[_-]?address)?$/i.test(name)) return 'email';
  if (/(?:^|[_-])(?:password|passwd)$/i.test(name)) return 'password';
  if (/(?:^|[_-])(?:otp|one[_-]?time[_-]?code|verification[_-]?code)$/i.test(name)) return 'otp';
  if (/(?:^|[_-])pin$/i.test(name)) return 'pin';
  return 'text';
}

export function acceptsVariable(kind: VariableKind, fieldType: string, name: string, variableName = ''): boolean {
  const label = name.toLowerCase();
  const verificationField = fieldType === 'one-time-code' || /\botp\b|verification code|one[ -]time (?:code|password)/i.test(label);
  const pinField = /\bpin\b/i.test(label);
  if (verificationField || pinField) return verificationField ? kind === 'otp' : kind === 'pin';
  switch (kind) {
    case 'password': return fieldType === 'password';
    case 'email': return fieldType === 'email' || (fieldType === 'text' && /e[ -]?mail|user(name)?|login/.test(label));
    case 'phone': return fieldType === 'tel' || (['text', 'number'].includes(fieldType) && /phone|mobile|telephone/.test(label));
    case 'text': {
      const field = label.trim().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
      const key = variableName.replaceAll('-', '_');
      return fieldType === 'text' && !!field && (key === field || key.endsWith(`_${field}`))
        && !/password|passcode|phone|mobile|telephone/.test(label);
    }
    case 'otp':
    case 'pin': return false;
  }
}

export function redactVariables(text: string, variables: Map<string, ProfileVariable>): string {
  const forms = [...variables.values()].flatMap(({ value, kind }) => {
    const values = [value, JSON.stringify(value).slice(1, -1), encodeURIComponent(value)];
    if (kind === 'phone') {
      const digits = value.replace(/\D/g, '');
      if (digits.length >= 6) values.push(digits);
      if (value.trim().startsWith('+') || /^1\d{10}$/.test(digits)) {
        for (let length = 1; length <= 3 && digits.length - length >= 6; length++) {
          values.push(digits.slice(length));
        }
      }
    }
    return values;
  }).sort((first, second) => second.length - first.length);
  for (const value of new Set(forms)) text = text.replaceAll(value, '[profile value]');
  return text;
}
