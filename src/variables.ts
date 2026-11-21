export type VariableKind = 'email' | 'password' | 'phone' | 'text';

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

  const saved = JSON.parse(raw ?? '{}') as Record<string, ProfileVariable>;
  for (const group of groups) {
    const matches = Object.keys(saved).filter(name => name.startsWith(`${group}_`) || name.startsWith(`${group}-`));
    if (!matches.length) throw new Error(`No saved profile variables match "${group} credentials". Use names such as ${group}_email and ${group}_password.`);
    for (const name of matches) names.add(name);
  }
  return new Map([...names].map(name => {
    const entry = Object.hasOwn(saved, name) ? saved[name] : undefined;
    if (!entry || !['email', 'password', 'phone', 'text'].includes(entry.kind) || !entry.value) {
      throw new Error(`No saved profile variable named {{${name}}} is available for this run.`);
    }
    return [name, entry];
  }));
}

export function acceptsVariable(kind: VariableKind, fieldType: string, name: string): boolean {
  const label = name.toLowerCase();
  switch (kind) {
    case 'password': return fieldType === 'password';
    case 'email': return fieldType === 'email' || (fieldType === 'text' && /e[ -]?mail|user(name)?|login/.test(label));
    case 'phone': return fieldType === 'tel' || (['text', 'number'].includes(fieldType) && /phone|mobile|telephone/.test(label));
    case 'text': return fieldType === 'text' && !/password|passcode|verification code|phone|mobile|telephone/.test(label);
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
