export function escapeHtml(str: string): string {
  return str
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

export function highlightKtoryLine(line: string): string {
  const trimmed = line.trim();
  if (!trimmed) return '<span class="opacity-0 select-none">&nbsp;</span>';

  // 1. Comment
  if (trimmed.startsWith("//")) {
    return `<span class="text-[var(--color-ink-faint)] italic">${escapeHtml(line)}</span>`;
  }

  // 2. Section: === Label ===
  const secMatch = line.match(/^(\s*)(===\s*)([a-zA-Z0-9_]+)(\s*===)(\s*)$/);
  if (secMatch) {
    return `${secMatch[1]}<span class="text-amber-600 dark:text-amber-400 font-semibold font-mono">${escapeHtml(secMatch[2])}</span><span class="text-[var(--color-ink)] font-semibold font-mono">${escapeHtml(secMatch[3])}</span><span class="text-amber-600 dark:text-amber-400 font-semibold font-mono">${escapeHtml(secMatch[4])}</span>${secMatch[5]}`;
  }

  // 3. Container: #choice or #do
  if (trimmed.startsWith("#")) {
    const contMatch = line.match(/^(\s*)(#[a-zA-Z0-9_]+)(\.[a-zA-Z0-9_]+)?(.*)$/);
    if (contMatch) {
      return (
        `${contMatch[1]}<span class="text-purple-600 dark:text-purple-400 font-semibold font-mono">${escapeHtml(contMatch[2])}</span>` +
        (contMatch[3] ? `<span class="text-violet-500 font-mono">${escapeHtml(contMatch[3])}</span>` : "") +
        (contMatch[4] ? `<span class="text-[var(--color-ink-muted)]">${escapeHtml(contMatch[4])}</span>` : "")
      );
    }
  }

  // 4. Choice item: * [Text] -> Target or + [Text]
  const choiceMatch = line.match(/^(\s*)([*+])(\s*)(\[)(.*?)(\])(?:\s*(->|=>)\s*([a-zA-Z0-9_]+))?(.*)$/);
  if (choiceMatch) {
    let out = `${choiceMatch[1]}<span class="text-amber-600 dark:text-amber-400 font-bold font-mono">${escapeHtml(choiceMatch[2])}</span>${choiceMatch[3]}`;
    out += `<span class="text-[var(--color-ink-faint)] font-mono">[</span><span class="text-[var(--color-ink)]">${escapeHtml(choiceMatch[5])}</span><span class="text-[var(--color-ink-faint)] font-mono">]</span>`;
    if (choiceMatch[7] && choiceMatch[8]) {
      out += ` <span class="text-purple-600 dark:text-purple-400 font-semibold font-mono">${escapeHtml(choiceMatch[7])}</span> <span class="text-blue-600 dark:text-blue-400 font-mono font-medium">${escapeHtml(choiceMatch[8])}</span>`;
    }
    if (choiceMatch[9]) out += escapeHtml(choiceMatch[9]);
    return out;
  }

  // 5. Decorator: .tag(args)
  const tagMatch = line.match(/^(\s*)(\.[a-zA-Z0-9_]+)(?:\((.*?)\))?(.*)$/);
  if (tagMatch) {
    let out = `${tagMatch[1]}<span class="text-purple-600 dark:text-purple-400 font-mono">${escapeHtml(tagMatch[2])}</span>`;
    if (tagMatch[3] !== undefined) {
      out += `<span class="text-[var(--color-ink-faint)] font-mono">(</span><span class="text-emerald-700 dark:text-emerald-400 font-mono">${escapeHtml(tagMatch[3])}</span><span class="text-[var(--color-ink-faint)] font-mono">)</span>`;
    }
    if (tagMatch[4]) out += escapeHtml(tagMatch[4]);
    return out;
  }

  // 6. Jump: -> Target or => Target
  const jumpMatch = line.match(/^(\s*)(->|=>)(\s*)([a-zA-Z0-9_]+)(.*)$/);
  if (jumpMatch) {
    return `${jumpMatch[1]}<span class="text-purple-600 dark:text-purple-400 font-semibold font-mono">${escapeHtml(jumpMatch[2])}</span>${jumpMatch[3]}<span class="text-amber-600 dark:text-amber-400 font-mono font-semibold">${escapeHtml(jumpMatch[4])}</span>${escapeHtml(jumpMatch[5])}`;
  }

  // 7. Explicit Colon Narration: : Text
  const narMatch = line.match(/^(\s*)([:：])(.*)$/);
  if (narMatch) {
    return `${narMatch[1]}<span class="text-[var(--color-ink-faint)] font-mono mr-1.5">${escapeHtml(narMatch[2])}</span><span class="text-[var(--color-ink-muted)] italic">${escapeHtml(narMatch[3])}</span>`;
  }

  // 8. Dialogue: Speaker: Text
  const colonIdx = line.indexOf(":");
  if (colonIdx > 0 && !trimmed.startsWith(".")) {
    const speakerPart = line.slice(0, colonIdx);
    const rest = line.slice(colonIdx + 1);
    return `<span class="text-blue-600 dark:text-blue-400 font-medium font-mono">${escapeHtml(speakerPart)}</span><span class="text-[var(--color-ink-faint)] font-mono mr-1.5">:</span><span class="text-[var(--color-ink)]">${escapeHtml(rest)}</span>`;
  }

  return `<span class="text-[var(--color-ink)]">${escapeHtml(line)}</span>`;
}
