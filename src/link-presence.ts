const inspection = /\b(?:verify|check|confirm|look for|find|make sure|assert|ensure)\b/i;
const presence = /\b(?:present|visible|exists?|displayed|shown|available|presence of|look for|has|contains|includes)\b/i;
const action = /(?:^|[.,;!?]|\b(?:and(?: then)?|then)\b)\s*(?:please\s+)?(?:click|open|follow|navigate|visit|tap|go to|scroll|search|sign[ -]?(?:in|up)|log[ -]?in|fill|submit|select|enter|type)\b|\b(?:by (?:clicking|following|opening|navigating|scrolling)|can (?:click|open|follow|visit))\b/i;
const destination = /\b(?:where (?:it|the link) (?:goes|leads)|(?:link|destination) (?:works|loads|opens)|link destination)\b/i;

export function linkPresenceIntent(charter: string): { readOnly: boolean; name?: string; footer?: boolean } {
  const footerCheck = /^(?:(?:on|from) (?:the |this |current )?(?:home\s*page|page)\s*,?\s+)?(?:scroll to (?:the )?(?:bottom|footer)(?: of the (?:home\s*page|page))?(?:\s*,\s*(?:then\s+)?|\s+and(?: then)?\s+|\s+then\s+))?(?:verify|check|confirm|make sure|ensure|assert)\s+(?:that\s+|whether\s+|if\s+)?(?:the\s+)?(?:(?:home\s*page\s+)?footer links\s+(?:are\s+(?:present|visible|displayed|shown|available)|exist)|(?:home\s*page\s+)?footer\s+(?:has|contains|includes)\s+(?:visible\s+)?links)(?:\s+on\s+(?:the |this |current )?(?:home\s*page|page))?[.!?]*$/i;
  if (footerCheck.test(charter.trim())) return { readOnly: true, footer: true };
  const quotes = /["“]([^"”]+)["”]|'([^']+)'/g;
  const plain = charter.replace(quotes, ' LINK_LABEL ');
  const clauses = plain.split(/[,;.!?]+|\b(?:and|then)\s+(?=(?:click|open|follow|navigate|visit|tap|verify|check|confirm|look|find)\b)/i);
  const requested = clauses.some(clause => /\blinks?\b/i.test(clause) && inspection.test(clause)
    && (presence.test(clause) || /\b(?:check for|find)\b/i.test(clause)) && !action.test(clause));
  if (!requested) return { readOnly: false };

  const readOnly = !action.test(plain) && !destination.test(plain);
  if (!readOnly) return { readOnly };

  const check = charter.trim().match(
    /^(?:please\s+)?(?:verify|check|confirm|make sure|ensure|assert)\s+(?:that\s+|whether\s+|if\s+)?(?:there is\s+)?(?:the\s+|a\s+)?(.+?)\s+(?:is\s+(?:present|visible|displayed|shown|available)|exists?)(?:\s+on\s+(?:the|this|current)\s+(?:home\s*page|page))?[.!?]*$/i,
  )?.[1];
  const rawName = check?.match(/^links?\s+(?:to\s+|named\s+|labelled\s+|labeled\s+|called\s+)?(.+)$/i)?.[1]
    ?? check?.match(/^(.+?)\s+link$/i)?.[1];
  if (rawName && /\b(?:and|or)\b/i.test(rawName.replace(quotes, ''))) return { readOnly };
  const name = rawName?.trim().replace(/^["“']|["”']$/g, '');
  return { readOnly, name };
}
