import { readFileSync } from 'node:fs';

function styleFrom(path) {
  const source = readFileSync(path, 'utf8');
  const match = source.match(/<style>([\s\S]*?)<\/style>/);
  if (!match) throw new Error(`No stylesheet found in ${path}`);
  return match[1].trim();
}

function blocks(css) {
  const result = [];
  let start = 0;
  let depth = 0;
  for (let index = 0; index < css.length; index += 1) {
    if (css.startsWith('/*', index)) {
      index = css.indexOf('*/', index + 2);
      if (index < 0) throw new Error('Unterminated CSS comment');
      continue;
    }
    if (css[index] === '{') depth += 1;
    if (css[index] !== '}') continue;
    depth -= 1;
    if (depth !== 0) continue;
    result.push(css.slice(start, index + 1).trim());
    start = index + 1;
  }
  if (css.slice(start).trim()) throw new Error('Unparsed CSS');
  return result;
}

function normalizedHeader(block) {
  return block
    .slice(0, block.indexOf('{'))
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\s+/g, ' ')
    .replace(/"/g, "'")
    .trim();
}

function selectorKeys(css, prefix = '') {
  return blocks(css).flatMap((block) => {
    const header = normalizedHeader(block);
    const body = block.slice(block.indexOf('{') + 1, -1);
    return header.startsWith('@media')
      ? selectorKeys(body, `${prefix}${header}|`)
      : [`${prefix}${header}`];
  });
}

const selectorAliases = new Map([
  ['.nav button.icon', '.icon'],
]);
const retainedSelectors = new Set([
  'html, body, #root',
  '[hidden]',
  '.btn:disabled, .chipset button:disabled',
  '.range',
  '.range.unset',
  '.ticks',
  '.hint',
]);
const sourceSelectors = new Set([
  ...selectorKeys(styleFrom('docs/prototypes/ui-screens.prototype.html')),
  ...selectorKeys(styleFrom('docs/prototypes/schedule-editing.prototype.html')),
]);
const actualSelectors = new Set(selectorKeys(readFileSync('src/TaskGuide.Web/src/index.css', 'utf8')));
const missing = [...sourceSelectors].filter((selector) =>
  !actualSelectors.has(selectorAliases.get(selector) ?? selector),
);
const allowedSelectors = new Set([
  ...[...sourceSelectors].map((selector) => selectorAliases.get(selector) ?? selector),
  ...retainedSelectors,
]);
const unexpected = [...actualSelectors].filter((selector) => !allowedSelectors.has(selector));

if (missing.length > 0 || unexpected.length > 0) {
  const errors = [];
  if (missing.length > 0) errors.push(`missing prototype selectors:\n${missing.join('\n')}`);
  if (unexpected.length > 0) errors.push(`unexpected selectors:\n${unexpected.join('\n')}`);
  throw new Error(`index.css ${errors.join('\n\n')}`);
}

console.log('index.css covers the ui-screens and schedule-editing prototype union.');
