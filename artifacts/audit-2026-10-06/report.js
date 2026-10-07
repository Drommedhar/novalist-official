'use strict';
const cards = Array.from(document.querySelectorAll('.finding'));
const list = document.getElementById('findings');
const controls = Object.fromEntries(['search', 'priority', 'area', 'platform', 'evidence', 'effort', 'sort'].map(id => [id, document.getElementById(id)]));
const allData = JSON.parse(document.getElementById('audit-data').textContent);
const normalizedSearch = new Map(cards.map(card => [card, card.textContent.toLocaleLowerCase()]));

function matches(card) {
  const query = controls.search.value.trim().toLocaleLowerCase();
  return (!query || query.split(/\s+/).every(word => normalizedSearch.get(card).includes(word))) &&
    (!controls.priority.value || card.dataset.priority === controls.priority.value) &&
    (!controls.area.value || card.dataset.area === controls.area.value) &&
    (!controls.platform.value || card.dataset.platform.includes(controls.platform.value)) &&
    (!controls.evidence.value || card.dataset.evidence === controls.evidence.value) &&
    (!controls.effort.value || card.dataset.effort === controls.effort.value);
}

function applyFilters() {
  const order = controls.sort.value;
  const sorted = [...cards].sort((a, b) => {
    if (order === 'effort') return ('SML'.indexOf(a.dataset.effort) - 'SML'.indexOf(b.dataset.effort)) || a.dataset.priority.localeCompare(b.dataset.priority);
    if (order === 'area') return a.dataset.area.localeCompare(b.dataset.area) || a.dataset.priority.localeCompare(b.dataset.priority);
    return a.dataset.priority.localeCompare(b.dataset.priority) || a.dataset.rank.localeCompare(b.dataset.rank);
  });
  let count = 0;
  for (const card of sorted) {
    card.hidden = !matches(card);
    if (!card.hidden) count++;
    list.appendChild(card);
  }
  document.getElementById('result-count').textContent = `${count} of ${cards.length} findings`;
  document.getElementById('empty').hidden = count > 0;
}

function resetFilters() {
  for (const [id, control] of Object.entries(controls)) control.value = id === 'sort' ? 'priority' : '';
}

for (const control of Object.values(controls)) control.addEventListener('input', applyFilters);
document.getElementById('reset').addEventListener('click', () => { resetFilters(); applyFilters(); });
for (const button of document.querySelectorAll('[data-preset]')) {
  button.addEventListener('click', () => {
    resetFilters();
    const preset = button.dataset.preset;
    if (preset === 'urgent') controls.priority.value = 'P1';
    if (preset === 'quick') controls.effort.value = 'S';
    if (preset === 'mobile') controls.platform.value = 'mobile';
    if (preset === 'reproduced') controls.evidence.value = 'reproduced';
    applyFilters();
  });
}

document.getElementById('expand').addEventListener('click', () => { for (const card of cards) if (!card.hidden) card.open = true; });
document.getElementById('collapse').addEventListener('click', () => { for (const card of cards) card.open = false; });
document.getElementById('download').addEventListener('click', () => {
  const shown = new Set(cards.filter(card => !card.hidden).map(card => card.id));
  const payload = { ...allData, findings: allData.findings.filter(finding => shown.has(finding.id)) };
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'novalist-audit-2026-10-06.json';
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});

let prePrintOpen = [];
window.addEventListener('beforeprint', () => {
  prePrintOpen = cards.filter(card => card.open);
  for (const card of cards) if (!card.hidden) card.open = true;
});
window.addEventListener('afterprint', () => {
  for (const card of cards) card.open = prePrintOpen.includes(card);
});
document.getElementById('print').addEventListener('click', () => window.print());

function revealHash() {
  const card = document.getElementById(decodeURIComponent(location.hash.slice(1)));
  if (!card || !card.classList.contains('finding')) return;
  resetFilters();
  applyFilters();
  card.open = true;
  card.scrollIntoView({ block: 'start' });
}
window.addEventListener('hashchange', revealHash);
document.addEventListener('keydown', event => {
  const target = event.target;
  if (event.key === '/' && !event.ctrlKey && !event.metaKey && !event.altKey &&
      !(target instanceof HTMLElement && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)))) {
    event.preventDefault();
    controls.search.focus();
  }
});
applyFilters();
revealHash();
