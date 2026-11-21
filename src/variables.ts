export type VariableKind = 'email' | 'password' | 'phone' | 'text';

interface ProfileVariable {
  kind: VariableKind;
  value: string;
}

export function readVariables(charter: string): Map<string, ProfileVariable> {
  const raw = process.env.QA_PROFILE_VARIABLES;
  delete process.env.QA_PROFILE_VARIABLES;
  const names = new Set([...charter.matchAll(/\{\{([a-z][a-z0-9_-]*)\}\}/gi)].map(match => match[1].toLowerCase()));
  if (!names.size) return new Map();

  const saved = JSON.parse(raw ?? '{}') as Record<string, ProfileVariable>;
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
    case 'phone': return fieldType === 'tel' || (fieldType === 'text' && /phone|mobile|telephone/.test(label));
    case 'text': return fieldType === 'text' && !/password|passcode|verification code/.test(label);
  }
}

export function redactVariables(text: string, variables: Map<string, ProfileVariable>): string {
  const forms = [...variables.values()].flatMap(({ value }) => [
    value, JSON.stringify(value).slice(1, -1), encodeURIComponent(value),
  ]).sort((first, second) => second.length - first.length);
  for (const value of new Set(forms)) text = text.replaceAll(value, '[profile value]');
  return text;
}
